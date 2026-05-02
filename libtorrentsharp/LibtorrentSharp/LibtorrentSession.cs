// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
// csdl - a cross-platform libtorrent wrapper for .NET
// Licensed under Apache-2.0 - see the license file for more information

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using LibtorrentSharp.Alerts;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp;

/// <summary>
/// Represents a client that can register and control torrents download/uploads
/// </summary>
public class LibtorrentSession : IDisposable
{
    internal const AlertCategories RequiredAlertCategories =
        AlertCategories.Error | AlertCategories.Status | AlertCategories.Storage | AlertCategories.PortMapping | AlertCategories.DHT | AlertCategories.Tracker;

    // Keyed by SHA-1 (v1) info-hash. v2-only torrents aren't keyed here because the
    // current C ABI surfaces sha1 buffers in alerts; supporting v2-only handles is a
    // future follow-up alongside the v2 alert path.
    private readonly ConcurrentDictionary<Sha1Hash, TorrentHandle> _attachedManagers = new();

    // Diagnostic: counts peer-alert marshaling exceptions silently swallowed by ProxyRaisedEvent.
    private string? _lastPeerAlertException;
    private int _peerAlertExceptionCount;
    private int _peerAlertAttemptCount;
    public string? LastPeerAlertException => _lastPeerAlertException;
    public int PeerAlertExceptionCount => System.Threading.Volatile.Read(ref _peerAlertExceptionCount);
    public int PeerAlertAttemptCount => System.Threading.Volatile.Read(ref _peerAlertAttemptCount);

    private readonly int[] _alertTypeHistogram = new int[68];
    public string AlertTypeHistogram()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _alertTypeHistogram.Length; i++)
        {
            var n = System.Threading.Volatile.Read(ref _alertTypeHistogram[i]);
            if (n > 0) sb.Append($"{i}:{n} ");
        }
        return sb.ToString().TrimEnd();
    }

    // magnet-added torrents we still own but haven't wired into TorrentHandle
    // (pending metadata-from-alert integration). Tracked here so Dispose can detach
    // them — and so DetachMagnet can release one without double-freeing the native
    // handle on shutdown. The byte value is unused; we just need set semantics with
    // O(1) removal, which ConcurrentBag doesn't provide.
    private readonly ConcurrentDictionary<IntPtr, byte> _magnetHandles = new();

    // need to keep a reference to the delegate to prevent GC invalidating it
    private readonly NativeMethods.SessionEventCallback _eventCallback;
    private readonly IntPtr _handle;

    // Bounded alert pipe. The native callback writes from a libtorrent thread; the
    // single managed consumer reads via Alerts. DropOldest keeps a slow consumer from
    // blowing up memory if alerts arrive faster than they can be processed.
    private readonly Channel<Alert> _alerts = Channel.CreateBounded<Alert>(
        new BoundedChannelOptions(capacity: 1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    private bool _disposed;
    private bool _includeUnmappedEvents;

    /// <summary>
    /// Creates a new instance of <see cref="LibtorrentSession"/> with default settings.
    /// </summary>
    public LibtorrentSession() : this(new SettingsPack())
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="LibtorrentSession"/> with the provided configuration.
    /// </summary>
    public LibtorrentSession(LibtorrentSessionConfig config) : this(config.Build())
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="LibtorrentSession"/> with the provided settings pack (advanced usage).
    /// </summary>
    public unsafe LibtorrentSession(SettingsPack pack)
    {
        ValidateSettingsPack(pack, ensureAlertMask: true);

        var packHandle = pack.BuildNative();

        try
        {
            _handle = NativeMethods.CreateSession(packHandle.ToPointer());

            if (_handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create session.");
            }

            _eventCallback = ProxyRaisedEvent;
            NativeMethods.SetEventCallback(_handle, _eventCallback, true);
        }
        finally
        {
            NativeMethods.FreeSettingsPack(packHandle);
        }
    }

    // Adopts a native session handle constructed by an alternate path
    // (currently only FromState). The caller has already validated the handle
    // is non-zero. Wires up the alert callback identically to the
    // settings-pack constructor.
    private LibtorrentSession(IntPtr nativeHandle)
    {
        _handle = nativeHandle;
        _eventCallback = ProxyRaisedEvent;
        NativeMethods.SetEventCallback(_handle, _eventCallback, true);
    }

    /// <summary>
    /// Constructs a new session restored from a previously-captured state
    /// buffer (as produced by <see cref="SaveState"/>). The state captures
    /// settings, the DHT routing table, and any plugin-specific state — but
    /// not torrents, which are restored separately via fast-resume.
    /// </summary>
    /// <remarks>
    /// The saved alert_mask is preserved as-is; the binding's required
    /// internal alert categories (<see cref="RequiredAlertCategories"/>) are
    /// force-applied on top to keep the alert pump healthy. Callers that
    /// stored a wider alert_mask should re-apply their custom mask via
    /// <see cref="UpdateSettings"/> after construction.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="state"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">libtorrent rejected the buffer (malformed bencoding, etc.).</exception>
    public static LibtorrentSession FromState(byte[] state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Length == 0)
        {
            throw new ArgumentException("State buffer must not be empty.", nameof(state));
        }

        var nativeHandle = NativeMethods.CreateSessionFromState(state, state.Length);
        if (nativeHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to construct session from state — buffer may be malformed.");
        }

        var session = new LibtorrentSession(nativeHandle);

        // Restored alert_mask is whatever was saved, which may omit categories
        // the binding's alert pump depends on. Force the required set on top.
        var pack = new SettingsPack();
        pack.Set("alert_mask", (int)RequiredAlertCategories);
        session.UpdateSettings(pack);

        return session;
    }

    /// <summary>
    /// Captures the session's current state as a bencoded byte buffer suitable
    /// for restoring later via <see cref="FromState"/>. Includes settings, the
    /// DHT routing table, and plugin state — not torrents (use per-torrent
    /// fast-resume for those).
    /// </summary>
    /// <returns>The state blob; never null. Empty only if libtorrent failed to serialize.</returns>
    public byte[] SaveState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        NativeMethods.SessionSaveState(_handle, out var bufPtr, out var bufLen);
        if (bufPtr == IntPtr.Zero || bufLen <= 0)
        {
            return Array.Empty<byte>();
        }

        try
        {
            var managed = new byte[bufLen];
            Marshal.Copy(bufPtr, managed, 0, bufLen);
            return managed;
        }
        finally
        {
            NativeMethods.FreeSessionStateBuf(bufPtr);
        }
    }

    /// <summary>
    /// The TCP port the session is actually listening on. May differ from the
    /// value configured via <c>listen_interfaces</c> when the requested port
    /// was 0 (OS-assigned) or when the requested port was already in use.
    /// Returns 0 when no listen sockets are open (libtorrent failed to bind).
    /// </summary>
    public ushort ListenPort
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return NativeMethods.SessionListenPort(_handle);
        }
    }

    /// <summary>The SSL/TLS port when SSL is enabled; 0 otherwise.</summary>
    public ushort SslListenPort
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return NativeMethods.SessionSslListenPort(_handle);
        }
    }

    /// <summary>
    /// Whether the session has at least one open listen socket. Pair with the
    /// listen_succeeded/failed alerts (follow-up slice) for event-driven
    /// tracking.
    /// </summary>
    public bool IsListening
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return NativeMethods.SessionIsListening(_handle) != 0;
        }
    }

    /// <summary>
    /// Updates the session's <c>listen_interfaces</c> setting, asking libtorrent
    /// to rebind its listen sockets to the specified interfaces and ports.
    /// Format matches libtorrent's canonical syntax — a comma-separated list of
    /// <c>interface:port</c> entries (with optional <c>s</c> suffix for SSL),
    /// e.g. <c>"0.0.0.0:6881,[::]:6881"</c> or <c>"eth0:6881,wlan0:6881s"</c>.
    /// Apply is asynchronous; use <see cref="ListenPort"/> / <see cref="IsListening"/>
    /// to observe the resulting state.
    /// </summary>
    public void SetListenInterfaces(string interfaces)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(interfaces);

        var pack = new SettingsPack();
        pack.Set("listen_interfaces", interfaces);
        UpdateSettings(pack);
    }

    /// <summary>
    /// Reads back the session's currently-effective <c>listen_interfaces</c>
    /// setting string. Returns the comma-separated list libtorrent is using to
    /// pick bind targets — useful for confirming a <see cref="SetListenInterfaces"/>
    /// took effect, or for round-tripping the setting through state-IO.
    /// </summary>
    public string GetListenInterfaces()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        NativeMethods.SessionGetListenInterfaces(_handle, out var bufPtr, out var bufLen);
        if (bufPtr == IntPtr.Zero || bufLen <= 0)
        {
            return string.Empty;
        }

        try
        {
            return Marshal.PtrToStringUTF8(bufPtr, bufLen) ?? string.Empty;
        }
        finally
        {
            NativeMethods.FreeListenInterfacesBuf(bufPtr);
        }
    }

    ~LibtorrentSession()
    {
        Dispose();
    }

    /// <summary>
    /// Gets the active torrents currently attached to the session.
    /// </summary>
    public IEnumerable<TorrentHandle> ActiveTorrents => _attachedManagers.Values;

    /// <summary>
    /// Whether to include events that only produce a <see cref="Alert"/> with no additional data.
    /// </summary>
    /// <remarks>
    /// Changing this value after subscribing will cause the event callback to be reset.
    /// </remarks>
    public bool IncludeUnmappedEvents
    {
        get => _includeUnmappedEvents;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _includeUnmappedEvents = value;

            // reset event callback if set
            if (_eventCallback != null)
            {
                NativeMethods.ClearEventCallback(_handle);
                NativeMethods.SetEventCallback(_handle, _eventCallback, value);
            }
        }
    }

    /// <summary>
    /// Gets or sets the default path to save downloaded torrents to.
    /// If a torrent is attached with a relative save path and this property is set, the save path will be combined with this property.
    /// </summary>
    public string DefaultDownloadPath { get; set; } = Path.Combine(Environment.CurrentDirectory, "downloads");

    /// <summary>
    /// Async stream of session alerts. Single-consumer — the channel under the hood is
    /// configured with <c>SingleReader = true</c>; iterating from multiple call sites
    /// concurrently is undefined behavior. Iteration completes naturally when the
    /// session is disposed (the channel's writer is completed during <see cref="Dispose"/>).
    /// </summary>
    /// <remarks>
    /// Alerts arrive on a libtorrent native thread and queue into a bounded channel
    /// (capacity 1024, drop-oldest). Slow consumers lose the oldest alerts rather than
    /// the producer blocking — alerts are inherently transient signals; backpressure
    /// would risk starving the native side.
    /// </remarks>
    public IAsyncEnumerable<Alert> Alerts => _alerts.Reader.ReadAllAsync();

    /// <summary>
    /// Applies a settings pack to the session, updating the current configuration at some point in the future.
    /// </summary>
    public void UpdateSettings(SettingsPack pack)
    {
        ValidateSettingsPack(pack);

        var packHandle = pack.BuildNative();

        try
        {
            NativeMethods.ApplySettingsPack(_handle, packHandle);
        }
        finally
        {
            NativeMethods.FreeSettingsPack(packHandle);
        }
    }

    /// <summary>
    /// Adds a torrent to the session. Dispatches on <see cref="AddTorrentParams"/> — the
    /// caller fills exactly one of <see cref="AddTorrentParams.MagnetUri"/>,
    /// <see cref="AddTorrentParams.TorrentInfo"/>, or <see cref="AddTorrentParams.ResumeData"/>.
    /// </summary>
    /// <returns>
    /// <see cref="AddTorrentResult"/> carrying either a <see cref="TorrentHandle"/>
    /// (file source) or a <see cref="MagnetHandle"/> (magnet / resume source).
    /// </returns>
    /// <exception cref="ArgumentException">Parameters fail validation, or source-specific preconditions fail (e.g. empty magnet URI).</exception>
    /// <exception cref="InvalidOperationException">File-source attach failed or the torrent was already attached.</exception>
    public AddTorrentResult Add(AddTorrentParams parameters)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(parameters);

        return parameters.ResolveSource() switch
        {
            AddTorrentSource.TorrentInfo => new AddTorrentResult(AttachTorrentInternal(parameters.TorrentInfo!, parameters.SavePath)),
            AddTorrentSource.Magnet => new AddTorrentResult(AddMagnetInternal(parameters.MagnetUri!, parameters.SavePath)),
            AddTorrentSource.Resume => new AddTorrentResult(AddWithResumeInternal(parameters.ResumeData!, parameters.SavePath)),
            _ => throw new ArgumentOutOfRangeException(nameof(parameters)),
        };
    }

    private TorrentHandle AttachTorrentInternal(TorrentInfo torrent, string? savePath)
    {
        var key = RequireV1Key(torrent.Metadata.Hashes);
        if (_attachedManagers.ContainsKey(key))
        {
            throw new InvalidOperationException("Torrent is already attached to this session.");
        }

        var resolvedSavePath = ResolveSavePath(savePath);

        // Pre-register the manager with a placeholder native handle so the
        // dispatcher's AddTorrent case finds it even when add_torrent_alert
        // fires synchronously on the alert thread before AttachTorrent
        // returns. Closes the -documented AddTorrentAlert race
        // (Subject==null on the alert wrapper) at the source — the // test tolerance stays as defense-in-depth.
        var manager = new TorrentHandle(IntPtr.Zero, resolvedSavePath, torrent);
        _attachedManagers.TryAdd(key, manager);

        var handle = NativeMethods.AttachTorrent(_handle, torrent.InfoHandle, resolvedSavePath);

        if (handle == IntPtr.Zero)
        {
            // Roll back the pre-registration so the failed add doesn't leave
            // a dangling Zero-handled manager in the map.
            _attachedManagers.TryRemove(key, out _);
            throw new InvalidOperationException("Failed to attach torrent to session.");
        }

        manager.SetNativeHandle(handle);
        return manager;
    }

    private MagnetHandle AddMagnetInternal(string magnetUri, string? savePath)
    {
        if (string.IsNullOrEmpty(magnetUri))
        {
            throw new ArgumentException("Magnet URI must not be null or empty.", nameof(magnetUri));
        }

        var resolvedSavePath = ResolveSavePath(savePath);
        var handle = NativeMethods.AddMagnet(_handle, magnetUri, resolvedSavePath);

        if (handle != IntPtr.Zero)
        {
            _magnetHandles.TryAdd(handle, 0);
        }

        return new MagnetHandle(_handle, handle, resolvedSavePath);
    }

    private MagnetHandle AddWithResumeInternal(byte[] resumeData, string? savePath)
    {
        if (resumeData.Length == 0)
        {
            throw new ArgumentException("Resume data must not be null or empty.", nameof(resumeData));
        }

        var resolvedSavePath = ResolveSavePath(savePath);
        var handle = NativeMethods.AddTorrentWithResume(_handle, resumeData, resumeData.Length, resolvedSavePath);

        if (handle != IntPtr.Zero)
        {
            _magnetHandles.TryAdd(handle, 0);
        }

        return new MagnetHandle(_handle, handle, resolvedSavePath);
    }

    private string ResolveSavePath(string? savePath)
    {
        savePath ??= DefaultDownloadPath;
        if (!Path.IsPathRooted(savePath))
        {
            savePath = Path.Combine(DefaultDownloadPath, savePath);
        }
        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
        }
        return Path.GetFullPath(savePath);
    }

    private static Sha1Hash RequireV1Key(InfoHashes? hashes)
    {
        if (hashes is { V1: { } v1 })
        {
            return v1;
        }
        throw new NotSupportedException(
            "TorrentInfo lacks a v1 (SHA-1) info-hash. v2-only torrents are not supported by the current binding.");
    }

    /// <summary>
    /// Snapshot of port-forwarding mappings registered on this session. libtorrent
    /// 2.x exposes mapping state only via alerts, so the binding accumulates
    /// successful and errored mappings as they arrive and returns the running set.
    /// Returns an empty list until libtorrent emits its first portmap_alert.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<PortMapping> GetPortMappings()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return PortMappingMarshaller.GetMappings(_handle);
    }

    /// <summary>
    /// Requests a DHT routing-table snapshot. The result arrives asynchronously as
    /// a <see cref="DhtStatsAlert"/> on the <see cref="Alerts"/> stream.
    /// </summary>
    public void PostDhtStats()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeMethods.PostDhtStats(_handle);
    }

    /// <summary>
    /// Requests a snapshot of the session's full performance-counter array.
    /// The result arrives asynchronously as a <see cref="SessionStatsAlert"/>
    /// on the <see cref="Alerts"/> stream. The counter array is flat;
    /// resolve metric indices separately via the session_stats_metrics surface
    /// (lands in a follow-up slice) and cache them for reuse.
    /// </summary>
    public void PostSessionStats()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeMethods.PostSessionStats(_handle);
    }

    /// <summary>
    /// Stores <paramref name="data"/> as an immutable BEP44 item on the DHT and
    /// returns the SHA-1 target hash the data will be retrievable under.
    /// The data is wrapped as libtorrent's <c>entry::string_t</c>; the target
    /// is <c>SHA1(bencode(data))</c> and is computed deterministically before
    /// the asynchronous put begins. Network put completion arrives later as a
    /// <see cref="DhtPutAlert"/> on the <see cref="Alerts"/> stream.
    /// </summary>
    public Sha1Hash DhtPutImmutable(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var target = new byte[20];
        NativeMethods.DhtPutImmutableString(_handle, data, data.Length, target);
        return new Sha1Hash(target);
    }

    /// <summary>
    /// Issues an asynchronous DHT lookup for an immutable BEP44 item identified
    /// by <paramref name="target"/>. The result arrives later as a
    /// <see cref="DhtImmutableItemAlert"/> on the <see cref="Alerts"/> stream.
    /// Misses (no peer holds the item) currently fire no alert — they time out
    /// internally.
    /// </summary>
    public void DhtGetImmutable(Sha1Hash target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeMethods.DhtGetImmutable(_handle, target.ToArray());
    }

    /// <summary>
    /// Issues an asynchronous DHT lookup for a mutable BEP44 item identified by
    /// an Ed25519 <paramref name="publicKey"/> (32 bytes) and an optional
    /// <paramref name="salt"/>. The result arrives later as a
    /// <see cref="DhtMutableItemAlert"/> on the <see cref="Alerts"/> stream.
    /// Misses currently fire no alert — they time out internally.
    /// </summary>
    public void DhtGetItemMutable(byte[] publicKey, byte[]? salt = null)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        if (publicKey.Length != 32)
        {
            throw new ArgumentException("Ed25519 public key must be exactly 32 bytes.", nameof(publicKey));
        }
        ObjectDisposedException.ThrowIf(_disposed, this);

        NativeMethods.DhtGetItemMutable(_handle, publicKey, salt ?? Array.Empty<byte>(), salt?.Length ?? 0);
    }

    /// <summary>
    /// Stores <paramref name="value"/> as a mutable BEP44 item on the DHT under
    /// the Ed25519 <paramref name="publicKey"/> + optional <paramref name="salt"/>
    /// at sequence number <paramref name="seq"/>. The bytes are wrapped as
    /// <c>entry::string_t</c>; the BEP44 signature is computed inside the
    /// native shim using <see cref="Ed25519"/>'s sibling helper. The caller is
    /// responsible for picking <paramref name="seq"/> values that monotonically
    /// increase with each update — peers reject stores with stale seq.
    /// Completion arrives later as a <see cref="DhtPutAlert"/> on the
    /// <see cref="Alerts"/> stream with the mutable envelope filled in.
    /// </summary>
    public void DhtPutItemMutable(
        byte[] publicKey,
        byte[] secretKey,
        byte[] value,
        long seq,
        byte[]? salt = null)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        ArgumentNullException.ThrowIfNull(secretKey);
        ArgumentNullException.ThrowIfNull(value);
        if (publicKey.Length != Ed25519.PublicKeySize)
        {
            throw new ArgumentException($"Ed25519 public key must be exactly {Ed25519.PublicKeySize} bytes.", nameof(publicKey));
        }
        if (secretKey.Length != Ed25519.SecretKeySize)
        {
            throw new ArgumentException($"Ed25519 secret key must be exactly {Ed25519.SecretKeySize} bytes.", nameof(secretKey));
        }
        ObjectDisposedException.ThrowIf(_disposed, this);

        NativeMethods.DhtPutItemMutable(
            _handle,
            publicKey,
            secretKey,
            salt ?? Array.Empty<byte>(),
            salt?.Length ?? 0,
            value,
            value.Length,
            seq);
    }

    /// <summary>
    /// Replaces the session's IP filter with the rules from <paramref name="filter"/>.
    /// An empty <see cref="IpFilter"/> clears any previously-set filter.
    /// </summary>
    public unsafe void SetIpFilter(IpFilter filter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(filter);

        var rules = filter.Rules;
        if (rules.Count == 0)
        {
            NativeMethods.SetIpFilter(_handle, null, 0);
            return;
        }

        var buffer = new NativeStructs.IpFilterRule[rules.Count];
        fixed (NativeStructs.IpFilterRule* p = buffer)
        {
            // Pin the whole array first, then write through the pinned pointer.
            // `fixed (byte* = buffer[i].start_ipv6)` per element doesn't reliably
            // write back into the array because the indexer copy semantics for
            // value-typed fixed-size buffers vary across runtimes.
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                WriteV6MappedBytes(rule.Start, p[i].start_ipv6);
                WriteV6MappedBytes(rule.End, p[i].end_ipv6);
                p[i].flags = (uint)rule.Access;
            }

            NativeMethods.SetIpFilter(_handle, p, rules.Count);
        }
    }

    /// <summary>
    /// Exports the session's current IP filter. Returns an empty <see cref="IpFilter"/>
    /// when none has been set. IPv4 ranges arrive as <see cref="System.Net.IPAddress"/>
    /// in v4 form (libtorrent stores them in a separate v4 table; the binding
    /// canonicalizes from the v4-mapped transport representation).
    /// </summary>
    public unsafe IpFilter GetIpFilter()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        NativeMethods.GetIpFilter(_handle, out var native);
        try
        {
            var filter = new IpFilter();
            if (native.length == 0 || native.items == IntPtr.Zero)
            {
                return filter;
            }

            var entrySize = Marshal.SizeOf<NativeStructs.IpFilterRule>();
            var startArr = new byte[16];
            var endArr = new byte[16];
            for (var i = 0; i < native.length; i++)
            {
                var entryPtr = native.items + entrySize * i;
                Marshal.Copy(entryPtr, startArr, 0, 16);
                Marshal.Copy(entryPtr + 16, endArr, 0, 16);
                var flags = (uint)Marshal.ReadInt32(entryPtr + 32);

                filter.AddRule(
                    ReadV6Mapped(startArr),
                    ReadV6Mapped(endArr),
                    (IpFilterAccess)flags);
            }
            return filter;
        }
        finally
        {
            NativeMethods.FreeIpFilter(ref native);
        }
    }

    private static unsafe void WriteV6MappedBytes(System.Net.IPAddress address, byte* dest16)
    {
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            for (var i = 0; i < 16; i++)
            {
                dest16[i] = bytes[i];
            }
            return;
        }

        // IPv4 → v4-mapped v6 (::ffff:0:0/96).
        for (var i = 0; i < 10; i++) dest16[i] = 0;
        dest16[10] = 0xff;
        dest16[11] = 0xff;
        var v4 = address.GetAddressBytes();
        dest16[12] = v4[0];
        dest16[13] = v4[1];
        dest16[14] = v4[2];
        dest16[15] = v4[3];
    }

    private static System.Net.IPAddress ReadV6Mapped(byte[] bytes16)
    {
        var v6 = new System.Net.IPAddress(bytes16);
        return v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6;
    }

    /// <summary>
    /// Detaches a magnet-added torrent from the session. Safe to call once per
    /// <see cref="MagnetHandle"/>; subsequent calls and the <see cref="Dispose"/> sweep
    /// won't double-free the native handle.
    /// </summary>
    public void DetachMagnet(MagnetHandle magnet)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(magnet);

        if (!magnet.IsValid)
        {
            return;
        }

        if (_magnetHandles.TryRemove(magnet.Handle, out _))
        {
            NativeMethods.DetachTorrent(_handle, magnet.Handle, (int)RemoveFlags.None);
        }
    }

    /// <summary>
    /// Requests resume data for a magnet-added torrent. The resulting blob arrives
    /// asynchronously via <see cref="ResumeDataReadyAlert"/>.
    /// </summary>
    public void RequestResumeData(MagnetHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(handle);

        if (!handle.IsValid)
        {
            throw new InvalidOperationException("Cannot request resume data for an invalid handle.");
        }

        NativeMethods.RequestResumeData(handle.Handle);
    }

    /// <summary>
    /// Requests resume data for a file-added torrent. The resulting blob arrives
    /// asynchronously via <see cref="ResumeDataReadyAlert"/>.
    /// </summary>
    public void RequestResumeData(TorrentHandle manager)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(manager);

        NativeMethods.RequestResumeData(manager.TorrentSessionHandle);
    }

    /// <summary>
    /// Detaches a torrent from the session, stopping any ongoing transfers.
    /// </summary>
    /// <param name="manager">The manager to detach</param>
    public void DetachTorrent(TorrentHandle manager) => DetachTorrent(manager, RemoveFlags.None);

    /// <summary>
    /// Detaches a torrent from the session and optionally deletes its on-disk data.
    /// When <paramref name="removeFlags"/> includes <see cref="RemoveFlags.DeleteFiles"/>,
    /// completion surfaces via <see cref="Alerts.FileDeletedAlert"/> /
    /// <see cref="Alerts.FileDeleteFailedAlert"/> before the final
    /// <see cref="Alerts.TorrentRemovedAlert"/>.
    /// </summary>
    public void DetachTorrent(TorrentHandle manager, RemoveFlags removeFlags)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // the manager is fully removed via the alert callback (fires once the torrent is fully removed)
        if (!_attachedManagers.ContainsKey(RequireV1Key(manager.Info.Metadata.Hashes)))
        {
            throw new InvalidOperationException("Unable to detach torrent from session. Ensure the torrent is attached to this session.");
        }

        manager.Stop();
        NativeMethods.DetachTorrent(_handle, manager.TorrentSessionHandle, (int)removeFlags);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var session in ActiveTorrents)
        {
            try
            {
                DetachTorrent(session);
            }
            catch
            {
                // ignore
            }
        }

        foreach (var magnetHandle in _magnetHandles.Keys)
        {
            if (!_magnetHandles.TryRemove(magnetHandle, out _))
            {
                continue;
            }

            try
            {
                NativeMethods.DetachTorrent(_handle, magnetHandle, (int)RemoveFlags.None);
            }
            catch
            {
                // best-effort — session shutdown will reclaim the rest
            }
        }

        _disposed = true;

        NativeMethods.ClearEventCallback(_handle);
        // Complete the alert pipe so any in-flight `await foreach (var alert in Alerts)`
        // observes the end-of-stream and unblocks. TryComplete is idempotent and safe
        // to call after the writer is already complete.
        _alerts.Writer.TryComplete();
        NativeMethods.FreeSession(_handle);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Performs a validation check on the current settings pack, updating any values to values required by this library to function
    /// </summary>
    // ensureAlertMask=true for session creation: always inject RequiredAlertCategories even
    // when the caller omitted alert_mask entirely (fresh SettingsPack).
    // ensureAlertMask=false for delta packs (UpdateSettings): only OR in the required set
    // when the caller explicitly included alert_mask — otherwise libtorrent's apply_settings
    // would overwrite the runtime mask with "0 | RequiredAlerts", silently stripping every
    // category (Connect, Peer, Upload, …) that was set at session creation.
    private static void ValidateSettingsPack(SettingsPack settingsPack, bool ensureAlertMask = false)
    {
        var existingMask = settingsPack.Get<int>("alert_mask");
        if (existingMask.HasValue)
            settingsPack.Set("alert_mask", existingMask.Value | (int)RequiredAlertCategories);
        else if (ensureAlertMask)
            settingsPack.Set("alert_mask", (int)RequiredAlertCategories);
    }

    /// <summary>
    /// Marshals raised unmanaged events to managed equivalents and queues them on
    /// the <see cref="Alerts"/> channel for any active iterator to drain.
    /// These events are raised and proxied by the unmanaged library, and are
    /// automatically destroyed once the callback returns.
    /// </summary>
    /// <param name="eventPtr">A <see cref="IntPtr"/> to the underlying event (see <c>event.h</c>)</param>
    private unsafe void ProxyRaisedEvent(IntPtr eventPtr)
    {
        if (eventPtr == IntPtr.Zero)
        {
            return;
        }

        try
        {
            ProxyRaisedEventCore(eventPtr);
        }
        catch
        {
            // Reverse-P/Invoke from libtorrent's dispatcher thread: any
            // managed exception that escapes here propagates into native
            // code where .NET 8 does not marshal it cleanly across the
            // boundary, reliably crashing the process. Swallow + drop
            // the alert. The dispatcher already silently drops alerts
            // whose info_hash doesn't map to an attached handle; this
            // extends that best-effort posture to dispatcher-internal
            // failures (bad marshaling layout, partially-detached
            // handles, an unexpected enum value in PtrToStructure).
            // NOTE: this guard does NOT mask the separately-tracked
            // crash that fires when AlertCategories.Connect is enabled
            // — that one is a true native SEGV in events.cpp's
            // populate_peer_alert path, before any managed code runs.
        }
    }

    private unsafe void ProxyRaisedEventCore(IntPtr eventPtr)
    {
        var rawType = *(int*)eventPtr.ToPointer();
        if ((uint)rawType < (uint)_alertTypeHistogram.Length)
            System.Threading.Interlocked.Increment(ref _alertTypeHistogram[rawType]);

        Alert? forwardAlert = null;
        switch ((AlertType)(*(int*)eventPtr.ToPointer()))
        {
            case AlertType.Generic:
            {
                var genericAlert = Marshal.PtrToStructure<NativeEvents.AlertBase>(eventPtr);
                forwardAlert = new Alert(genericAlert);
                break;
            }

            case AlertType.TorrentStatus:
            {
                var statusAlert = Marshal.PtrToStructure<NativeEvents.TorrentStatusAlert>(eventPtr);
                if (!_attachedManagers.TryGetValue(new Sha1Hash(statusAlert.info_hash), out var torrentSubject))
                {
                    return;
                }

                forwardAlert = new TorrentStatusAlert(statusAlert, torrentSubject);
                break;
            }

            case AlertType.ClientPerformance:
            {
                var performanceAlert = Marshal.PtrToStructure<NativeEvents.PerformanceWarningAlert>(eventPtr);
                forwardAlert = new PerformanceWarningAlert(performanceAlert);
                break;
            }

            case AlertType.Peer:
            {
                System.Threading.Interlocked.Increment(ref _peerAlertAttemptCount);
                try
                {
                    var peerAlert = Marshal.PtrToStructure<NativeEvents.PeerAlert>(eventPtr);
                    _attachedManagers.TryGetValue(new Sha1Hash(peerAlert.info_hash), out var peerSubject);
                    forwardAlert = new PeerAlert(peerAlert, peerSubject);
                }
                catch (Exception pex)
                {
                    _lastPeerAlertException = pex.GetType().Name + ": " + pex.Message;
                    System.Threading.Interlocked.Increment(ref _peerAlertExceptionCount);
                }
                break;
            }

            case AlertType.TorrentRemoved:
            {
                var removedAlert = Marshal.PtrToStructure<NativeEvents.TorrentRemovedAlert>(eventPtr);
                // torrent_removed_alert can fire for magnet-source
                // torrents too — DetachMagnet eagerly removes from
                // _magnetHandles before the native call, so by the
                // time this alert fires the magnet is no longer
                // tracked anywhere in the session. Forward with null
                // Subject for magnets — same pattern as slices
                // 101/102/103/108/109/110, but using TryRemove
                // (rather than TryGetValue) for the side-effect
                // because TorrentRemoved is the lifecycle terminator
                // for TorrentInfo-source torrents and the manager's
                // MarkAsDetached flag must flip exactly once.
                if (_attachedManagers.TryRemove(new Sha1Hash(removedAlert.info_hash), out var manager))
                {
                    // mark as detached to prevent further usage
                    manager.MarkAsDetached();
                }

                forwardAlert = new TorrentRemovedAlert(removedAlert, manager);
                break;
            }

            case AlertType.TorrentPaused:
            {
                var pausedAlert = Marshal.PtrToStructure<NativeEvents.TorrentPausedAlert>(eventPtr);
                // torrent_paused_alert can fire for magnet-source
                // torrents too — calling MagnetHandle.Pause() triggers
                // the state transition regardless of metadata arrival.
                // Forward with null Subject for magnet handles — same
                // pattern as slices 101/102/103/108/109.
                _attachedManagers.TryGetValue(new Sha1Hash(pausedAlert.info_hash), out var pausedSubject);
                forwardAlert = new TorrentPausedAlert(pausedAlert, pausedSubject);
                break;
            }

            case AlertType.TorrentResumed:
            {
                var resumedAlert = Marshal.PtrToStructure<NativeEvents.TorrentResumedAlert>(eventPtr);
                // torrent_resumed_alert can fire for magnet-source
                // torrents too — magnets are added in paused state by
                // default (see AddTorrentParams), so MagnetHandle.Resume()
                // fires this alert on the first call. Forward with null
                // Subject for magnet handles — same pattern as slices
                // 101/102/103/108/109.
                _attachedManagers.TryGetValue(new Sha1Hash(resumedAlert.info_hash), out var resumedSubject);
                forwardAlert = new TorrentResumedAlert(resumedAlert, resumedSubject);
                break;
            }

            case AlertType.TorrentFinished:
            {
                var finishedAlert = Marshal.PtrToStructure<NativeEvents.TorrentFinishedAlert>(eventPtr);
                // torrent_finished_alert can fire for magnet-source
                // torrents that complete download after metadata
                // arrival; magnets are tracked in _magnetHandles, not
                // _attachedManagers, so the lookup misses. Forward
                // with null Subject rather than silently drop —
                // callers correlate magnet completions by InfoHash.
                // Same forward-with-null pattern as slices 101/102/103.
                _attachedManagers.TryGetValue(new Sha1Hash(finishedAlert.info_hash), out var finishedSubject);
                forwardAlert = new TorrentFinishedAlert(finishedAlert, finishedSubject);
                break;
            }

            case AlertType.TorrentChecked:
            {
                var checkedAlert = Marshal.PtrToStructure<NativeEvents.TorrentCheckedAlert>(eventPtr);
                // torrent_checked_alert can fire for magnet-source
                // torrents too (after metadata arrival, when libtorrent
                // verifies any pre-existing payload at the save_path).
                // Forward with null Subject for magnet handles — same
                // pattern as slices 101/102/103/108.
                _attachedManagers.TryGetValue(new Sha1Hash(checkedAlert.info_hash), out var checkedSubject);
                forwardAlert = new TorrentCheckedAlert(checkedAlert, checkedSubject);
                break;
            }

            case AlertType.StorageMoved:
            {
                var movedAlert = Marshal.PtrToStructure<NativeEvents.StorageMovedAlert>(eventPtr);
                // storage_moved_alert can fire for magnet-source torrents
                // too — MagnetHandle.MoveStorage works even before
                // metadata arrival (it's just a save-path update at
                // that point, no physical files to relocate). Forward
                // with null Subject for magnet handles — same pattern
                // as slices 101/102/103/108/109/110/111/112; continues
                // the wider audit cleanup slice 112 started.
                _attachedManagers.TryGetValue(new Sha1Hash(movedAlert.info_hash), out var movedSubject);
                forwardAlert = new StorageMovedAlert(movedAlert, movedSubject);
                break;
            }

            case AlertType.StorageMovedFailed:
            {
                var movedFailedAlert = Marshal.PtrToStructure<NativeEvents.StorageMovedFailedAlert>(eventPtr);
                if (!_attachedManagers.TryGetValue(new Sha1Hash(movedFailedAlert.info_hash), out var movedFailedSubject))
                {
                    return;
                }
                forwardAlert = new StorageMovedFailedAlert(movedFailedAlert, movedFailedSubject);
                break;
            }

            case AlertType.TrackerReply:
            {
                var trackerReplyAlert = Marshal.PtrToStructure<NativeEvents.TrackerReplyAlert>(eventPtr);
                // tracker_reply_alert fires for magnet-source torrents too — forward
                // with null Subject rather than silently drop when the info-hash
                // isn't in _attachedManagers (magnets live in _magnets).
                _attachedManagers.TryGetValue(new Sha1Hash(trackerReplyAlert.info_hash), out var trackerReplySubject);
                forwardAlert = new TrackerReplyAlert(trackerReplyAlert, trackerReplySubject);
                break;
            }

            case AlertType.ScrapeReply:
            {
                var scrapeReplyAlert = Marshal.PtrToStructure<NativeEvents.ScrapeReplyAlert>(eventPtr);
                if (!_attachedManagers.TryGetValue(new Sha1Hash(scrapeReplyAlert.info_hash), out var scrapeReplySubject))
                {
                    return;
                }
                forwardAlert = new ScrapeReplyAlert(scrapeReplyAlert, scrapeReplySubject);
                break;
            }

            case AlertType.ScrapeFailed:
            {
                var scrapeFailedAlert = Marshal.PtrToStructure<NativeEvents.ScrapeFailedAlert>(eventPtr);
                if (!_attachedManagers.TryGetValue(new Sha1Hash(scrapeFailedAlert.info_hash), out var scrapeFailedSubject))
                {
                    return;
                }
                forwardAlert = new ScrapeFailedAlert(scrapeFailedAlert, scrapeFailedSubject);
                break;
            }

            case AlertType.TrackerError:
            {
                var trackerErrorAlert = Marshal.PtrToStructure<NativeEvents.TrackerErrorAlert>(eventPtr);
                // tracker_error_alert fires for magnet-source torrents too — forward
                // with null Subject rather than silently drop when the info-hash
                // isn't in _attachedManagers (magnets live in _magnets).
                _attachedManagers.TryGetValue(new Sha1Hash(trackerErrorAlert.info_hash), out var trackerErrorSubject);
                forwardAlert = new TrackerErrorAlert(trackerErrorAlert, trackerErrorSubject);
                break;
            }

            case AlertType.TrackerAnnounce:
            {
                var trackerAnnounceAlert = Marshal.PtrToStructure<NativeEvents.TrackerAnnounceAlert>(eventPtr);
                // tracker_announce_alert fires for magnet-source torrents too — forward
                // with null Subject rather than silently drop when the info-hash
                // isn't in _attachedManagers (magnets live in _magnets).
                _attachedManagers.TryGetValue(new Sha1Hash(trackerAnnounceAlert.info_hash), out var trackerAnnounceSubject);
                forwardAlert = new TrackerAnnounceAlert(trackerAnnounceAlert, trackerAnnounceSubject);
                break;
            }

            case AlertType.TrackerWarning:
            {
                var trackerWarningAlert = Marshal.PtrToStructure<NativeEvents.TrackerWarningAlert>(eventPtr);
                // tracker_warning_alert fires for magnet-source torrents too — forward
                // with null Subject rather than silently drop when the info-hash
                // isn't in _attachedManagers (magnets live in _magnets).
                _attachedManagers.TryGetValue(new Sha1Hash(trackerWarningAlert.info_hash), out var trackerWarningSubject);
                forwardAlert = new TrackerWarningAlert(trackerWarningAlert, trackerWarningSubject);
                break;
            }

            case AlertType.FileRenamed:
            {
                var fileRenamedAlert = Marshal.PtrToStructure<NativeEvents.FileRenamedAlert>(eventPtr);
                // file_renamed_alert can fire for magnet-source torrents
                // too — MagnetHandle.RenameFile is a no-op pre-metadata
                // (libtorrent has no per-file knowledge yet), but
                // post-metadata-arrival it triggers the rename and
                // fires this alert. Forward with null Subject for
                // magnet handles — same pattern as slices 101/102/103/
                // 108/109/110/111/112/113.
                _attachedManagers.TryGetValue(new Sha1Hash(fileRenamedAlert.info_hash), out var fileRenamedSubject);
                forwardAlert = new FileRenamedAlert(fileRenamedAlert, fileRenamedSubject);
                break;
            }

            case AlertType.FileRenameFailed:
            {
                var fileRenameFailedAlert = Marshal.PtrToStructure<NativeEvents.FileRenameFailedAlert>(eventPtr);
                if (!_attachedManagers.TryGetValue(new Sha1Hash(fileRenameFailedAlert.info_hash), out var fileRenameFailedSubject))
                {
                    return;
                }
                forwardAlert = new FileRenameFailedAlert(fileRenameFailedAlert, fileRenameFailedSubject);
                break;
            }

            case AlertType.FastresumeRejected:
            {
                var fastresumeRejectedAlert = Marshal.PtrToStructure<NativeEvents.FastresumeRejectedAlert>(eventPtr);
                // fastresume_rejected_alert can fire for magnet/resume-
                // source torrents whose handles live in _magnetHandles
                // (not _attachedManagers); silently dropping makes the
                // rejection invisible to managed callers. Forward with
                // null Subject — same forward-with-null pattern as
                // slices 101 (PeerBlocked) + 102 (SaveResumeDataFailed).
                _attachedManagers.TryGetValue(new Sha1Hash(fastresumeRejectedAlert.info_hash), out var fastresumeRejectedSubject);
                forwardAlert = new FastresumeRejectedAlert(fastresumeRejectedAlert, fastresumeRejectedSubject);
                break;
            }

            case AlertType.SaveResumeDataFailed:
            {
                var saveResumeFailedAlert = Marshal.PtrToStructure<NativeEvents.SaveResumeDataFailedAlert>(eventPtr);
                // save_resume_data_failed_alert can fire for magnet
                // handles (RequestResumeData(MagnetHandle) on a
                // metadata-less magnet); magnets are tracked in
                // _magnetHandles, not _attachedManagers, so the lookup
                // misses. Forward with null Subject rather than silently
                // drop — callers correlate magnet resume failures by
                // InfoHash. Same forward-with-null pattern as the
                // PeerBlocked dispatcher fix.
                _attachedManagers.TryGetValue(new Sha1Hash(saveResumeFailedAlert.info_hash), out var saveResumeFailedSubject);
                forwardAlert = new SaveResumeDataFailedAlert(saveResumeFailedAlert, saveResumeFailedSubject);
                break;
            }

            case AlertType.TorrentDeleted:
            {
                // Fires from the disk thread after torrent_removed_alert has
                // already removed the handle from _attachedManagers; surface
                // the info_hash directly rather than resolving a subject.
                var torrentDeletedAlert = Marshal.PtrToStructure<NativeEvents.TorrentDeletedAlert>(eventPtr);
                forwardAlert = new TorrentDeletedAlert(torrentDeletedAlert);
                break;
            }

            case AlertType.TorrentDeleteFailed:
            {
                var torrentDeleteFailedAlert = Marshal.PtrToStructure<NativeEvents.TorrentDeleteFailedAlert>(eventPtr);
                forwardAlert = new TorrentDeleteFailedAlert(torrentDeleteFailedAlert);
                break;
            }

            case AlertType.MetadataReceived:
            {
                var metadataReceivedAlert = Marshal.PtrToStructure<NativeEvents.MetadataReceivedAlert>(eventPtr);
                forwardAlert = new MetadataReceivedAlert(metadataReceivedAlert);
                break;
            }

            case AlertType.MetadataFailed:
            {
                var metadataFailedAlert = Marshal.PtrToStructure<NativeEvents.MetadataFailedAlert>(eventPtr);
                forwardAlert = new MetadataFailedAlert(metadataFailedAlert);
                break;
            }

            case AlertType.TorrentError:
            {
                var torrentErrorAlert = Marshal.PtrToStructure<NativeEvents.TorrentErrorAlert>(eventPtr);
                // torrent_error_alert can fire for magnet-source torrents
                // too — disk errors during post-metadata download or
                // recheck don't care which add-source produced the
                // torrent. Forward with null Subject for magnet handles —
                // same pattern as slices 101/102/103/108/109/110/111/112/
                // 113/114. Runtime verification of the magnet path
                // deferred per (the lock-during-recheck trigger
                // pattern requires the magnet to first complete download
                // so its payload exists to lock — substantive enough for
                // its own slice).
                _attachedManagers.TryGetValue(new Sha1Hash(torrentErrorAlert.info_hash), out var torrentErrorSubject);
                forwardAlert = new TorrentErrorAlert(torrentErrorAlert, torrentErrorSubject);
                break;
            }

            case AlertType.FileError:
            {
                var fileErrorAlert = Marshal.PtrToStructure<NativeEvents.FileErrorAlert>(eventPtr);
                // file_error_alert can fire for magnet-source torrents
                // too — transient disk errors during post-metadata
                // download or recheck don't care which add-source
                // produced the torrent. Forward with null Subject for
                // magnet handles — same pattern as slices 101/102/103/
                // 108/109/110/111/112/113/114/115. Runtime verification
                // of the magnet path deferred per slice 115's reasoning
                // (the lock-during-recheck trigger pattern requires the
                // magnet to first complete download so its payload
                // exists to lock).
                _attachedManagers.TryGetValue(new Sha1Hash(fileErrorAlert.info_hash), out var fileErrorSubject);
                forwardAlert = new FileErrorAlert(fileErrorAlert, fileErrorSubject);
                break;
            }

            case AlertType.UdpError:
            {
                var udpErrorAlert = Marshal.PtrToStructure<NativeEvents.UdpErrorAlert>(eventPtr);
                forwardAlert = new UdpErrorAlert(udpErrorAlert);
                break;
            }

            case AlertType.SessionError:
            {
                var sessionErrorAlert = Marshal.PtrToStructure<NativeEvents.SessionErrorAlert>(eventPtr);
                forwardAlert = new SessionErrorAlert(sessionErrorAlert);
                break;
            }

            case AlertType.DhtError:
            {
                var dhtErrorAlert = Marshal.PtrToStructure<NativeEvents.DhtErrorAlert>(eventPtr);
                forwardAlert = new DhtErrorAlert(dhtErrorAlert);
                break;
            }

            case AlertType.LsdError:
            {
                var lsdErrorAlert = Marshal.PtrToStructure<NativeEvents.LsdErrorAlert>(eventPtr);
                forwardAlert = new LsdErrorAlert(lsdErrorAlert);
                break;
            }

            case AlertType.HashFailed:
            {
                var hashFailedAlert = Marshal.PtrToStructure<NativeEvents.HashFailedAlert>(eventPtr);
                // hash_failed_alert can fire for magnet-source torrents
                // too — corrupted piece data received after metadata
                // arrival fires the same alert regardless of which
                // add-source produced the torrent. Forward with null
                // Subject for magnet handles — same pattern as slices
                // 101/102/103/108/109/110/111/112/113/114/115/116.
                // Runtime verification of the magnet path deferred —
                // hash failures need deliberate data corruption mid-
                // transfer to trigger, more substantive than the
                // closing slices in this audit warrant.
                _attachedManagers.TryGetValue(new Sha1Hash(hashFailedAlert.info_hash), out var hashFailedSubject);
                forwardAlert = new HashFailedAlert(hashFailedAlert, hashFailedSubject);
                break;
            }

            case AlertType.ExternalIp:
            {
                var externalIpAlert = Marshal.PtrToStructure<NativeEvents.ExternalIpAlert>(eventPtr);
                forwardAlert = new ExternalIpAlert(externalIpAlert);
                break;
            }

            case AlertType.Portmap:
            {
                var portmapAlert = Marshal.PtrToStructure<NativeEvents.PortmapAlert>(eventPtr);
                forwardAlert = new PortmapAlert(portmapAlert);
                break;
            }

            case AlertType.PortmapError:
            {
                var portmapErrorAlert = Marshal.PtrToStructure<NativeEvents.PortmapErrorAlert>(eventPtr);
                forwardAlert = new PortmapErrorAlert(portmapErrorAlert);
                break;
            }

            case AlertType.DhtBootstrap:
            {
                var dhtBootstrapAlert = Marshal.PtrToStructure<NativeEvents.DhtBootstrapAlert>(eventPtr);
                forwardAlert = new DhtBootstrapAlert(dhtBootstrapAlert);
                break;
            }

            case AlertType.DhtReply:
            {
                var dhtReplyAlert = Marshal.PtrToStructure<NativeEvents.DhtReplyAlert>(eventPtr);
                if (!_attachedManagers.TryGetValue(new Sha1Hash(dhtReplyAlert.info_hash), out var dhtReplySubject))
                {
                    return;
                }
                forwardAlert = new DhtReplyAlert(dhtReplyAlert, dhtReplySubject);
                break;
            }

            case AlertType.Trackerid:
            {
                var trackeridAlert = Marshal.PtrToStructure<NativeEvents.TrackeridAlert>(eventPtr);
                if (!_attachedManagers.TryGetValue(new Sha1Hash(trackeridAlert.info_hash), out var trackeridSubject))
                {
                    return;
                }
                forwardAlert = new TrackeridAlert(trackeridAlert, trackeridSubject);
                break;
            }

            case AlertType.CacheFlushed:
            {
                var cacheFlushedAlert = Marshal.PtrToStructure<NativeEvents.CacheFlushedAlert>(eventPtr);
                if (!_attachedManagers.TryGetValue(new Sha1Hash(cacheFlushedAlert.info_hash), out var cacheFlushedSubject))
                {
                    return;
                }
                forwardAlert = new CacheFlushedAlert(cacheFlushedAlert, cacheFlushedSubject);
                break;
            }

            case AlertType.DhtAnnounce:
            {
                var dhtAnnounceAlert = Marshal.PtrToStructure<NativeEvents.DhtAnnounceAlert>(eventPtr);
                forwardAlert = new DhtAnnounceAlert(dhtAnnounceAlert);
                break;
            }

            case AlertType.DhtGetPeers:
            {
                var dhtGetPeersAlert = Marshal.PtrToStructure<NativeEvents.DhtGetPeersAlert>(eventPtr);
                forwardAlert = new DhtGetPeersAlert(dhtGetPeersAlert);
                break;
            }

            case AlertType.DhtOutgoingGetPeers:
            {
                var dhtOutgoingGetPeersAlert = Marshal.PtrToStructure<NativeEvents.DhtOutgoingGetPeersAlert>(eventPtr);
                forwardAlert = new DhtOutgoingGetPeersAlert(dhtOutgoingGetPeersAlert);
                break;
            }

            case AlertType.AddTorrent:
            {
                var addTorrentAlert = Marshal.PtrToStructure<NativeEvents.AddTorrentAlert>(eventPtr);
                _attachedManagers.TryGetValue(new Sha1Hash(addTorrentAlert.info_hash), out var addTorrentSubject);
                forwardAlert = new AddTorrentAlert(addTorrentAlert, addTorrentSubject);
                break;
            }

            case AlertType.TorrentNeedCert:
            {
                var torrentNeedCertAlert = Marshal.PtrToStructure<NativeEvents.TorrentNeedCertAlert>(eventPtr);
                // torrent_need_cert_alert can fire for magnet-source SSL
                // torrents too — once metadata arrives and reveals the
                // torrent is SSL, libtorrent fires this alert until a
                // cert is installed via set_ssl_certificate(). Forward
                // with null Subject for magnet handles — same pattern
                // as slices 101/102/103/108/109/110/111/112/113/114/
                // 115/116/117. **Closes the dispatcher silent-drop
                // audit** that slice 112 widened past slice 111's
                // initial 8-of-8 claim. Runtime verification deferred —
                // SSL torrents need an SSL-capable .torrent + cert
                // handshake, deferred-runtime by structural necessity
                // since the test harness can't easily mock SSL.
                _attachedManagers.TryGetValue(new Sha1Hash(torrentNeedCertAlert.info_hash), out var torrentNeedCertSubject);
                forwardAlert = new TorrentNeedCertAlert(torrentNeedCertAlert, torrentNeedCertSubject);
                break;
            }

            case AlertType.TorrentConflict:
            {
                var torrentConflictAlert = Marshal.PtrToStructure<NativeEvents.TorrentConflictAlert>(eventPtr);
                _attachedManagers.TryGetValue(new Sha1Hash(torrentConflictAlert.info_hash), out var torrentConflictSubject);
                _attachedManagers.TryGetValue(new Sha1Hash(torrentConflictAlert.conflicting_info_hash), out var torrentConflictOther);
                forwardAlert = new TorrentConflictAlert(torrentConflictAlert, torrentConflictSubject, torrentConflictOther);
                break;
            }

            case AlertType.FileCompleted:
            {
                var fileCompletedAlert = Marshal.PtrToStructure<NativeEvents.FileCompletedAlert>(eventPtr);
                // file_completed_alert can fire for magnet-source
                // torrents too — magnets that download data after
                // metadata arrival fire one of these per file as it
                // completes. Forward with null Subject for magnet
                // handles — same pattern as slices 101/102/103/108/
                // 109/110/111. Corrects 's overconfident
                // "8 of 8 magnet-source dispatcher cases fixed" claim:
                // a wider audit reveals at least 7 more dispatcher
                // cases (StorageMoved, FileRenamed, TorrentError,
                // FileError, HashFailed, TorrentNeedCert,
                // FileCompleted) that all silently drop magnet-source
                // alerts.
                _attachedManagers.TryGetValue(new Sha1Hash(fileCompletedAlert.info_hash), out var fileCompletedSubject);
                forwardAlert = new FileCompletedAlert(fileCompletedAlert, fileCompletedSubject);
                break;
            }

            case AlertType.PieceFinished:
            {
                var pieceFinishedAlert = Marshal.PtrToStructure<NativeEvents.PieceFinishedAlert>(eventPtr);
                if (!_attachedManagers.TryGetValue(new Sha1Hash(pieceFinishedAlert.info_hash), out var pieceFinishedSubject))
                {
                    return;
                }
                forwardAlert = new PieceFinishedAlert(pieceFinishedAlert, pieceFinishedSubject);
                break;
            }

            case AlertType.UrlSeed:
            {
                var urlSeedAlert = Marshal.PtrToStructure<NativeEvents.UrlSeedAlert>(eventPtr);
                if (!_attachedManagers.TryGetValue(new Sha1Hash(urlSeedAlert.info_hash), out var urlSeedSubject))
                {
                    return;
                }
                forwardAlert = new UrlSeedAlert(urlSeedAlert, urlSeedSubject);
                break;
            }

            case AlertType.BlockFinished:
            {
                var blockFinishedAlert = Marshal.PtrToStructure<NativeEvents.BlockFinishedAlert>(eventPtr);
                if (!_attachedManagers.TryGetValue(new Sha1Hash(blockFinishedAlert.info_hash), out var blockFinishedSubject))
                {
                    return;
                }
                forwardAlert = new BlockFinishedAlert(blockFinishedAlert, blockFinishedSubject);
                break;
            }

            case AlertType.BlockUploaded:
            {
                var blockUploadedAlert = Marshal.PtrToStructure<NativeEvents.BlockUploadedAlert>(eventPtr);
                // block_uploaded_alert fires for magnet-source torrents too — forward
                // with null Subject rather than silently drop when the info-hash
                // isn't in _attachedManagers (magnets live in _magnets).
                _attachedManagers.TryGetValue(new Sha1Hash(blockUploadedAlert.info_hash), out var blockUploadedSubject);
                forwardAlert = new BlockUploadedAlert(blockUploadedAlert, blockUploadedSubject);
                break;
            }

            case AlertType.PeerBlocked:
            {
                var peerBlockedAlert = Marshal.PtrToStructure<NativeEvents.PeerBlockedAlert>(eventPtr);
                // peer_blocked_alert fires at filter time (before the
                // BitTorrent handshake has identified which torrent
                // the peer was reaching for), so the alert's
                // info_hash is zero for filter-driven rejections.
                // TryGetValue misses; we still forward the alert
                // with a null Subject rather than silently drop it —
                // callers still need PeerAddress + Reason for IP-block
                // telemetry. See PeerBlockedAlert doc-comment for the
                // null-Subject semantics.
                _attachedManagers.TryGetValue(new Sha1Hash(peerBlockedAlert.info_hash), out var peerBlockedSubject);
                forwardAlert = new PeerBlockedAlert(peerBlockedAlert, peerBlockedSubject);
                break;
            }

            case AlertType.IncomingConnection:
            {
                var incomingConnectionAlert = Marshal.PtrToStructure<NativeEvents.IncomingConnectionAlert>(eventPtr);
                forwardAlert = new IncomingConnectionAlert(incomingConnectionAlert);
                break;
            }

            case AlertType.BlockTimeout:
            {
                var blockTimeoutAlert = Marshal.PtrToStructure<NativeEvents.BlockTimeoutAlert>(eventPtr);
                if (!_attachedManagers.TryGetValue(new Sha1Hash(blockTimeoutAlert.info_hash), out var blockTimeoutSubject))
                {
                    return;
                }
                forwardAlert = new BlockTimeoutAlert(blockTimeoutAlert, blockTimeoutSubject);
                break;
            }

            case AlertType.BlockDownloading:
            {
                var blockDownloadingAlert = Marshal.PtrToStructure<NativeEvents.BlockDownloadingAlert>(eventPtr);
                if (!_attachedManagers.TryGetValue(new Sha1Hash(blockDownloadingAlert.info_hash), out var blockDownloadingSubject))
                {
                    return;
                }
                forwardAlert = new BlockDownloadingAlert(blockDownloadingAlert, blockDownloadingSubject);
                break;
            }

            case AlertType.UnwantedBlock:
            {
                var unwantedBlockAlert = Marshal.PtrToStructure<NativeEvents.UnwantedBlockAlert>(eventPtr);
                if (!_attachedManagers.TryGetValue(new Sha1Hash(unwantedBlockAlert.info_hash), out var unwantedBlockSubject))
                {
                    return;
                }
                forwardAlert = new UnwantedBlockAlert(unwantedBlockAlert, unwantedBlockSubject);
                break;
            }

            case AlertType.Socks5:
            {
                var socks5Alert = Marshal.PtrToStructure<NativeEvents.Socks5Alert>(eventPtr);
                forwardAlert = new Socks5Alert(socks5Alert);
                break;
            }

            case AlertType.I2p:
            {
                var i2pAlert = Marshal.PtrToStructure<NativeEvents.I2pAlert>(eventPtr);
                forwardAlert = new I2pAlert(i2pAlert);
                break;
            }

            case AlertType.TorrentLog:
            {
                var torrentLogAlert = Marshal.PtrToStructure<NativeEvents.TorrentLogAlert>(eventPtr);
                if (!_attachedManagers.TryGetValue(new Sha1Hash(torrentLogAlert.info_hash), out var torrentLogSubject))
                {
                    return;
                }
                forwardAlert = new TorrentLogAlert(torrentLogAlert, torrentLogSubject);
                break;
            }

            case AlertType.Log:
            {
                var logAlert = Marshal.PtrToStructure<NativeEvents.LogAlert>(eventPtr);
                forwardAlert = new LogAlert(logAlert);
                break;
            }

            case AlertType.DhtLog:
            {
                var dhtLogAlert = Marshal.PtrToStructure<NativeEvents.DhtLogAlert>(eventPtr);
                forwardAlert = new DhtLogAlert(dhtLogAlert);
                break;
            }

            case AlertType.ResumeDataReady:
            {
                var resumeAlert = Marshal.PtrToStructure<NativeEvents.ResumeDataAlert>(eventPtr);
                forwardAlert = new ResumeDataReadyAlert(resumeAlert);
                break;
            }

            case AlertType.DhtStats:
            {
                var dhtAlert = Marshal.PtrToStructure<NativeEvents.DhtStatsAlert>(eventPtr);
                forwardAlert = new DhtStatsAlert(dhtAlert);
                break;
            }

            case AlertType.DhtPut:
            {
                var dhtPutAlert = Marshal.PtrToStructure<NativeEvents.DhtPutAlert>(eventPtr);
                forwardAlert = new DhtPutAlert(dhtPutAlert);
                break;
            }

            case AlertType.DhtImmutableItem:
            {
                var dhtItemAlert = Marshal.PtrToStructure<NativeEvents.DhtImmutableItemAlert>(eventPtr);
                forwardAlert = new DhtImmutableItemAlert(dhtItemAlert);
                break;
            }

            case AlertType.DhtMutableItem:
            {
                var dhtMutableAlert = Marshal.PtrToStructure<NativeEvents.DhtMutableItemAlert>(eventPtr);
                forwardAlert = new DhtMutableItemAlert(dhtMutableAlert);
                break;
            }

            case AlertType.SessionStats:
            {
                var sessionStatsAlert = Marshal.PtrToStructure<NativeEvents.SessionStatsAlert>(eventPtr);
                forwardAlert = new SessionStatsAlert(sessionStatsAlert);
                break;
            }

            case AlertType.ListenSucceeded:
            {
                var listenSucceededAlert = Marshal.PtrToStructure<NativeEvents.ListenSucceededAlert>(eventPtr);
                forwardAlert = new ListenSucceededAlert(listenSucceededAlert);
                break;
            }

            case AlertType.ListenFailed:
            {
                var listenFailedAlert = Marshal.PtrToStructure<NativeEvents.ListenFailedAlert>(eventPtr);
                forwardAlert = new ListenFailedAlert(listenFailedAlert);
                break;
            }
        }

        if (forwardAlert == null)
        {
            return;
        }

        // the native library always invokes this from another thread; the channel's
        // SingleReader/multi-writer config tolerates this. TryWrite returns false when
        // the channel is completed (during Dispose) — no consequence here.
        _alerts.Writer.TryWrite(forwardAlert);
    }
}