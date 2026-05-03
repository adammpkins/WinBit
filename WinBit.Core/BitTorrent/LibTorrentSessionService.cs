using System.Collections.Concurrent;
using System.Net;
using LibtorrentSharp;
using LibtorrentSharp.Alerts;
using LibtorrentSharp.Enums;
using Microsoft.Extensions.Options;
using WinBit.Core.Common;
using WinBit.Core.Hosting;
using WinBit.Core.Logging;
using WinBit.Core.Persistence;
using WinBit.Core.Settings;
using WinBit.Core.Sharing;
using WinBit.Core.Threading;

// LibtorrentSharp.TorrentHandle (the renamed-from-TorrentManager binding type) collides
// with WinBit.Core.BitTorrent.TorrentHandle (the read-only DTO returned to viewmodels).
// Because this file is in WinBit.Core.BitTorrent, the same-namespace WinBit type wins
// over both the LibtorrentSharp using and any using-alias. Each binding-side site
// fully qualifies as LibtorrentSharp.TorrentHandle. If the WinBit DTO is ever removed
// the qualifications can collapse back to the unqualified name.

namespace WinBit.Core.BitTorrent;

/// <summary>
/// LibtorrentSharp-backed <see cref="ITorrentSessionService"/>. The sole engine adapter
/// implementation since the 2026-04-27 engine swap (see <c>docs/torrent-engine.md</c>).
/// This is the *only* WinBit-aware file in the libtorrent adapter chain — the binding
/// under <c>libtorrentsharp/</c> stays project-agnostic.
/// </summary>
/// <remarks>
/// <para>Lifecycle (<c>a-lifecycle</c>) and the alert pump (<c>a-alertpump</c>) are wired:
/// <see cref="StartAsync"/> instantiates a <see cref="LibtorrentSession"/>, subscribes to
/// <c>AlertRaised</c>, and maintains a <see cref="TorrentId"/> → <see cref="TorrentHandle"/>
/// map populated from torrent-status / torrent-removed alerts. Alerts arrive on a native
/// thread and only set a "pending publish" flag — actual fan-out to <see cref="TorrentUpdated"/>
/// is batched on the next <see cref="CaptureAndPublishSnapshots"/> tick and marshaled through
/// <see cref="IDispatcherQueueProvider"/>. Add/remove flows and snapshot translation arrive in
/// subsequent commits.</para>
/// </remarks>
public sealed class LibTorrentSessionService : ITorrentSessionService, IAsyncDisposable
{
    private const string EngineNotRunningMessage =
        "LibtorrentSharp engine is not running. Call StartAsync before issuing torrent operations.";

    private const string NotImplementedMessage =
        "LibtorrentSharp engine action is not wired yet. The adapter implementation is incremental — " +
        "see docs/libtorrent-binding.md and LIBTORRENT_TASKS.md for the rollout plan.";

    // Bumped when the libtorrent resume blob format changes incompatibly. ITorrentStateStore
    // returns null on mismatch so a stale blob from an older binary triggers a fresh re-check.
    private const int ResumeBlobVersion = 1;

    // Cap PersistFastResumeAsync's wait. libtorrent normally produces save_resume_data alerts
    // within milliseconds, but a stuck native call shouldn't deadlock the autosave loop.
    private static readonly TimeSpan ResumeRequestTimeout = TimeSpan.FromSeconds(5);
    // One log entry per tracker URL per window — prevents burst duplicates when multiple torrents
    // share the same dead tracker or libtorrent probes multiple endpoints simultaneously.
    private static readonly TimeSpan TrackerErrorLogCooldown = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _trackerErrorCooldowns = new();

    // Parses the human-readable error reason from libtorrent's tracker_error_alert::message().
    // Format: "<name> (<url>)[<endpoint>] v<N> <error_reason> "<failure_reason>" (<times>)"
    // Group 1 captures <error_reason> — the part between the endpoint bracket, version, and the
    // opening quote of failure_reason. Used when ErrorMessage (failure_reason) is empty.
    private static readonly System.Text.RegularExpressions.Regex _trackerMsgErrPattern =
        new(@"\] v\d+ (.+?) """, System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly ILogService _log;
    private readonly Paths _paths;
    private readonly WinBitCoreOptions _options;
    private readonly IDispatcherQueueProvider _dispatcher;
    private readonly ITorrentStateStore _stateStore;
    private readonly ICustomNameStore _customNames;

    // File-added torrents (LibtorrentSession.Add with TorrentInfo source). Populated by AddAsync and refreshed
    // opportunistically by TorrentStatusAlert; entries drop on TorrentRemovedAlert or RemoveAsync.
    private readonly ConcurrentDictionary<TorrentId, LibtorrentSharp.TorrentHandle> _handles = new();

    // Removal tombstones. RemoveAsync inserts here BEFORE TryRemove on _handles/_magnets,
    // and the alert pump checks this set before refreshing _handles in TorrentStatusAlert.
    // Without it, libtorrent's post-detach status alerts (which keep firing for a few ticks
    // until peer connections wind down) re-insert the just-removed handle and the row
    // resurrects in the UI. Cleared when TorrentRemovedAlert acks the libtorrent-side removal,
    // or when AddAsync re-adds the same hash (rare but legal).
    private readonly ConcurrentDictionary<TorrentId, byte> _recentlyRemoved = new();

    // Magnet-added torrents (LibtorrentSession.Add with MagnetUri source). The current LibtorrentSharp surface
    // does not promote these to TorrentManagers when metadata resolves, so they live in a
    // parallel map. Both are walked by Torrents and RemoveAsync.
    private readonly ConcurrentDictionary<TorrentId, MagnetHandle> _magnets = new();

    // Keeps TorrentInfo alive for resume-loaded torrents (which libtorrent loads
    // as MagnetHandle via the resume blob). Used to serve GetTorrentFilesAsync
    // and TotalSize for those handles without binding changes.
    private readonly ConcurrentDictionary<TorrentId, LibtorrentSharp.TorrentInfo> _torrentInfos = new();

    // 0 = nothing to publish; 1 = at least one alert arrived since the last tick. Interlocked
    // exchange means the polling loop never publishes a stale or duplicated batch.
    private int _pendingPublish;

    // Last built snapshot batch, refreshed by CaptureAndPublishSnapshots. GetSnapshots returns
    // this directly so pull-side consumers (Web UI /torrents/info, status bar) see the same
    // data the push-side subscribers just got, without re-reading libtorrent.
    private IReadOnlyList<TorrentSnapshot> _lastSnapshots = Array.Empty<TorrentSnapshot>();

    // RequestResumeData is fire-and-forget; the blob arrives later via ResumeDataReadyAlert.
    // We register a TCS per request and the alert handler completes it, keyed by info-hash.
    private readonly ConcurrentDictionary<TorrentId, TaskCompletionSource<byte[]>> _pendingResumeRequests = new();

    // Resolved once in StartAsync. -1 means lts.dll absent or metric name mismatch,
    // in which case _dhtNodeCount stays 0 — safe fallback for non-DHT builds.
    private int _dhtNodesMetricIdx = -1;

    // Written by the SessionStatsAlert handler; read by GetSessionStats. Volatile so the
    // polling thread always sees the latest value the alert pump wrote.
    private volatile int _dhtNodeCount;

    // Per-add metadata that LibtorrentSharp doesn't surface back from MagnetHandle: the original
    // magnet URI (for clipboard copy) and the display name parsed at add time. Populated in
    // AddAsync, cleared on RemoveAsync / Stop.
    private readonly ConcurrentDictionary<TorrentId, MagnetMetadata> _magnetMetadata = new();

    // Immutable-after-add date fields that libtorrent does not expose on its status struct.
    // Written by AddAsync and RehydrateSavedTorrentsAsync (potentially concurrent callers);
    // read from the polling-loop thread in BuildSnapshotBatch — must be a ConcurrentDictionary.
    private readonly ConcurrentDictionary<TorrentId, (DateTime AddedUtc, DateTime? CompletedUtc)> _dateLookup = new();

    // Tracker host cache: accumulated from tracker alert URLs so GetTrackerHosts() never
    // needs to call GetTrackers() (which blocks on libtorrent's io_context via sync_call_ret).
    // Populated by the alert pump; read from the UI thread without any P/Invoke.
    private readonly ConcurrentDictionary<TorrentId, string[]> _trackerHostsCache = new();

    private readonly record struct MagnetMetadata(string? OriginalUri, string DisplayName);

    // Mirrors the last pex argument passed to SetPeerDiscoveryAsync. Checked in AddAsync so
    // newly added torrents honour the current PEX setting without waiting for the next applier
    // cycle. Default true matches libtorrent's out-of-the-box behaviour.
    private bool _pexEnabled = true;

    // Tracks which torrents have first/last piece priority active. Maintained in-process
    // because libtorrent does not expose a flag for this; the piece priorities themselves
    // are the source of truth on the native side, but querying them per-tick is too expensive.
    private readonly ConcurrentDictionary<TorrentId, bool> _firstLastPiecePriorityEnabled = new();

    private LibtorrentSession? _client;
    private CancellationTokenSource? _alertPumpCts;
    private Task? _alertPumpTask;

    public LibTorrentSessionService(
        ILogService log,
        Paths paths,
        IOptions<WinBitCoreOptions> options,
        IDispatcherQueueProvider dispatcher,
        ITorrentStateStore stateStore,
        ICustomNameStore customNames)
    {
        _log = log;
        _paths = paths;
        _options = options.Value;
        _dispatcher = dispatcher;
        _stateStore = stateStore;
        _customNames = customNames;
    }

    public bool IsRunning => _client is not null;

    public IReadOnlyList<TorrentId> Torrents
    {
        get
        {
            if (_handles.IsEmpty && _magnets.IsEmpty)
            {
                return Array.Empty<TorrentId>();
            }

            var ids = new HashSet<TorrentId>(_handles.Keys);
            foreach (var id in _magnets.Keys)
            {
                ids.Add(id);
            }
            return ids.ToArray();
        }
    }

    public event EventHandler<IReadOnlyList<TorrentSnapshot>>? TorrentUpdated;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_client is not null)
        {
            return;
        }

        var config = new LibtorrentSessionConfig
        {
            UserAgent = "WinBit/0.1",
            // Error + PerformanceWarning + Tracker + IPBlock expose the causes of mass-
            // disconnects and disk-saturation stalls. Without them the adapter is blind
            // to anything outside Status / Storage / PortMapping.
            AlertCategories = AlertCategories.Status
                | AlertCategories.Storage
                | AlertCategories.PortMapping
                | AlertCategories.Error
                | AlertCategories.PerformanceWarning
                | AlertCategories.Tracker
                | AlertCategories.IPBlock
                | AlertCategories.Peer
                | AlertCategories.Connect   // peer_connect_alert + peer_disconnected_alert require connect, not peer
                | AlertCategories.Upload
                | AlertCategories.Stats,    // session_stats_alert for DHT node count
            MaxConnections = 200,
        };

        var pack = config.Build();

        // Disk + network tuning. libtorrent defaults (~16 MiB disk write queue, 4 aio
        // threads) become a bottleneck past ~30 MB/s and can trigger mass peer-disconnect
        // when the queue backs up. Raise the buffers and request queue so we can sustain
        // high-rate swarms. Values chosen from libtorrent's "high-performance" guidance
        // in docs/torrent-engine.md; safe for typical desktop hardware.
        pack.Set("max_queued_disk_bytes", 64 * 1024 * 1024);     // 64 MiB (default 16 MiB)
        pack.Set("aio_threads", 8);                              // default 4
        pack.Set("max_out_request_queue", 1500);                 // default 500
        pack.Set("send_buffer_watermark", 3 * 1024 * 1024);      // 3 MiB (default 500 KiB)
        pack.Set("send_buffer_watermark_factor", 150);
        pack.Set("send_buffer_low_watermark", 512 * 1024);
        // libtorrent 2.x dropped the manual disk cache in favor of OS page-cache, so
        // cache_size is no longer accepted. Nothing to tune here.

        // Pin to fixed-slots choker so seeding works correctly when upload_rate_limit=0
        // (unlimited). rate_based_choker and BitTyrant both behave poorly with no cap:
        // BitTyrant refuses to unchoke at all; rate_based_choker starts with 0 slots
        // until upload rate feedback arrives. fixed_slots_choker (0) unchoking a fixed
        // number of peers immediately — qBittorrent uses this same default.
        pack.Set("choking_algorithm", 0);
        // seed_choking_algorithm is separate from choking_algorithm and controls unchoke
        // during seeding; round_robin (0) distributes upload bandwidth uniformly.
        // unchoke_slots_limit: -1 is documented as "unlimited" but on some libtorrent 2.x
        // builds it is silently clamped to 0 (unchoke nobody). Use an explicit cap instead.
        pack.Set("seed_choking_algorithm", 0);
        pack.Set("unchoke_slots_limit", 50);

        // Prefer MSE (encrypted) but accept plaintext (libtorrent enc_policy=1).
        pack.Set("out_enc_policy", 1);
        pack.Set("in_enc_policy", 1);

        // ListenPort = 0 is documented as "do not set an explicit endpoint"; libtorrent's
        // default would bind 6881, which races between parallel test sessions on a single
        // box. Bind ephemeral instead so the OS picks a free port.
        var listenPort = _options.ListenPort > 0 ? _options.ListenPort : 0;
        pack.Set("listen_interfaces", $"0.0.0.0:{listenPort},[::]:{listenPort}");

        // DHT keeps a UDP node alive that touches public bootstrap routers and survives
        // briefly past session shutdown — fine for production but flaky when tests churn
        // dozens of sessions in quick succession. Tie DHT to the listen port: an explicit
        // port = production / dev run; ListenPort = 0 = ephemeral/headless mode (tests).
        var enableDht = _options.ListenPort > 0;
        pack.Set("enable_dht", enableDht);
        if (enableDht)
        {
            pack.Set("dht_bootstrap_nodes", "router.bittorrent.com:6881,router.utorrent.com:6881,dht.transmissionbt.com:6881");
        }

        pack.Set("enable_lsd", _options.AllowLocalPeerDiscovery);
        pack.Set("enable_upnp", _options.AllowPortForwarding);
        pack.Set("enable_natpmp", _options.AllowPortForwarding);

        _log.Write(
            $"Libtorrent settings-pack applied:" +
            $" choking_algorithm={pack.Get<int>("choking_algorithm")?.ToString() ?? "unset"}" +
            $" seed_choking_algorithm={pack.Get<int>("seed_choking_algorithm")?.ToString() ?? "unset"}" +
            $" unchoke_slots_limit={pack.Get<int>("unchoke_slots_limit")?.ToString() ?? "unset"}" +
            $" out_enc_policy={pack.Get<int>("out_enc_policy")?.ToString() ?? "unset"}" +
            $" in_enc_policy={pack.Get<int>("in_enc_policy")?.ToString() ?? "unset"}" +
            $" alert_mask={pack.Get<int>("alert_mask")?.ToString() ?? "unset"}",
            LogSeverity.Normal);

        var downloads = Path.Combine(_paths.Root, "downloads");
        Directory.CreateDirectory(downloads);

        _client = new LibtorrentSession(pack)
        {
            DefaultDownloadPath = downloads,
        };

        // Resolve once so the alert handler can index directly into Counters[].
        // Returns -1 if the metric name is absent (old lts.dll or DHT disabled),
        // so the guard in SessionStatsAlert keeps _dhtNodeCount at 0.
        _dhtNodesMetricIdx = SessionStatsMetrics.FindIndex("dht.dht_nodes");

        // Pump alerts off the binding's IAsyncEnumerable into OnAlertReceived. The
        // pump task runs until either the channel completes (Dispose on the session)
        // or the CTS is canceled by Stop/Dispose. Background priority — exceptions in
        // the loop are logged and swallowed so a single bad alert can't tear down the
        // whole adapter.
        _alertPumpCts = new CancellationTokenSource();
        _alertPumpTask = Task.Run(() => PumpAlertsAsync(_client, _alertPumpCts.Token));

        _log.Write(
            $"Libtorrent engine started (port: {_options.ListenPort}, UPnP: {_options.AllowPortForwarding}, LSD: {_options.AllowLocalPeerDiscovery}, downloads: {downloads})",
            LogSeverity.Info);

        await RehydrateSavedTorrentsAsync(_client, ct).ConfigureAwait(false);
    }

    private async Task RehydrateSavedTorrentsAsync(LibtorrentSession client, CancellationToken ct)
    {
        IReadOnlyList<TorrentStateRecord> records;
        try
        {
            records = await _stateStore.GetAllAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Write($"Cold-start: failed to read saved torrent list: {ex.Message}", LogSeverity.Warning);
            return;
        }

        if (records.Count == 0)
        {
            return;
        }

        _log.Write($"Cold-start: rehydrating {records.Count} saved torrent(s).", LogSeverity.Info);

        // Bulk-load dates before the rehydration loop so each torrent's entry is available
        // to BuildSnapshotBatch even if individual resume attempts fail partway through.
        foreach (var record in records)
        {
            _dateLookup[record.Id] = (record.AddedUtc, record.CompletedUtc);
        }

        foreach (var record in records)
        {
            try
            {
                var resumeBlob = await _stateStore
                    .LoadFastResumeAsync(record.Id, ResumeBlobVersion, ct)
                    .ConfigureAwait(false);

                if (resumeBlob is not { Length: > 0 })
                {
                    _log.Write(
                        $"Cold-start: no valid resume data for {record.Name} ({record.Id}); skipping re-add.",
                        LogSeverity.Warning);
                    continue;
                }

                var savePath = string.IsNullOrEmpty(record.SavePath)
                    ? client.DefaultDownloadPath
                    : record.SavePath;
                Directory.CreateDirectory(savePath);

                var handle = client.Add(new LibtorrentSharp.AddTorrentParams
                {
                    ResumeData = resumeBlob,
                    SavePath = savePath,
                }).Magnet!;

                if (!handle.IsValid)
                {
                    _log.Write(
                        $"Cold-start: libtorrent rejected resume blob for {record.Name} ({record.Id}).",
                        LogSeverity.Warning);
                    continue;
                }

                _magnets[record.Id] = handle;
                _magnetMetadata[record.Id] = new MagnetMetadata(OriginalUri: null, record.Name);
                Interlocked.Exchange(ref _pendingPublish, 1);
                handle.Resume();
                PublishImmediateSnapshot(record.Id, TorrentState.Checking);

                _log.Write(
                    $"Cold-start: restored {record.Name} ({record.Id}) → {savePath}",
                    LogSeverity.Info);
            }
            catch (Exception ex)
            {
                _log.Write(
                    $"Cold-start: failed to rehydrate {record.Name} ({record.Id}): {ex.Message}",
                    LogSeverity.Warning);
            }
        }
    }

    private async Task PumpAlertsAsync(LibtorrentSession client, CancellationToken ct)
    {
        try
        {
            await foreach (var alert in client.Alerts.WithCancellation(ct).ConfigureAwait(false))
            {
                try
                {
                    OnAlertReceived(alert);
                }
                catch (Exception ex)
                {
                    _log.Write($"Alert handler threw: {ex.Message}", LogSeverity.Warning);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on Stop/Dispose
        }
        catch (Exception ex)
        {
            _log.Write($"Alert pump exited unexpectedly: {ex.Message}", LogSeverity.Warning);
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        var client = _client;
        if (client is null)
        {
            return;
        }

        _client = null;
        ShutdownAlertPump();
        _handles.Clear();
        _magnets.Clear();
        _magnetMetadata.Clear();
        Interlocked.Exchange(ref _pendingPublish, 0);

        // lts_destroy_session is a blocking native call. Run it on a thread-pool
        // thread so it can't prevent the host from completing its shutdown sequence.
        // Eight seconds is generous; libtorrent normally cleans up in under a second.
        var disposeTask = Task.Run(() => client.Dispose());
        if (await Task.WhenAny(disposeTask, Task.Delay(8_000, CancellationToken.None)).ConfigureAwait(false) != disposeTask)
        {
            _log.Write("Libtorrent session dispose timed out after 8 s", LogSeverity.Warning);
        }

        _log.Write("Libtorrent engine stopped", LogSeverity.Info);
    }

    private void ShutdownAlertPump()
    {
        var cts = _alertPumpCts;
        var task = _alertPumpTask;
        _alertPumpCts = null;
        _alertPumpTask = null;

        if (cts is null)
        {
            return;
        }

        try { cts.Cancel(); } catch { /* already disposed */ }

        // Best-effort wait so the pump finishes draining before the session is freed.
        // Bounded so a stuck pump doesn't block shutdown indefinitely.
        try
        {
            task?.Wait(TimeSpan.FromSeconds(2));
        }
        catch { /* OperationCanceledException / aggregate — both expected */ }

        try { cts.Dispose(); } catch { /* already disposed */ }
    }

    private void OnAlertReceived(Alert alert)
    {
        switch (alert)
        {
            case TorrentStatusAlert statusAlert when statusAlert.Subject is { } manager:
                if (TryMakeTorrentId(manager, out var statusId))
                {
                    // libtorrent keeps emitting status alerts for several ticks after
                    // DetachTorrent (until seed connections wind down). The tombstone
                    // check stops those late alerts from resurrecting a just-removed
                    // entry in _handles. ContainsKey alone is racy: another thread
                    // could TryRemove between the check and the assignment.
                    if (!_recentlyRemoved.ContainsKey(statusId))
                    {
                        _handles[statusId] = manager;
                    }
                }
                break;

            case TorrentRemovedAlert removedAlert when removedAlert.Subject is { } removedManager:
                if (TryMakeTorrentId(removedManager, out var removedId))
                {
                    _handles.TryRemove(removedId, out _);
                    _recentlyRemoved.TryRemove(removedId, out _);
                }
                break;

            case ResumeDataReadyAlert resumeAlert:
                var resumeId = TorrentId.FromInfoHash(resumeAlert.InfoHash.ToString());
                if (_pendingResumeRequests.TryRemove(resumeId, out var pending))
                {
                    pending.TrySetResult(resumeAlert.ResumeData ?? Array.Empty<byte>());
                }
                break;

            case TrackerReplyAlert trackerReply:
                CacheTrackerHost(TorrentId.FromInfoHash(trackerReply.InfoHash.ToString()), trackerReply.TrackerUrl);
                break;

            case TrackerErrorAlert trackerError:
            {
                // ErrorMessage is the tracker's HTTP failure_reason; empty for network errors.
                // Extract just the error reason from alert::message() for network errors.
                var errReason = string.IsNullOrEmpty(trackerError.ErrorMessage)
                    ? ExtractTrackerErrorReason(trackerError.Message)
                    : trackerError.ErrorMessage;
                // "skipping tracker announce" means libtorrent knows the tracker is unreachable
                // and is suppressing its announce slot — no actionable info, suppress like announce/reply.
                if (errReason.Contains("skipping tracker announce", StringComparison.OrdinalIgnoreCase))
                    break;
                // Rate-limit per URL: multiple torrents sharing a dead tracker, plus multi-endpoint
                // UDP/HTTP probes, fire several alerts per URL per announce cycle. Log only the first
                // occurrence within the cooldown window.
                var now = DateTimeOffset.UtcNow;
                if (_trackerErrorCooldowns.TryGetValue(trackerError.TrackerUrl, out var lastLogged) &&
                    now - lastLogged < TrackerErrorLogCooldown)
                    break;
                _trackerErrorCooldowns[trackerError.TrackerUrl] = now;
                _log.Write(
                    $"Tracker error {SeedingDiagHash(trackerError.InfoHash)}: url={trackerError.TrackerUrl} err={errReason}",
                    LogSeverity.Normal);
                break;
            }

            case TrackerAnnounceAlert:
                break;

            case UdpErrorAlert udpError:
                // Loopback UDP errors (127.0.0.1, ::1) come from LSD/DHT talking to itself
                // and carry no actionable info — suppress them silently.
                if (!IPAddress.IsLoopback(udpError.Endpoint.Address))
                    LogNoisyAlert(alert);
                break;

            case TorrentResumedAlert resumed:
                _log.Write(
                    $"Torrent {SeedingDiagHash(resumed.InfoHash)} resumed by libtorrent (hasSubject={resumed.Subject is not null})",
                    LogSeverity.Normal);
                break;

            case TorrentPausedAlert paused:
                _log.Write(
                    $"Torrent {SeedingDiagHash(paused.InfoHash)} paused by libtorrent (hasSubject={paused.Subject is not null})",
                    LogSeverity.Normal);
                break;

            case TorrentFinishedAlert finished:
            {
                var id = TorrentId.FromInfoHash(finished.InfoHash.ToString());
                var now = DateTime.UtcNow;
                var didSet = false;
                _dateLookup.AddOrUpdate(
                    id,
                    _ => { didSet = true; return (now, now); },
                    (_, existing) =>
                    {
                        if (existing.CompletedUtc.HasValue) return existing;
                        didSet = true;
                        return (existing.AddedUtc, now);
                    });
                if (didSet)
                {
                    _ = Task.Run(() => _stateStore.UpdateCompletedUtcAsync(id, now), CancellationToken.None);
                    _log.Write($"Torrent {id.Value[..8]} completed.", LogSeverity.Info);
                }
                break;
            }

            case SessionStatsAlert statsAlert:
                if (_dhtNodesMetricIdx >= 0 && _dhtNodesMetricIdx < statsAlert.Counters.Length)
                {
                    _dhtNodeCount = (int)statsAlert.Counters[_dhtNodesMetricIdx];
                }
                break;

            case PortmapErrorAlert portmapError
                when portmapError.ErrorMessage.Contains("no router found", StringComparison.OrdinalIgnoreCase):
                // UPnP/NAT-PMP "no router found" fires on every startup per network interface
                // when UPnP is enabled but the network has no compatible router — not actionable.
                break;

            default:
                LogNoisyAlert(alert);
                break;
        }

        // Any alert is enough to schedule a publish on the next tick. Snapshot translation
        // (a-snapshots) reads the full handle map at publish time, so we don't need a
        // per-handle dirty set.
        Interlocked.Exchange(ref _pendingPublish, 1);
    }

    // libtorrent alert-category bits (mirrors LibtorrentSharp.Enums.AlertCategories).
    // Alerts in these bands usually mean something went wrong or performance is
    // suffering — worth surfacing in the log even when we don't have a typed wrapper.
    private const int AlertCategoryError = 1 << 0;
    private const int AlertCategoryPerformanceWarning = 1 << 9;
    private const int AlertCategoryPortMapping = 1 << 2;
    private const int AlertCategoryImportantMask =
        AlertCategoryError | AlertCategoryPerformanceWarning | AlertCategoryPortMapping;

    private static string ExtractTrackerErrorReason(string alertMessage)
    {
        var m = _trackerMsgErrPattern.Match(alertMessage);
        return m.Success ? m.Groups[1].Value : alertMessage;
    }

    private void LogNoisyAlert(Alert alert)
    {
        if ((alert.Category & AlertCategoryImportantMask) == 0)
        {
            return;
        }

        var severity = (alert.Category & AlertCategoryError) != 0
            ? LogSeverity.Warning
            : LogSeverity.Info;
        _log.Write($"libtorrent alert {alert.Type}: {alert.Message}", severity);
    }

    private static bool TryMakeTorrentId(LibtorrentSharp.TorrentHandle manager, out TorrentId id)
    {
        var hex = manager.Info.Metadata.Hashes is { } hashes ? hashes.PreferredHex : null;
        if (string.IsNullOrEmpty(hex))
        {
            id = default;
            return false;
        }
        id = TorrentId.FromInfoHash(hex);
        return true;
    }

    /// <summary>
    /// Called once per second by <c>StatusPollingLoop</c>. Walks the handle map, translates
    /// each <see cref="LibtorrentSharp.TorrentStatus"/> into a <see cref="TorrentSnapshot"/>,
    /// caches the batch for <see cref="GetSnapshots"/>, and dispatches once via
    /// <see cref="IDispatcherQueueProvider"/> whenever any torrent exists — one hop per tick
    /// regardless of alert flux. Skips the work entirely on a truly idle engine.
    /// </summary>
    public void CaptureAndPublishSnapshots()
    {
        // Request a session_stats_alert from libtorrent. The alert is cheap to
        // post and arrives asynchronously on the pump; the handler updates
        // _dhtNodeCount which GetSessionStats reads on the next consumer call.
        _client?.PostSessionStats();

        var pending = Interlocked.Exchange(ref _pendingPublish, 0) != 0;
        var hasTorrents = !_handles.IsEmpty || !_magnets.IsEmpty;

        // Skip only on a truly idle engine with no torrents and no pending alert. Once
        // any torrent is registered we publish every tick: libtorrent does not
        // auto-emit state_update_alert (we don't drive post_torrent_updates), so a
        // stable swarm produces near-zero alert flux and gating publish on alerts would
        // strand the UI at the last alert's peer/seed counts. The `pending` branch keeps
        // removal-flush working when the last torrent was just removed.
        if (!hasTorrents && !pending)
        {
            return;
        }

        var previous = Volatile.Read(ref _lastSnapshots);
        var snapshots = BuildSnapshotBatch();
        Volatile.Write(ref _lastSnapshots, snapshots);

        // State-transition log is noisy; only emit it when alerts actually fired,
        // since transitions only happen as a consequence of alert-driven state changes.
        if (pending)
        {
            LogStateTransitions(previous, snapshots);
        }

        if (snapshots.Count > 0)
        {
            var subscribers = TorrentUpdated;
            if (subscribers is not null)
            {
                _dispatcher.Post(() => subscribers(this, snapshots));
            }
        }
    }

    private void PublishImmediateSnapshot(TorrentId id, TorrentState initialState)
    {
        var synthetic = new TorrentSnapshot { Id = id, State = initialState };
        var existing = Volatile.Read(ref _lastSnapshots);
        // Guard against resume-path re-adds where the torrent is already in the batch.
        if (existing.Any(s => s.Id == id))
        {
            return;
        }
        var merged = existing.Append(synthetic).ToArray();
        Volatile.Write(ref _lastSnapshots, merged);

        var subscribers = TorrentUpdated;
        if (subscribers is not null)
        {
            _dispatcher.Post(() => subscribers(this, merged));
        }
    }

    private void LogStateTransitions(IReadOnlyList<TorrentSnapshot> previous, IReadOnlyList<TorrentSnapshot> current)
    {
        if (previous.Count == 0)
        {
            return;
        }

        var prevStates = new Dictionary<TorrentId, TorrentState>(previous.Count);
        foreach (var p in previous)
        {
            prevStates[p.Id] = p.State;
        }

        foreach (var s in current)
        {
            if (prevStates.TryGetValue(s.Id, out var prev) && prev != s.State)
            {
                var shortId = s.Id.Value.Length >= 8 ? s.Id.Value[..8] : s.Id.Value;
                var err = string.IsNullOrEmpty(s.ErrorMessage) ? string.Empty : $" ({s.ErrorMessage})";
                _log.Write($"Torrent {shortId} state: {prev} → {s.State}{err}", LogSeverity.Info);
            }
        }
    }

    public Task<IReadOnlyList<WinBit.Core.BitTorrent.PeerInfo>> GetPeersAsync(TorrentId id, CancellationToken ct = default)
    {
        IReadOnlyList<LibtorrentSharp.PeerInfo> raw;

        if (_handles.TryGetValue(id, out var handle))
        {
            raw = handle.GetPeers();
        }
        else if (_magnets.TryGetValue(id, out var magnet))
        {
            raw = magnet.GetPeers();
        }
        else
        {
            return Task.FromResult<IReadOnlyList<WinBit.Core.BitTorrent.PeerInfo>>(Array.Empty<WinBit.Core.BitTorrent.PeerInfo>());
        }

        var peers = raw.Select(p => new WinBit.Core.BitTorrent.PeerInfo
        {
            Address = $"{p.Address}:{p.Port}",
            Client = p.Client is { Length: > 0 } c ? c : null,
            Progress = p.Progress,
            DownloadSpeedBps = p.DownloadRate,
            UploadSpeedBps = p.UploadRate,
            IsSeeder = p.Flags.HasFlag(LibtorrentSharp.Enums.PeerFlags.Seed),
            IsEncrypted = p.Flags.HasFlag(LibtorrentSharp.Enums.PeerFlags.Rc4Encrypted),
            IsHandshakeEncrypted = p.Flags.HasFlag(LibtorrentSharp.Enums.PeerFlags.PlaintextEncrypted),
            IsInteresting = p.Flags.HasFlag(LibtorrentSharp.Enums.PeerFlags.Interesting),
            IsChoked = p.Flags.HasFlag(LibtorrentSharp.Enums.PeerFlags.Choked),
            IsRemoteInteresting = p.Flags.HasFlag(LibtorrentSharp.Enums.PeerFlags.RemoteInterested),
            IsRemoteChoked = p.Flags.HasFlag(LibtorrentSharp.Enums.PeerFlags.RemoteChoked),
            IsOptimisticUnchoke = p.Flags.HasFlag(LibtorrentSharp.Enums.PeerFlags.OptimisticUnchoke),
            IsSnubbed = p.Flags.HasFlag(LibtorrentSharp.Enums.PeerFlags.Snubbed),
            IsIncomingConnection = !p.Flags.HasFlag(LibtorrentSharp.Enums.PeerFlags.OutgoingConnection),
            IsFromDht = p.Source.HasFlag(LibtorrentSharp.Enums.PeerSource.Dht),
            IsFromPex = p.Source.HasFlag(LibtorrentSharp.Enums.PeerSource.Pex),
            IsFromLsd = p.Source.HasFlag(LibtorrentSharp.Enums.PeerSource.Lsd),
            IsUtp = p.Flags.HasFlag(LibtorrentSharp.Enums.PeerFlags.UtpSocket),
            IsHolepunched = p.Flags.HasFlag(LibtorrentSharp.Enums.PeerFlags.Holepunched),
        }).ToList();

        return Task.FromResult<IReadOnlyList<WinBit.Core.BitTorrent.PeerInfo>>(peers);
    }

    public Task<IReadOnlyList<WinBit.Core.BitTorrent.TrackerInfo>> GetTrackersAsync(TorrentId id, CancellationToken ct = default)
    {
        IReadOnlyList<LibtorrentSharp.TrackerInfo> raw;

        if (_handles.TryGetValue(id, out var handle))
        {
            raw = handle.GetTrackers();
        }
        else if (_magnets.TryGetValue(id, out var magnet))
        {
            raw = magnet.GetTrackers();
        }
        else
        {
            return Task.FromResult<IReadOnlyList<WinBit.Core.BitTorrent.TrackerInfo>>(Array.Empty<WinBit.Core.BitTorrent.TrackerInfo>());
        }

        var trackers = raw.Select(lt => new WinBit.Core.BitTorrent.TrackerInfo
        {
            Url = new Uri(lt.Url),
            Status = TrackerStatusMapper.MapStatus(lt.Updating, lt.Fails, lt.LastError, lt.Verified),
            Tier = lt.Tier,
            Seeds = lt.ScrapeComplete,
            Leeches = lt.ScrapeIncomplete,
            Completed = lt.ScrapeDownloaded,
            NextAnnounceUtc = lt.NextAnnounce == DateTimeOffset.MinValue ? null : lt.NextAnnounce,
            LastError = string.IsNullOrEmpty(lt.LastError) ? null : lt.LastError,
        }).ToList();

        return Task.FromResult<IReadOnlyList<WinBit.Core.BitTorrent.TrackerInfo>>(trackers);
    }

    public Task<IReadOnlyList<TorrentFileEntry>> GetTorrentFilesAsync(TorrentId id, CancellationToken ct = default)
    {
        if (_handles.TryGetValue(id, out var handle))
        {
            var fileProgress = handle.GetFileProgress();
            var isSeed = handle.GetCurrentStatus().Progress >= 1.0f;
            var entries = handle.Files
                .Where(f => !f.Info.IsPadFile)
                .Select(f => new TorrentFileEntry
                {
                    Index = f.Info.Index,
                    Name = f.Info.Name,
                    RelativePath = f.Info.Path.Replace('\\', '/'),
                    SizeBytes = f.Info.FileSize,
                    Priority = MapFilePriority(f.Priority),
                    DownloadedBytes = isSeed ? f.Info.FileSize : (fileProgress.Length > f.Info.Index ? fileProgress[f.Info.Index] : 0L),
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<TorrentFileEntry>>(entries);
        }

        // Magnets (including resume-loaded torrents) use the native handle directly.
        // MagnetHandle.GetFiles() calls lts_torrent_handle_file_list which works for
        // both fresh magnets with resolved metadata and cold-start resume-loaded handles.
        // Pure pre-metadata magnets return an empty list from the native side.
        if (_magnets.TryGetValue(id, out var magnet))
        {
            var isSeed = magnet.GetCurrentStatus().Progress >= 1.0f;
            var allFiles = magnet.GetFiles();
            var fileProgress = isSeed ? Array.Empty<long>() : magnet.GetFileProgress(allFiles.Count);
            var entries = allFiles
                .Where(f => !f.Info.IsPadFile)
                .Select(f => new TorrentFileEntry
                {
                    Index = f.Info.Index,
                    Name = f.Info.Name,
                    RelativePath = f.Info.Path.Replace('\\', '/'),
                    SizeBytes = f.Info.FileSize,
                    Priority = MapFilePriority(f.Priority),
                    DownloadedBytes = isSeed ? f.Info.FileSize : (fileProgress.Length > f.Info.Index ? fileProgress[f.Info.Index] : 0L),
                })
                .ToList();
            return Task.FromResult<IReadOnlyList<TorrentFileEntry>>(entries);
        }

        return Task.FromResult<IReadOnlyList<TorrentFileEntry>>(Array.Empty<TorrentFileEntry>());
    }

    public Task<IReadOnlyList<bool>> GetPiecesAsync(TorrentId id, CancellationToken ct = default)
    {
        if (_handles.TryGetValue(id, out var handle))
        {
            ct.ThrowIfCancellationRequested();
            var info = handle.Info;
            if (info is null || info.NumPieces == 0)
                return Task.FromResult<IReadOnlyList<bool>>(Array.Empty<bool>());
            return Task.FromResult<IReadOnlyList<bool>>(handle.GetPieceBitfield(info.NumPieces));
        }

        if (_magnets.TryGetValue(id, out var magnet))
        {
            ct.ThrowIfCancellationRequested();
            var count = magnet.NumPieces;
            if (count == 0)
                return Task.FromResult<IReadOnlyList<bool>>(Array.Empty<bool>());
            return Task.FromResult<IReadOnlyList<bool>>(magnet.GetPieceBitfield(count));
        }

        return Task.FromResult<IReadOnlyList<bool>>(Array.Empty<bool>());
    }

    private static WinBit.Core.BitTorrent.FileDownloadPriority MapFilePriority(LibtorrentSharp.Enums.FileDownloadPriority p) =>
        p switch
        {
            LibtorrentSharp.Enums.FileDownloadPriority.DoNotDownload => WinBit.Core.BitTorrent.FileDownloadPriority.DoNotDownload,
            LibtorrentSharp.Enums.FileDownloadPriority.Low           => WinBit.Core.BitTorrent.FileDownloadPriority.Low,
            LibtorrentSharp.Enums.FileDownloadPriority.Normal        => WinBit.Core.BitTorrent.FileDownloadPriority.Normal,
            // libtorrent's High (7) is the absolute maximum on its 0-7 scale; map it to
            // Core Maximum (7). Core High (6) arrives as an unnamed byte value from
            // libtorrent — the default arm catches it and rounds down to Normal.
            LibtorrentSharp.Enums.FileDownloadPriority.High          => WinBit.Core.BitTorrent.FileDownloadPriority.Maximum,
            _                                                          => WinBit.Core.BitTorrent.FileDownloadPriority.Normal,
        };

    private static LibtorrentSharp.Enums.FileDownloadPriority MapToNativeFilePriority(WinBit.Core.BitTorrent.FileDownloadPriority priority) =>
        priority switch
        {
            WinBit.Core.BitTorrent.FileDownloadPriority.DoNotDownload => LibtorrentSharp.Enums.FileDownloadPriority.DoNotDownload,
            WinBit.Core.BitTorrent.FileDownloadPriority.Low           => LibtorrentSharp.Enums.FileDownloadPriority.Low,
            // Core High (6) has no named LibtorrentSharp equivalent; cast the value directly.
            // Core Maximum (7) corresponds to LibtorrentSharp.High on the 0-7 scale.
            WinBit.Core.BitTorrent.FileDownloadPriority.High          => (LibtorrentSharp.Enums.FileDownloadPriority)6,
            WinBit.Core.BitTorrent.FileDownloadPriority.Maximum       => LibtorrentSharp.Enums.FileDownloadPriority.High,
            _                                                           => LibtorrentSharp.Enums.FileDownloadPriority.Normal,
        };

    public IReadOnlyList<TorrentSnapshot> GetSnapshots() => Volatile.Read(ref _lastSnapshots);

    public async Task PersistFastResumeAsync(CancellationToken ct = default)
    {
        var client = _client;
        if (client is null)
        {
            return;
        }

        if (_handles.IsEmpty && _magnets.IsEmpty)
        {
            return;
        }

        var requests = new List<(TorrentId Id, TaskCompletionSource<byte[]> Tcs)>(_handles.Count + _magnets.Count);

        foreach (var (id, manager) in _handles)
        {
            requests.Add(RegisterAndRequest(id, () => client.RequestResumeData(manager)));
        }

        foreach (var (id, magnet) in _magnets)
        {
            if (!magnet.IsValid)
            {
                continue;
            }
            requests.Add(RegisterAndRequest(id, () => client.RequestResumeData(magnet)));
        }

        if (requests.Count == 0)
        {
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ResumeRequestTimeout);

        foreach (var (id, tcs) in requests)
        {
            try
            {
                var blob = await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                if (blob.Length > 0)
                {
                    await _stateStore.SaveFastResumeAsync(id, blob, ResumeBlobVersion, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _log.Write($"Resume data request for {id.Value[..8]} timed out after {ResumeRequestTimeout.TotalSeconds:0}s", LogSeverity.Warning);
            }
            catch (Exception ex)
            {
                _log.Write($"Persisting resume data for {id.Value[..8]} failed: {ex.Message}", LogSeverity.Warning);
            }
            finally
            {
                _pendingResumeRequests.TryRemove(id, out _);
            }
        }
    }

    private (TorrentId Id, TaskCompletionSource<byte[]> Tcs) RegisterAndRequest(TorrentId id, Action requestNative)
    {
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Replace any previous in-flight request for the same handle (autosave overlapping
        // shutdown). The orphaned TCS is never completed but never awaited again either.
        _pendingResumeRequests[id] = tcs;
        try
        {
            requestNative();
        }
        catch (Exception ex)
        {
            _log.Write($"RequestResumeData failed for {id.Value[..8]}: {ex.Message}", LogSeverity.Warning);
            tcs.TrySetException(ex);
        }
        return (id, tcs);
    }

    private IReadOnlyList<TorrentSnapshot> BuildSnapshotBatch()
    {
        if (_handles.IsEmpty && _magnets.IsEmpty)
        {
            return Array.Empty<TorrentSnapshot>();
        }

        var batch = new List<TorrentSnapshot>(_handles.Count + _magnets.Count);

        foreach (var (id, manager) in _handles)
        {
            try
            {
                _dateLookup.TryGetValue(id, out var dates);
                batch.Add(BuildSnapshot(id, manager.GetCurrentStatus()) with
                {
                    TotalSize = manager.Info.Metadata.TotalSize,
                    HasFirstLastPiecePriority = _firstLastPiecePriorityEnabled.ContainsKey(id),
                    AddedUtc = dates.AddedUtc,
                    CompletedUtc = dates.CompletedUtc,
                });
            }
            catch (Exception ex)
            {
                _log.Write($"Failed to read status for torrent {id}: {ex.Message}", LogSeverity.Warning);
            }
        }

        foreach (var (id, magnet) in _magnets)
        {
            if (!magnet.IsValid)
            {
                continue;
            }

            try
            {
                _dateLookup.TryGetValue(id, out var dates);
                batch.Add(BuildSnapshot(id, magnet.GetCurrentStatus()) with
                {
                    TotalSize = magnet.TotalSize,
                    AddedUtc = dates.AddedUtc,
                    CompletedUtc = dates.CompletedUtc,
                });
            }
            catch (Exception ex)
            {
                _log.Write($"Failed to read status for magnet {id}: {ex.Message}", LogSeverity.Warning);
            }
        }

        return batch;
    }

    private static TorrentSnapshot BuildSnapshot(TorrentId id, LibtorrentSharp.TorrentStatus status) =>
        new()
        {
            Id = id,
            State = status.Flags.HasFlag(LibtorrentSharp.Enums.TorrentFlags.Paused)
                ? (status.Flags.HasFlag(LibtorrentSharp.Enums.TorrentFlags.AutoManaged)
                    ? TorrentState.Queued
                    : TorrentState.Paused)
                : MapState(status.State),
            Progress = status.Progress,
            BytesDownloaded = status.BytesDownloaded,
            BytesUploaded = status.BytesUploaded,
            DownloadSpeedBps = status.DownloadRate,
            UploadSpeedBps = status.UploadRate,
            Ratio = status.Ratio ?? 0.0,
            Eta = status.Eta,
            Seeds = status.SeedCount,
            Peers = status.PeerCount,
            ErrorMessage = string.IsNullOrEmpty(status.ErrorMessage) ? null : status.ErrorMessage,
            IsSequentialDownload = status.Flags.HasFlag(LibtorrentSharp.Enums.TorrentFlags.SequentialDownload),
            IsForceStart = !status.Flags.HasFlag(LibtorrentSharp.Enums.TorrentFlags.AutoManaged) && !status.Flags.HasFlag(LibtorrentSharp.Enums.TorrentFlags.Paused),
        };

    // libtorrent has no Paused / Stopped value in its state enum — pause is a flag
    // (torrent_flags::paused) on the status struct. BuildSnapshot checks the flag before
    // calling here, so MapState only runs for torrents that are actively running.
    // paused && autoManaged → Queued (auto-manager holds it); paused && !autoManaged → Paused.
    private static TorrentState MapState(LibtorrentSharp.Enums.TorrentState state) => state switch
    {
        LibtorrentSharp.Enums.TorrentState.CheckingFiles => TorrentState.Checking,
        LibtorrentSharp.Enums.TorrentState.CheckingResume => TorrentState.Checking,
        LibtorrentSharp.Enums.TorrentState.DownloadingMetadata => TorrentState.Downloading,
        LibtorrentSharp.Enums.TorrentState.Downloading => TorrentState.Downloading,
        LibtorrentSharp.Enums.TorrentState.Seeding => TorrentState.Seeding,
        LibtorrentSharp.Enums.TorrentState.Finished => TorrentState.Completed,
        LibtorrentSharp.Enums.TorrentState.Errored => TorrentState.Error,
        _ => TorrentState.Stopped,
    };

    public async Task<Result<TorrentId>> AddAsync(AddTorrentParams parameters, CancellationToken ct = default)
    {
        var client = _client;
        if (client is null)
        {
            return Result<TorrentId>.Failure(EngineNotRunningMessage);
        }

        try
        {
            Directory.CreateDirectory(parameters.SavePath);

            TorrentId addedId;
            string displayName;

            if (parameters.Source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                if (!LibtorrentSharp.MagnetUri.TryGetInfoHash(parameters.Source, out var infoHash))
                {
                    return Result<TorrentId>.Failure(
                        "Magnet URI is missing or has an unparseable xt=urn:btih hash.");
                }

                var hash = infoHash.ToString();
                addedId = TorrentId.FromInfoHash(hash);
                displayName = LibtorrentSharp.MagnetUri.TryGetDisplayName(parameters.Source) ?? hash[..8];

                // Try to skip the re-check by attaching from a previously-persisted resume
                // blob. The resume-sourced add returns a MagnetHandle even for what was
                // originally a magnet add, so it slots into _magnets either way.
                var resumeBlob = await _stateStore
                    .LoadFastResumeAsync(addedId, ResumeBlobVersion, ct)
                    .ConfigureAwait(false);
                MagnetHandle magnet;
                bool loadedFromResume = false;
                if (resumeBlob is { Length: > 0 })
                {
                    magnet = client.Add(new LibtorrentSharp.AddTorrentParams { ResumeData = resumeBlob, SavePath = parameters.SavePath }).Magnet!;
                    if (magnet.IsValid)
                    {
                        loadedFromResume = true;
                    }
                    else
                    {
                        _log.Write($"Resume blob for {hash[..8]} was rejected; falling back to fresh magnet add.", LogSeverity.Warning);
                        magnet = client.Add(new LibtorrentSharp.AddTorrentParams { MagnetUri = parameters.Source, SavePath = parameters.SavePath }).Magnet!;
                    }
                }
                else
                {
                    magnet = client.Add(new LibtorrentSharp.AddTorrentParams { MagnetUri = parameters.Source, SavePath = parameters.SavePath }).Magnet!;
                }

                if (!magnet.IsValid)
                {
                    return Result<TorrentId>.Failure("libtorrent rejected the magnet URI.");
                }

                _magnets[addedId] = magnet;
                if (!_pexEnabled)
                    magnet.SetFlags(TorrentFlags.DisablePex);
                _magnetMetadata[addedId] = new MagnetMetadata(parameters.Source, displayName);
                Interlocked.Exchange(ref _pendingPublish, 1);

                // libtorrent's C ABI adds every torrent paused (paused=true,
                // auto_managed=false) so an unconditional Resume/Pause is required to
                // honour StartImmediately — the native default isn't "running".
                if (parameters.StartImmediately)
                {
                    magnet.Resume();
                }
                else
                {
                    magnet.Pause();
                }

                PublishImmediateSnapshot(addedId,
                    parameters.StartImmediately ? TorrentState.Checking : TorrentState.Paused);

                _log.Write(
                    loadedFromResume
                        ? $"Loaded magnet {hash[..8]} from saved resume blob → {parameters.SavePath}"
                        : $"Added magnet {hash[..8]} → {parameters.SavePath}",
                    LogSeverity.Info);
            }
            else
            {
                if (!File.Exists(parameters.Source))
                {
                    return Result<TorrentId>.Failure(
                        $"Unknown torrent source: '{parameters.Source}'. Expected a magnet URI or a local .torrent path.");
                }

                var info = new TorrentInfo(parameters.Source);
                var hashHex = info.Metadata.Hashes is { } hashes ? hashes.PreferredHex : null;
                if (string.IsNullOrEmpty(hashHex))
                {
                    return Result<TorrentId>.Failure("Torrent has neither a v1 nor v2 info-hash.");
                }

                addedId = TorrentId.FromInfoHash(hashHex);
                displayName = info.Metadata.Name;

                var resumeBlob = await _stateStore
                    .LoadFastResumeAsync(addedId, ResumeBlobVersion, ct)
                    .ConfigureAwait(false);
                bool loadedFromResume = false;
                if (resumeBlob is { Length: > 0 })
                {
                    // Resume-loaded file torrents return as MagnetHandle (binding quirk: the
                    // .torrent metadata travels inside the resume blob, so libtorrent doesn't
                    // need a separate torrent_info handle and the LibtorrentSharp wrapper
                    // doesn't promote the result to a TorrentHandle). Either way it slots
                    // into _magnets and behaves identically for the rest of the adapter.
                    var resumeHandle = client.Add(new LibtorrentSharp.AddTorrentParams { ResumeData = resumeBlob, SavePath = parameters.SavePath }).Magnet!;
                    if (resumeHandle.IsValid)
                    {
                        _magnets[addedId] = resumeHandle;
                        if (!_pexEnabled)
                            resumeHandle.SetFlags(TorrentFlags.DisablePex);
                        _magnetMetadata[addedId] = new MagnetMetadata(OriginalUri: null, displayName);
                        _torrentInfos[addedId] = info;
                        Interlocked.Exchange(ref _pendingPublish, 1);
                        if (parameters.StartImmediately)
                        {
                            resumeHandle.Resume();
                        }
                        else
                        {
                            resumeHandle.Pause();
                        }

                        PublishImmediateSnapshot(addedId,
                            parameters.StartImmediately ? TorrentState.Checking : TorrentState.Paused);

                        loadedFromResume = true;
                    }
                    else
                    {
                        _log.Write($"Resume blob for {hashHex[..8]} was rejected; falling back to fresh file-based add.", LogSeverity.Warning);
                    }
                }

                if (!loadedFromResume)
                {
                    var manager = client.Add(new LibtorrentSharp.AddTorrentParams { TorrentInfo = info, SavePath = parameters.SavePath }).Torrent!;
                    _recentlyRemoved.TryRemove(addedId, out _);
                    _handles[addedId] = manager;
                    if (!_pexEnabled)
                        manager.SetFlags(TorrentFlags.DisablePex);
                    Interlocked.Exchange(ref _pendingPublish, 1);
                    if (parameters.StartImmediately)
                    {
                        manager.Start();
                    }
                    else
                    {
                        manager.Stop();
                    }

                    PublishImmediateSnapshot(addedId,
                        parameters.StartImmediately ? TorrentState.Checking : TorrentState.Paused);
                }

                _log.Write(
                    loadedFromResume
                        ? $"Loaded torrent {hashHex[..8]} ({displayName}) from saved resume blob → {parameters.SavePath}"
                        : $"Added torrent {hashHex[..8]} ({displayName}) → {parameters.SavePath}",
                    LogSeverity.Info);
            }

            // Upsert the persistence row so a later PersistFastResumeAsync call has a target
            // for SaveFastResumeAsync (which is UPDATE-only). Without this, resume blobs
            // would silently never persist for libtorrent-added torrents.
            // Fire-and-forget: don't block AddAsync (and therefore the dialog) on the SQLite write.
            var addedUtc = DateTime.UtcNow;
            _dateLookup[addedId] = (addedUtc, CompletedUtc: null);
            var record = new TorrentStateRecord
            {
                Id = addedId,
                Name = displayName,
                SavePath = parameters.SavePath,
                AddedUtc = addedUtc,
                Category = parameters.Category,
                Tags = parameters.Tags.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(parameters.Tags),
            };
            _ = Task.Run(async () =>
            {
                try
                {
                    await _stateStore.UpsertTorrentAsync(record, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Write($"Background UpsertTorrentAsync failed for {addedId}: {ex.Message}", LogSeverity.Warning);
                }
            });

            return Result<TorrentId>.Success(addedId);
        }
        catch (Exception ex)
        {
            _log.Write($"  AddAsync exception: {ex.GetType().Name}: {ex.Message}", LogSeverity.Warning);
            return Result<TorrentId>.Failure($"Add failed: {ex.Message}");
        }
    }

    public Task<Result> RemoveAsync(TorrentId id, bool deleteContent = false, CancellationToken ct = default)
    {
        var client = _client;
        if (client is null)
        {
            return Task.FromResult(Result.Failure(EngineNotRunningMessage));
        }

        try
        {
            // Tombstone BEFORE TryRemove so any in-flight TorrentStatusAlert on the
            // alert pump can see the tombstone and skip its _handles refresh.
            _recentlyRemoved[id] = 0;

            if (_magnets.TryRemove(id, out var magnet))
            {
                client.DetachMagnet(magnet);
                _magnetMetadata.TryRemove(id, out _);
                _torrentInfos.TryRemove(id, out _);
                _trackerHostsCache.TryRemove(id, out _);
                _firstLastPiecePriorityEnabled.TryRemove(id, out _);
                _dateLookup.TryRemove(id, out _);
                Interlocked.Exchange(ref _pendingPublish, 1);
                _ = _customNames.RemoveNameAsync(id);
                _ = _stateStore.RemoveTorrentAsync(id, CancellationToken.None);
                _log.Write($"Removed magnet {id.Value[..8]}", LogSeverity.Info);
                return Task.FromResult(Result.Success());
            }

            if (_handles.TryRemove(id, out var manager))
            {
                client.DetachTorrent(manager, deleteContent ? RemoveFlags.DeleteFiles : RemoveFlags.None);
                _torrentInfos.TryRemove(id, out _);
                _trackerHostsCache.TryRemove(id, out _);
                _firstLastPiecePriorityEnabled.TryRemove(id, out _);
                _dateLookup.TryRemove(id, out _);
                Interlocked.Exchange(ref _pendingPublish, 1);
                _ = _customNames.RemoveNameAsync(id);
                _ = _stateStore.RemoveTorrentAsync(id, CancellationToken.None);
                _log.Write($"Removed torrent {id.Value[..8]} (deleteContent={deleteContent})", LogSeverity.Info);
                return Task.FromResult(Result.Success());
            }

            // Nothing to remove — clear the speculative tombstone so a future add of
            // the same hash isn't accidentally suppressed.
            _recentlyRemoved.TryRemove(id, out _);

            return Task.FromResult(Result.Failure($"No torrent with info-hash {id.Value} is currently loaded."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Failure($"Remove failed: {ex.Message}"));
        }
    }

    public Task<Result> AddTrackerAsync(TorrentId id, string url, int tier = 0, CancellationToken ct = default) =>
        Task.FromResult(InvokeOnHandle(
            id,
            manager => manager.AddTracker(url, tier),
            magnet => magnet.AddTracker(url, tier),
            "AddTracker"));

    public Task<Result> RemoveTrackerAsync(TorrentId id, string url, CancellationToken ct = default) =>
        Task.FromResult(InvokeOnHandle(
            id,
            manager => manager.RemoveTracker(url),
            magnet => magnet.RemoveTracker(url),
            "RemoveTracker"));

    public Task<Result> EditTrackerAsync(TorrentId id, string oldUrl, string newUrl, int newTier, CancellationToken ct = default) =>
        Task.FromResult(InvokeOnHandle(
            id,
            manager => manager.EditTracker(oldUrl, newUrl, newTier),
            magnet => magnet.EditTracker(oldUrl, newUrl, newTier),
            "EditTracker"));

    public Task<IReadOnlyList<WebSeedInfo>> GetWebSeedsAsync(TorrentId id, CancellationToken ct = default)
    {
        IReadOnlyList<LibtorrentSharp.WebSeedInfo> raw;

        if (_handles.TryGetValue(id, out var handle))
        {
            raw = handle.GetWebSeeds();
        }
        else if (_magnets.TryGetValue(id, out var magnet))
        {
            raw = magnet.GetWebSeeds();
        }
        else
        {
            return Task.FromResult<IReadOnlyList<WebSeedInfo>>(Array.Empty<WebSeedInfo>());
        }

        var seeds = raw
            .Select(ws => Uri.TryCreate(ws.Url, UriKind.Absolute, out var uri) ? new WebSeedInfo { Url = uri } : null)
            .OfType<WebSeedInfo>()
            .ToList();

        return Task.FromResult<IReadOnlyList<WebSeedInfo>>(seeds);
    }

    public Task<Result> AddWebSeedAsync(TorrentId id, string url, CancellationToken ct = default) =>
        Task.FromResult(InvokeOnHandle(
            id,
            manager => manager.AddWebSeed(url),
            magnet => magnet.AddWebSeed(url),
            "AddWebSeed"));

    public Task<Result> RemoveWebSeedAsync(TorrentId id, string url, CancellationToken ct = default) =>
        Task.FromResult(InvokeOnHandle(
            id,
            manager => manager.RemoveWebSeed(url),
            magnet => magnet.RemoveWebSeed(url),
            "RemoveWebSeed"));

    public Task<Result> AddPeerAsync(TorrentId id, string ipAddress, int port, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(ipAddress, out var addr))
            return Task.FromResult(Result.Failure($"Invalid IP address: \"{ipAddress}\"."));
        return Task.FromResult(InvokeOnHandle(
            id,
            manager => manager.ConnectPeer(addr, port),
            magnet => magnet.ConnectPeer(addr, port),
            "AddPeer"));
    }

    public Task<byte[]?> ExportTorrentBytesAsync(TorrentId id, CancellationToken ct = default)
    {
        if (_handles.TryGetValue(id, out var handle))
            return Task.FromResult<byte[]?>(handle.ExportTorrentBytes());
        if (_magnets.TryGetValue(id, out var magnet))
            return Task.FromResult<byte[]?>(magnet.ExportTorrentBytes());
        return Task.FromResult<byte[]?>(null);
    }

    public Task<Result> PauseAsync(TorrentId id, CancellationToken ct = default) =>
        Task.FromResult(InvokeOnHandle(
            id,
            // Bypass Pause() — the native lts_pause_torrent sets auto_managed which lets the
            // queue re-enable the torrent immediately. Force-stop by clearing auto_managed
            // then setting the paused flag directly so the auto-manager can't undo it.
            m => { m.UnsetFlags(LibtorrentSharp.Enums.TorrentFlags.AutoManaged); m.SetFlags(LibtorrentSharp.Enums.TorrentFlags.Paused); },
            m => { m.UnsetFlags(LibtorrentSharp.Enums.TorrentFlags.AutoManaged); m.SetFlags(LibtorrentSharp.Enums.TorrentFlags.Paused); },
            "Pause"));

    public Task<Result> ResumeAsync(TorrentId id, CancellationToken ct = default) =>
        Task.FromResult(InvokeOnHandle(id, m => m.Resume(), m => m.Resume(), "Resume"));

    public Task<Result> ForceRecheckAsync(TorrentId id, CancellationToken ct = default) =>
        Task.FromResult(InvokeOnHandle(id, m => m.ForceRecheck(), m => m.ForceRecheck(), "ForceRecheck"));

    public Task<Result> ForceReannounceAsync(TorrentId id, CancellationToken ct = default) =>
        Task.FromResult(InvokeOnHandle(
            id,
            m => m.ReannounceAllTrackers(TimeSpan.Zero, force: true),
            m => m.ReannounceAllTrackers(TimeSpan.Zero, force: true),
            "ForceReannounce"));

    private Result InvokeOnHandle(
        TorrentId id,
        Action<LibtorrentSharp.TorrentHandle> onManager,
        Action<MagnetHandle> onMagnet,
        string actionLabel)
    {
        if (_client is null)
        {
            return Result.Failure(EngineNotRunningMessage);
        }

        try
        {
            var shortId = id.Value.Length >= 8 ? id.Value[..8] : id.Value;

            if (_handles.TryGetValue(id, out var manager))
            {
                if (TryReadStatus(id, out var pre))
                    _log.Write(
                        $"{actionLabel} {shortId} pre: paused={pre.Flags.HasFlag(LibtorrentSharp.Enums.TorrentFlags.Paused)}" +
                        $" autoManaged={pre.Flags.HasFlag(LibtorrentSharp.Enums.TorrentFlags.AutoManaged)} state={pre.State}",
                        LogSeverity.Normal);

                onManager(manager);

                if (TryReadStatus(id, out var post))
                    _log.Write(
                        $"{actionLabel} {shortId} post: paused={post.Flags.HasFlag(LibtorrentSharp.Enums.TorrentFlags.Paused)}" +
                        $" autoManaged={post.Flags.HasFlag(LibtorrentSharp.Enums.TorrentFlags.AutoManaged)} state={post.State}",
                        LogSeverity.Normal);

                Interlocked.Exchange(ref _pendingPublish, 1);
                return Result.Success();
            }

            if (_magnets.TryGetValue(id, out var magnet))
            {
                if (TryReadStatus(id, out var pre))
                    _log.Write(
                        $"{actionLabel} {shortId} pre: paused={pre.Flags.HasFlag(LibtorrentSharp.Enums.TorrentFlags.Paused)}" +
                        $" autoManaged={pre.Flags.HasFlag(LibtorrentSharp.Enums.TorrentFlags.AutoManaged)} state={pre.State}",
                        LogSeverity.Normal);

                onMagnet(magnet);

                if (TryReadStatus(id, out var post))
                    _log.Write(
                        $"{actionLabel} {shortId} post: paused={post.Flags.HasFlag(LibtorrentSharp.Enums.TorrentFlags.Paused)}" +
                        $" autoManaged={post.Flags.HasFlag(LibtorrentSharp.Enums.TorrentFlags.AutoManaged)} state={post.State}",
                        LogSeverity.Normal);

                Interlocked.Exchange(ref _pendingPublish, 1);
                return Result.Success();
            }

            return Result.Failure($"No torrent with info-hash {id.Value} is currently loaded.");
        }
        catch (Exception ex)
        {
            return Result.Failure($"{actionLabel} failed: {ex.Message}");
        }
    }

    public string? GetMagnetUri(TorrentId id)
    {
        if (_magnetMetadata.TryGetValue(id, out var meta) && meta.OriginalUri is { Length: > 0 })
        {
            return meta.OriginalUri;
        }

        // For file-added or resume-loaded torrents we synthesize a v1 magnet from the
        // info-hash and best-known display name. Sufficient for clipboard copy.
        if (_handles.TryGetValue(id, out var manager))
        {
            var name = manager.Info.Metadata.Name ?? id.Value;
            return $"magnet:?xt=urn:btih:{id.Value}&dn={Uri.EscapeDataString(name)}";
        }

        if (_magnets.ContainsKey(id))
        {
            var name = _magnetMetadata.TryGetValue(id, out var m) ? m.DisplayName : id.Value;
            return $"magnet:?xt=urn:btih:{id.Value}&dn={Uri.EscapeDataString(name)}";
        }

        return null;
    }

    public string? GetSavePath(TorrentId id)
    {
        if (TryReadStatus(id, out var status))
        {
            return string.IsNullOrEmpty(status.SavePath) ? null : status.SavePath;
        }
        return null;
    }

    public string? GetName(TorrentId id)
    {
        var custom = _customNames.GetName(id);
        if (custom is not null)
        {
            return custom;
        }

        if (_handles.TryGetValue(id, out var manager))
        {
            return manager.Info.Metadata.Name;
        }

        return _magnetMetadata.TryGetValue(id, out var meta) ? meta.DisplayName : null;
    }

    public async Task<Result> SetNameAsync(TorrentId id, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure("Name must not be empty.");
        }

        await _customNames.SetNameAsync(id, name.Trim(), ct).ConfigureAwait(false);
        return Result.Success();
    }

    private void CacheTrackerHost(TorrentId id, string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return;
        var host = uri.Host;
        _trackerHostsCache.AddOrUpdate(
            id,
            addValueFactory: _ => [host],
            updateValueFactory: (_, existing) =>
                existing.Contains(host, StringComparer.OrdinalIgnoreCase)
                    ? existing
                    : [.. existing, host]);
    }

    public IReadOnlyList<string> GetTrackerHosts(TorrentId id) =>
        _trackerHostsCache.TryGetValue(id, out var hosts) ? hosts : Array.Empty<string>();

    private bool TryReadStatus(TorrentId id, out LibtorrentSharp.TorrentStatus status)
    {
        if (_handles.TryGetValue(id, out var manager))
        {
            try { status = manager.GetCurrentStatus(); return true; }
            catch { status = null!; return false; }
        }
        if (_magnets.TryGetValue(id, out var magnet) && magnet.IsValid)
        {
            try { status = magnet.GetCurrentStatus(); return true; }
            catch { status = null!; return false; }
        }
        status = null!;
        return false;
    }

    public async Task<TorrentDetailInfo?> GetTorrentDetailAsync(TorrentId id, CancellationToken ct = default)
    {
        var stateRecord = await _stateStore.GetByIdAsync(id, ct).ConfigureAwait(false);
        // stateRecord may be null for torrents loaded via libtorrent resume but lacking a
        // state-DB entry (e.g. the DB was cleared). We still show what we can; AddedDate
        // and CompletionDate will render as "—" when the record is missing.

        string? savePath = null;
        string? comment = null;
        string? creator = null;
        DateTimeOffset? creationDate = null;
        int totalPieces = 0;
        long pieceLength = 0;

        if (_handles.TryGetValue(id, out var handle))
        {
            // TryReadStatus covers both _handles and _magnets, but for TorrentHandle we
            // also need TorrentInfo (metadata) which is only on the handle, not the status.
            if (TryReadStatus(id, out var status))
            {
                savePath = string.IsNullOrEmpty(status.SavePath) ? null : status.SavePath;
            }

            var info = handle.Info;
            var meta = info.Metadata;
            comment = string.IsNullOrEmpty(meta.Comment) ? null : meta.Comment;
            creator = string.IsNullOrEmpty(meta.Creator) ? null : meta.Creator;
            // creation_epoch = 0 maps to 1970-01-01 UTC, which means the field wasn't set.
            creationDate = meta.CreatedAt.ToUnixTimeSeconds() <= 0 ? null : meta.CreatedAt;
            totalPieces = info.NumPieces;
            pieceLength = info.PieceLength;
        }
        else if (_magnets.TryGetValue(id, out var magnet))
        {
            savePath = string.IsNullOrEmpty(magnet.SavePath) ? null : magnet.SavePath;

            // Resume-loaded file torrents arrive as MagnetHandle but keep their TorrentInfo
            // in _torrentInfos (populated during AddAsync so metadata survives the add path).
            if (_torrentInfos.TryGetValue(id, out var torrentInfo))
            {
                var meta = torrentInfo.Metadata;
                comment = string.IsNullOrEmpty(meta.Comment) ? null : meta.Comment;
                creator = string.IsNullOrEmpty(meta.Creator) ? null : meta.Creator;
                creationDate = meta.CreatedAt.ToUnixTimeSeconds() <= 0 ? null : meta.CreatedAt;
                totalPieces = torrentInfo.NumPieces;
                pieceLength = torrentInfo.PieceLength;
            }
            else
            {
                // Pure magnet or resume-loaded magnet without a cached TorrentInfo —
                // NumPieces and PieceLength are available on the handle once metadata resolves.
                totalPieces = magnet.NumPieces;
                pieceLength = magnet.PieceLength;
            }
        }
        else
        {
            // Torrent is not loaded in the libtorrent session — nothing to show.
            return null;
        }

        return new TorrentDetailInfo(
            InfoHash: id.Value,
            SavePath: savePath,
            Comment: comment,
            Creator: creator,
            CreationDate: creationDate,
            AddedDate: stateRecord is not null
                ? new DateTimeOffset(stateRecord.AddedUtc, TimeSpan.Zero)
                : null,
            CompletionDate: stateRecord?.CompletedUtc.HasValue == true
                ? new DateTimeOffset(stateRecord.CompletedUtc.Value, TimeSpan.Zero)
                : null,
            TotalPieces: totalPieces,
            PieceLength: pieceLength
        );
    }

    public (long DownloadBps, long UploadBps)? GetSpeedLimits(TorrentId id)
    {
        if (_handles.TryGetValue(id, out var manager))
        {
            return (NormalizeRate(manager.DownloadRateLimit), NormalizeRate(manager.UploadRateLimit));
        }

        if (_magnets.TryGetValue(id, out var magnet) && magnet.IsValid)
        {
            return (NormalizeRate(magnet.DownloadRateLimit), NormalizeRate(magnet.UploadRateLimit));
        }

        return null;
    }

    public Task<Result> SetSpeedLimitsAsync(TorrentId id, long? downloadBps, long? uploadBps, CancellationToken ct = default) =>
        Task.FromResult(InvokeOnHandle(
            id,
            manager =>
            {
                if (downloadBps.HasValue) manager.DownloadRateLimit = ClampRate(downloadBps.Value);
                if (uploadBps.HasValue) manager.UploadRateLimit = ClampRate(uploadBps.Value);
            },
            magnet =>
            {
                if (downloadBps.HasValue) magnet.DownloadRateLimit = ClampRate(downloadBps.Value);
                if (uploadBps.HasValue) magnet.UploadRateLimit = ClampRate(uploadBps.Value);
            },
            "SetSpeedLimits"));

    public Task<Result> SetGlobalSpeedLimitsAsync(long downloadBps, long uploadBps, CancellationToken ct = default) =>
        Task.FromResult(ApplySettingsPack("SetGlobalSpeedLimits", pack =>
        {
            pack.Set("download_rate_limit", ClampRate(downloadBps));
            pack.Set("upload_rate_limit", ClampRate(uploadBps));
        }));

    public Task<Result> SetPortForwardingAsync(bool enabled, CancellationToken ct = default) =>
        Task.FromResult(ApplySettingsPack("SetPortForwarding", pack =>
        {
            pack.Set("enable_upnp", enabled);
            pack.Set("enable_natpmp", enabled);
        }));

    public Task<Result> SetEncryptionModeAsync(EncryptionMode mode, CancellationToken ct = default) =>
        Task.FromResult(ApplySettingsPack("SetEncryptionMode", pack =>
        {
            var policy = LibtorrentEncryptionMapper.ToPolicy(mode);
            pack.Set("out_enc_policy", policy);
            pack.Set("in_enc_policy", policy);
        }));

    public Task<Result> SetPeerDiscoveryAsync(bool dht, bool pex, bool lsd, CancellationToken ct = default)
    {
        var result = ApplySettingsPack("SetPeerDiscovery", pack =>
        {
            pack.Set("enable_dht", dht);
            pack.Set("enable_lsd", lsd);
        });
        _pexEnabled = pex;
        ApplyPexFlagToAllHandles(pex);
        return Task.FromResult(result);
    }

    private void ApplyPexFlagToAllHandles(bool pexEnabled)
    {
        foreach (var (_, handle) in _handles)
        {
            if (pexEnabled)
                handle.UnsetFlags(TorrentFlags.DisablePex);
            else
                handle.SetFlags(TorrentFlags.DisablePex);
        }
        foreach (var (_, magnet) in _magnets)
        {
            if (!magnet.IsValid)
                continue;
            if (pexEnabled)
                magnet.UnsetFlags(TorrentFlags.DisablePex);
            else
                magnet.SetFlags(TorrentFlags.DisablePex);
        }
    }

    private Result ApplySettingsPack(string actionLabel, Action<SettingsPack> mutate)
    {
        var client = _client;
        if (client is null)
        {
            // Settings appliers re-run after StartAsync, so a pre-start call returns
            // Success without effect rather than spamming failures during boot.
            return Result.Success();
        }

        try
        {
            var pack = new SettingsPack();
            mutate(pack);
            client.UpdateSettings(pack);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"{actionLabel} failed: {ex.Message}");
        }
    }

    private static int ClampRate(long rate) => (int)Math.Clamp(rate, 0L, int.MaxValue);

    private static long NormalizeRate(int libtorrentRate) => libtorrentRate < 0 ? 0L : libtorrentRate;

    public Task<Result> SetSuperSeedingAsync(TorrentId id, bool enabled, CancellationToken ct = default) =>
        Task.FromResult(InvokeOnHandle(
            id,
            manager => manager.SetSuperSeeding(enabled),
            magnet => magnet.SetSuperSeeding(enabled),
            "SetSuperSeeding"));

    public Task<Result> SetSequentialDownloadAsync(TorrentId id, bool enabled, CancellationToken ct = default) =>
        Task.FromResult(InvokeOnHandle(
            id,
            manager => manager.SetSequentialDownload(enabled),
            magnet => magnet.SetSequentialDownload(enabled),
            "SetSequentialDownload"));

    public Task<Result> RelocateTorrentAsync(TorrentId id, string newPath, CancellationToken ct = default) =>
        Task.FromResult(InvokeOnHandle(
            id,
            manager => manager.MoveStorage(newPath),
            magnet => magnet.MoveStorage(newPath),
            "MoveStorage"));

    public Task<Result> SetFirstLastPiecePriorityAsync(TorrentId id, bool enable, CancellationToken ct = default)
    {
        var result = InvokeOnHandle(
            id,
            handle =>
            {
                foreach (var file in handle.Files)
                {
                    if (file.Priority == LibtorrentSharp.Enums.FileDownloadPriority.DoNotDownload) continue;
                    var (first, last) = handle.Info.GetFilePieceRange(file.Info.Index);
                    var newPriority = enable ? LibtorrentSharp.Enums.FileDownloadPriority.High : file.Priority;
                    handle.SetPiecePriority(first, newPriority);
                    if (last != first) handle.SetPiecePriority(last, newPriority);
                }
                if (enable)
                    _firstLastPiecePriorityEnabled[id] = true;
                else
                    _firstLastPiecePriorityEnabled.TryRemove(id, out _);
            },
            _ => { },
            "SetFirstLastPiecePriority");
        return Task.FromResult(result);
    }

    public Task<Result> ForceStartTorrentAsync(TorrentId id, bool forceStart, CancellationToken ct = default) =>
        Task.FromResult(InvokeOnHandle(
            id,
            manager => manager.SetForceStart(forceStart),
            magnet => magnet.SetForceStart(forceStart),
            "SetForceStart"));

    public Task RenameFileAsync(TorrentId id, int fileIndex, string newRelativePath, CancellationToken ct = default)
    {
        // Validate before touching the engine so callers get a deterministic error
        // even when the engine hasn't started yet.
        ArgumentException.ThrowIfNullOrWhiteSpace(newRelativePath);

        // Fire-and-forget at the libtorrent level; FileRenamedAlert arrives later.
        // Errors (e.g. invalid index, I/O failure) surface through the alert pump.
        InvokeOnHandle(
            id,
            manager => manager.RenameFile(fileIndex, newRelativePath),
            magnet => magnet.RenameFile(fileIndex, newRelativePath),
            "RenameFile");

        return Task.CompletedTask;
    }

    public Task SetFilePriorityAsync(TorrentId id, int fileIndex, FileDownloadPriority priority, CancellationToken ct = default)
    {
        // Validate before touching the engine so callers get a deterministic error
        // even when the engine hasn't started yet.
        ArgumentOutOfRangeException.ThrowIfNegative(fileIndex);

        var nativePriority = MapToNativeFilePriority(priority);

        // Both branches look up the file by index and set Priority directly on the
        // TorrentManagerFile wrapper; the setter calls lts_set_file_priority immediately.
        if (_handles.TryGetValue(id, out var handle))
        {
            var file = handle.Files.FirstOrDefault(f => f.Info.Index == fileIndex);
            if (file is not null) file.Priority = nativePriority;
            return Task.CompletedTask;
        }

        if (_magnets.TryGetValue(id, out var magnet))
        {
            var file = magnet.GetFiles().FirstOrDefault(f => f.Info.Index == fileIndex);
            if (file is not null) file.Priority = nativePriority;
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    public ShareLimitSnapshot? GetShareLimitSnapshot(TorrentId id)
    {
        if (!TryReadStatus(id, out var status))
        {
            return null;
        }

        var paused = status.Flags.HasFlag(LibtorrentSharp.Enums.TorrentFlags.Paused);
        var autoManaged = status.Flags.HasFlag(LibtorrentSharp.Enums.TorrentFlags.AutoManaged);
        var superSeeding = status.Flags.HasFlag(LibtorrentSharp.Enums.TorrentFlags.SuperSeeding);
        var isFinished = status.State is LibtorrentSharp.Enums.TorrentState.Finished
            or LibtorrentSharp.Enums.TorrentState.Seeding;

        return new ShareLimitSnapshot(
            Id: id,
            State: MapState(status.State),
            IsFinished: isFinished,
            IsForced: !autoManaged && !paused,
            IsStopped: paused && !autoManaged,
            IsSuperSeeding: superSeeding,
            Ratio: status.Ratio ?? 0.0,
            BytesUploaded: status.BytesUploaded);
    }

    public SessionStats GetSessionStats()
    {
        var snapshots = Volatile.Read(ref _lastSnapshots);
        if (snapshots.Count == 0)
        {
            return default;
        }

        long down = 0, up = 0, sessionDown = 0, sessionUp = 0;
        int connections = 0;
        foreach (var s in snapshots)
        {
            down += s.DownloadSpeedBps;
            up += s.UploadSpeedBps;
            sessionDown += s.BytesDownloaded;
            sessionUp += s.BytesUploaded;
            connections += s.Peers;
        }

        return new SessionStats(
            GlobalDownloadBps: down,
            GlobalUploadBps: up,
            OpenConnections: connections,
            DhtNodes: _dhtNodeCount,
            SessionDownloadedBytes: sessionDown,
            SessionUploadedBytes: sessionUp);
    }

    public ValueTask DisposeAsync()
    {
        var client = _client;
        if (client is null)
        {
            return ValueTask.CompletedTask;
        }

        _client = null;
        ShutdownAlertPump();
        _handles.Clear();
        _magnets.Clear();
        _magnetMetadata.Clear();
        Interlocked.Exchange(ref _pendingPublish, 0);
        client.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string SeedingDiagHash(LibtorrentSharp.Sha1Hash hash)
    {
        var h = hash.ToString();
        return h.Length >= 8 ? h[..8] : h;
    }

}
