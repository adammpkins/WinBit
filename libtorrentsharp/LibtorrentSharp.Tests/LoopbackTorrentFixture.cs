using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibtorrentSharp.Alerts;
using LibtorrentSharp.Enums;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Two-session loopback fixture: a seed session on 127.0.0.1 with a
/// pre-populated payload, and a leech session ready to download it. Both
/// sessions run with DHT / LSD / UPnP / NAT-PMP disabled — only direct
/// peer connections over loopback. Closes the long-standing &quot;runtime
/// verification deferred&quot; gap on the f-alerts-full slices that needed
/// real peer traffic (TorrentFinished, PieceFinished, FileCompleted,
/// MetadataReceived, ExternalIp, etc.).
/// </summary>
/// <remarks>
/// Intentionally NOT in <c>Network/</c> — there is no real internet
/// dependency, just localhost socket binding. The fixture is opt-in
/// per-test via <c>using</c> and per-test setup; xUnit doesn't share
/// it across tests because each one wants a fresh swarm.
/// </remarks>
public sealed class LoopbackTorrentFixture : IDisposable
{
    // 4 pieces × 16 KiB = 64 KiB total. Small enough to complete in a
    // few seconds over loopback; large enough to be a multi-piece
    // download (so PieceFinishedAlert fires more than once when
    // observers care).
    private const int PieceLength = 16384;
    private const int PayloadLength = PieceLength * 4;

    private readonly string _seedSavePath;
    private readonly string _leechSavePath;
    private readonly byte[] _payload;
    private readonly byte[] _torrentBytes;
    private bool _disposed;

    public LibtorrentSession SeedSession { get; }
    public LibtorrentSession LeechSession { get; }
    public TorrentHandle SeedHandle { get; }
    public TorrentHandle LeechHandle { get; }

    // Eagerly-draining alert captures. Start consuming the session's Alerts
    // stream *before* Add() / Start() so alerts that arrive during fixture
    // setup can't slip past a test that only starts awaiting after the
    // ctor returns. Fixes an intermittent flake in the AddTorrent
    // test where the alert raced the test's await.
    public AlertCapture SeedAlerts { get; }
    public AlertCapture LeechAlerts { get; }

    public LoopbackTorrentFixture()
    {
        // Deterministic payload so test failures are reproducible.
        var rng = new Random(Seed: 42);
        _payload = new byte[PayloadLength];
        rng.NextBytes(_payload);

        var root = Path.Combine(Path.GetTempPath(), "LibtorrentSharp-Loopback", Guid.NewGuid().ToString("N"));
        _seedSavePath = Path.Combine(root, "seed");
        _leechSavePath = Path.Combine(root, "leech");
        Directory.CreateDirectory(_seedSavePath);
        Directory.CreateDirectory(_leechSavePath);

        // Pre-populate the seed's payload file before adding the torrent so
        // the initial hash check confirms the seed has all pieces.
        File.WriteAllBytes(Path.Combine(_seedSavePath, "payload.bin"), _payload);

        _torrentBytes = BuildTorrent("payload.bin", _payload);

        SeedSession = NewSession(_seedSavePath);
        LeechSession = NewSession(_leechSavePath);

        // Arm captures BEFORE Add so we catch the AddTorrentAlert that
        // fires synchronously during the native add_torrent call.
        SeedAlerts = new AlertCapture(SeedSession);
        LeechAlerts = new AlertCapture(LeechSession);

        SeedHandle = SeedSession.Add(new AddTorrentParams
        {
            TorrentInfo = new TorrentInfo(_torrentBytes),
            SavePath = _seedSavePath,
        }).Torrent!;

        LeechHandle = LeechSession.Add(new AddTorrentParams
        {
            TorrentInfo = new TorrentInfo(_torrentBytes),
            SavePath = _leechSavePath,
        }).Torrent!;

        // Adds default to paused — use Start (force-resume bypassing the
        // queue) so both torrents enter the active swarm immediately,
        // independent of libtorrent's queue limits.
        SeedHandle.Start();
        LeechHandle.Start();
    }

    /// <summary>
    /// Awaits the seed session's <see cref="ListenSucceededAlert"/> —
    /// a deterministic readiness signal for `ListenPort` and any
    /// subsequent <see cref="ConnectLeechToSeed"/> call. Replaces the
    /// magic <c>Task.Delay(1s)</c> pattern earlier tests relied on.
    /// </summary>
    public async Task WaitForSeedListeningAsync(TimeSpan? timeout = null)
    {
        var listen = await SeedAlerts.WaitForAsync<ListenSucceededAlert>(
            _ => true,
            timeout ?? TimeSpan.FromSeconds(15));
        if (listen == null)
        {
            throw new InvalidOperationException(
                $"Seed session did not emit ListenSucceededAlert within timeout. ListenPort={SeedSession.ListenPort}.");
        }
    }

    /// <summary>
    /// Wires the leech to the seed by explicit ConnectPeer (no DHT / LSD
    /// in this fixture, so peer discovery is manual). Caller should
    /// invoke <see cref="WaitForSeedListeningAsync"/> first to guarantee
    /// the seed's listen socket is bound.
    /// </summary>
    public bool ConnectLeechToSeed()
    {
        var seedPort = SeedSession.ListenPort;
        if (seedPort == 0)
        {
            throw new InvalidOperationException(
                "Seed session has no listen port — wait for TorrentCheckedAlert before connecting.");
        }
        return LeechHandle.ConnectPeer(IPAddress.Loopback, seedPort);
    }

    /// <summary>
    /// Drains alerts from <paramref name="session"/> until <paramref name="predicate"/>
    /// matches one — returns it — or <paramref name="timeout"/> elapses.
    /// </summary>
    public static async Task<T?> WaitForAlertAsync<T>(
        LibtorrentSession session,
        Func<T, bool> predicate,
        TimeSpan timeout)
        where T : Alert
    {
        using var cts = new CancellationTokenSource(timeout);
        var enumerator = session.Alerts.GetAsyncEnumerator(cts.Token);
        try
        {
            while (await enumerator.MoveNextAsync())
            {
                if (enumerator.Current is T typed && predicate(typed))
                {
                    return typed;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // fall through to null return
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
        return null;
    }

    private static LibtorrentSession NewSession(string savePath)
    {
        var pack = new SettingsPack();
        // Bind to ephemeral loopback port so concurrent runs don't fight.
        pack.Set("listen_interfaces", "127.0.0.1:0");
        // Pure-loopback fixture — disable everything that would reach out.
        pack.Set("enable_dht", false);
        pack.Set("enable_lsd", false);
        pack.Set("enable_upnp", false);
        pack.Set("enable_natpmp", false);
        // Allow same-IP peers (loopback both endpoints).
        pack.Set("allow_multiple_connections_per_ip", true);
        // Opt into FileProgress + PieceProgress so per-file completion
        // (FileCompletedAlert) and per-piece completion (PieceFinishedAlert)
        // fire on the leech when the download finishes; opt into Connect +
        // Peer because the alerts our PeerAlert wraps are split across
        // libtorrent categories — peer_connect_alert / peer_disconnected_alert
        // sit under `connect`, peer_ban / peer_snubbed / peer_unsnubbed /
        // peer_error sit under `peer`; opt into Upload so block_uploaded_alert
        // (BlockUploadedAlert) fires on the seed when a block is sent to the
        // leech; opt into BlockProgress so block_downloading_alert / block_timeout_alert
        // / unwanted_block_alert fire on the leech when blocks arrive (slice 70/69/71
        // wrappers); opt into TorrentLog so torrent_log_alert lines fire during
        // peer connect / piece exchange (slice 74 wrapper); opt into SessionLog
        // so log_alert (session-level) fires during session startup / listen
        // socket setup (slice 75 wrapper). Gets OR'd with RequiredAlertCategories
        // in the session ctor. Safe for the tiny loopback torrent (4 pieces,
        // 1 peer connection per test) — wouldn't be safe for bigger swarms,
        // which is why these aren't in the binding's required mask.
        pack.Set(
            "alert_mask",
            (int)(Enums.AlertCategories.FileProgress | Enums.AlertCategories.PieceProgress | Enums.AlertCategories.Connect | Enums.AlertCategories.Peer | Enums.AlertCategories.Upload | Enums.AlertCategories.BlockProgress | Enums.AlertCategories.TorrentLog | Enums.AlertCategories.SessionLog));

        return new LibtorrentSession(pack)
        {
            DefaultDownloadPath = savePath,
        };
    }

    // Hand-built single-file torrent with REAL SHA-1 piece hashes computed
    // from the payload. Earlier fixtures used zero-filled piece hashes
    // because the alerts they tested didn't depend on hash validity.
    private static byte[] BuildTorrent(string name, byte[] payload)
    {
        var numPieces = (payload.Length + PieceLength - 1) / PieceLength;
        var pieces = new byte[numPieces * 20];

        using var sha1 = SHA1.Create();
        for (int i = 0; i < numPieces; i++)
        {
            var offset = i * PieceLength;
            var length = Math.Min(PieceLength, payload.Length - offset);
            var hash = sha1.ComputeHash(payload, offset, length);
            Buffer.BlockCopy(hash, 0, pieces, i * 20, 20);
        }

        using var ms = new MemoryStream();
        WriteByte(ms, 'd');
        WriteBencString(ms, "info");
        WriteByte(ms, 'd');
        WriteBencString(ms, "length"); WriteBencInt(ms, payload.Length);
        WriteBencString(ms, "name");   WriteBencString(ms, name);
        WriteBencString(ms, "piece length"); WriteBencInt(ms, PieceLength);
        WriteBencString(ms, "pieces"); WriteBencBytes(ms, pieces);
        WriteByte(ms, 'e');
        WriteByte(ms, 'e');
        return ms.ToArray();
    }

    private static void WriteByte(Stream s, char c) => s.WriteByte((byte)c);

    private static void WriteBencString(Stream s, string value)
        => WriteBencBytes(s, Encoding.UTF8.GetBytes(value));

    private static void WriteBencBytes(Stream s, byte[] bytes)
    {
        var header = Encoding.ASCII.GetBytes($"{bytes.Length}:");
        s.Write(header, 0, header.Length);
        s.Write(bytes, 0, bytes.Length);
    }

    private static void WriteBencInt(Stream s, long value)
    {
        var bytes = Encoding.ASCII.GetBytes($"i{value}e");
        s.Write(bytes, 0, bytes.Length);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { SeedAlerts?.Dispose(); } catch { /* best-effort */ }
        try { LeechAlerts?.Dispose(); } catch { /* best-effort */ }

        try { SeedSession?.Dispose(); } catch { /* best-effort */ }
        try { LeechSession?.Dispose(); } catch { /* best-effort */ }

        // Clean up payload dirs — both should be safe to remove since
        // sessions are disposed by now.
        TryDelete(_seedSavePath);
        TryDelete(_leechSavePath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best-effort — Windows may hold file locks briefly after
            // session disposal; not worth blocking the test on cleanup.
        }
    }
}
