using System.Net;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Connections;
using WinBit.Core.Common;
using WinBit.Core.Hosting;
using WinBit.Core.Logging;
using WinBit.Core.Networking;
using WinBit.Core.Persistence;
using WinBit.Core.Settings;
using WinBit.Core.Sharing;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Wraps MonoTorrent's <see cref="ClientEngine"/>. <see cref="StartAsync"/> constructs the engine
/// (engines are hot on construction — it begins listening for peer connections immediately).
/// <see cref="StopAsync"/> stops every <c>TorrentManager</c>. <see cref="DisposeAsync"/> stops
/// and disposes. Richer surface (Add/Remove/Recheck, typed snapshots) lands with the M3 types
/// deliverable.
/// </summary>
public sealed class TorrentSessionService : ITorrentSessionService
{
    private readonly ILogService _log;
    private readonly IPeerLogService _peerLog;
    private readonly Paths _paths;
    private readonly IIpFilterService _ipFilter;
    private readonly WinBitCoreOptions _options;
    private ClientEngine? _engine;

    public TorrentSessionService(ILogService log, IPeerLogService peerLog, Paths paths, IIpFilterService ipFilter, IOptions<WinBitCoreOptions> options)
    {
        _log = log;
        _peerLog = peerLog;
        _paths = paths;
        _ipFilter = ipFilter;
        _options = options.Value;
    }

    public bool IsRunning => _engine is not null;

    public IReadOnlyList<TorrentId> Torrents =>
        _engine is null
            ? Array.Empty<TorrentId>()
            : _engine.Torrents
                .Select(m => TorrentId.FromInfoHash((m.InfoHashes.V1 ?? m.InfoHashes.V2)!.ToHex()))
                .ToArray();

    public event EventHandler<IReadOnlyList<TorrentSnapshot>>? TorrentUpdated;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_engine is not null)
        {
            return Task.CompletedTask;
        }

        var cacheDir = Path.Combine(_paths.Root, "engine");
        Directory.CreateDirectory(cacheDir);

        var builder = new EngineSettingsBuilder
        {
            CacheDirectory = cacheDir,
            AutoSaveLoadFastResume = false,
            AllowPortForwarding = _options.AllowPortForwarding,
            AllowLocalPeerDiscovery = _options.AllowLocalPeerDiscovery,
        };

        if (_options.ListenPort > 0)
        {
            builder.ListenEndPoints = new Dictionary<string, System.Net.IPEndPoint>
            {
                { "ipv4", new System.Net.IPEndPoint(System.Net.IPAddress.Any, _options.ListenPort) },
            };
            // Explicit DHT endpoint on the same port — MonoTorrent won't bootstrap DHT without it.
            builder.DhtEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, _options.ListenPort);
        }

        _engine = new ClientEngine(builder.ToSettings());
        _engine.ConnectionManager.BanPeer += OnBanPeerAttempt;
        _log.Write($"Torrent engine started (cache: {cacheDir}, port: {_options.ListenPort}, UPnP: {_options.AllowPortForwarding}, LPD: {_options.AllowLocalPeerDiscovery})", LogSeverity.Info);
        return Task.CompletedTask;
    }

    private void OnBanPeerAttempt(object? sender, AttemptConnectionEventArgs args)
    {
        if (_ipFilter.RuleCount == 0)
        {
            return;
        }
        if (IPAddress.TryParse(args.Peer.ConnectionUri.Host, out var addr) && _ipFilter.IsBlocked(addr))
        {
            args.BanPeer = true;
            _peerLog.Record(args.Peer.ConnectionUri.ToString(), "Blocked by IP filter");
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_engine is null)
        {
            return;
        }

        await _engine.StopAllAsync().ConfigureAwait(false);
        _log.Write("Torrent engine stopped", LogSeverity.Info);
    }

    public async Task<Result<TorrentId>> AddAsync(AddTorrentParams parameters, CancellationToken ct = default)
    {
        if (_engine is null)
        {
            return Result<TorrentId>.Failure("Engine has not been started.");
        }

        Directory.CreateDirectory(parameters.SavePath);

        try
        {
            TorrentManager manager;

            if (parameters.Source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                var link = MagnetLink.Parse(parameters.Source);
                manager = await _engine.AddAsync(link, parameters.SavePath).ConfigureAwait(false);
            }
            else if (File.Exists(parameters.Source))
            {
                var torrent = await Torrent.LoadAsync(parameters.Source).ConfigureAwait(false);
                manager = await _engine.AddAsync(torrent, parameters.SavePath).ConfigureAwait(false);
            }
            else
            {
                return Result<TorrentId>.Failure($"Unknown torrent source: '{parameters.Source}'. Expected a magnet URI or a local .torrent path.");
            }

            var hash = (manager.InfoHashes.V1 ?? manager.InfoHashes.V2)!.ToHex();
            var shortHash = hash[..8];

            manager.TorrentStateChanged += (_, e) =>
                _log.Write($"Torrent {shortHash} state: {e.OldState} → {e.NewState}", LogSeverity.Info);

            manager.PeerConnected += (_, e) =>
                _log.Write($"Torrent {shortHash} peer+ {e.Peer.Uri}", LogSeverity.Info);

            manager.PeerDisconnected += (_, e) =>
                _log.Write($"Torrent {shortHash} peer- {e.Peer.Uri}", LogSeverity.Info);

            manager.ConnectionAttemptFailed += (_, e) =>
                _log.Write($"Torrent {shortHash} conn-fail {e.Peer.ConnectionUri} reason:{e.Reason}", LogSeverity.Warning);

            _log.Write($"Added torrent {shortHash} ({parameters.Source[..Math.Min(parameters.Source.Length, 60)]}) → {parameters.SavePath}", LogSeverity.Info);

            if (parameters.StartImmediately)
            {
                await manager.StartAsync().ConfigureAwait(false);

                // Default behavior announces tier-by-tier; kick every tier at once so we don't
                // starve on a low-traffic first tracker.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await manager.TrackerManager.AnnounceAsync(CancellationToken.None).ConfigureAwait(false);
                        _log.Write($"Torrent {shortHash} initial announce dispatched to {manager.TrackerManager.Tiers.Sum(t => t.Trackers.Count)} trackers", LogSeverity.Info);
                    }
                    catch (Exception ex)
                    {
                        _log.Write($"Torrent {shortHash} initial announce error: {ex.Message}", LogSeverity.Warning);
                    }
                });
            }

            return Result<TorrentId>.Success(TorrentId.FromInfoHash(hash));
        }
        catch (Exception ex)
        {
            return Result<TorrentId>.Failure($"Add failed: {ex.Message}");
        }
    }

    public async Task<Result> RemoveAsync(TorrentId id, bool deleteContent = false, CancellationToken ct = default)
    {
        var manager = FindManager(id);
        if (manager is null || _engine is null)
        {
            return Result.Failure($"No torrent with info-hash {id.Value} is currently loaded.");
        }

        var mode = deleteContent ? RemoveMode.CacheDataAndDownloadedData : RemoveMode.CacheDataOnly;

        try
        {
            await _engine.RemoveAsync(manager, mode).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Remove failed: {ex.Message}");
        }
    }

    public Task<Result> PauseAsync(TorrentId id, CancellationToken ct = default) =>
        RunOnManagerAsync(id, async m => await m.PauseAsync().ConfigureAwait(false));

    public Task<Result> ResumeAsync(TorrentId id, CancellationToken ct = default) =>
        RunOnManagerAsync(id, async m => await m.StartAsync().ConfigureAwait(false));

    public Task<Result> ForceRecheckAsync(TorrentId id, CancellationToken ct = default) =>
        RunOnManagerAsync(id, async m => await m.HashCheckAsync(autoStart: true).ConfigureAwait(false));

    public Task<Result> ForceReannounceAsync(TorrentId id, CancellationToken ct = default) =>
        RunOnManagerAsync(id, async m => await m.TrackerManager.AnnounceAsync(CancellationToken.None).ConfigureAwait(false));

    public string? GetMagnetUri(TorrentId id)
    {
        var manager = FindManager(id);
        if (manager is null)
        {
            return null;
        }
        var magnet = new MagnetLink(manager.InfoHashes, manager.Torrent?.Name);
        return magnet.ToV1String();
    }

    public string? GetSavePath(TorrentId id) => FindManager(id)?.SavePath;

    public string? GetName(TorrentId id) => FindManager(id)?.Torrent?.Name;

    public IReadOnlyList<string> GetTrackerHosts(TorrentId id)
    {
        var manager = FindManager(id);
        if (manager is null)
        {
            return Array.Empty<string>();
        }

        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tier in manager.TrackerManager.Tiers)
        {
            foreach (var tracker in tier.Trackers)
            {
                var host = tracker.Uri.Host;
                if (!string.IsNullOrWhiteSpace(host))
                {
                    hosts.Add(host);
                }
            }
        }
        return hosts.OrderBy(h => h, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public (long DownloadBps, long UploadBps)? GetSpeedLimits(TorrentId id)
    {
        var manager = FindManager(id);
        if (manager is null)
        {
            return null;
        }
        return (manager.Settings.MaximumDownloadRate, manager.Settings.MaximumUploadRate);
    }

    public SessionStats GetSessionStats()
    {
        if (_engine is null)
        {
            return default;
        }

        long sessionDown = 0, sessionUp = 0;
        foreach (var manager in _engine.Torrents)
        {
            sessionDown += manager.Monitor.DataBytesReceived;
            sessionUp += manager.Monitor.DataBytesSent;
        }

        return new SessionStats(
            GlobalDownloadBps: _engine.TotalDownloadRate,
            GlobalUploadBps: _engine.TotalUploadRate,
            OpenConnections: _engine.ConnectionManager.OpenConnections,
            DhtNodes: _engine.Dht.NodeCount,
            SessionDownloadedBytes: sessionDown,
            SessionUploadedBytes: sessionUp);
    }

    public ShareLimitSnapshot? GetShareLimitSnapshot(TorrentId id)
    {
        var manager = FindManager(id);
        if (manager is null)
        {
            return null;
        }

        var ratio = manager.Monitor.DataBytesReceived == 0
            ? 0.0
            : (double)manager.Monitor.DataBytesSent / manager.Monitor.DataBytesReceived;

        var state = manager.State;
        var isStopped = state is MonoTorrent.Client.TorrentState.Stopped
            or MonoTorrent.Client.TorrentState.Stopping;
        // MonoTorrent lacks qBittorrent's "forced" concept — wire it up if/when the engine
        // gains a forced-priority surface.
        return new ShareLimitSnapshot(
            Id: id,
            State: MapState(state),
            IsFinished: manager.Complete,
            IsForced: false,
            IsStopped: isStopped,
            IsSuperSeeding: manager.IsInitialSeeding,
            Ratio: ratio,
            BytesUploaded: manager.Monitor.DataBytesSent);
    }

    public Task<Result> SetSpeedLimitsAsync(TorrentId id, long? downloadBps, long? uploadBps, CancellationToken ct = default) =>
        RunOnManagerAsync(id, async m =>
        {
            var builder = new TorrentSettingsBuilder(m.Settings);
            if (downloadBps.HasValue)
            {
                builder.MaximumDownloadRate = (int)Math.Clamp(downloadBps.Value, 0L, int.MaxValue);
            }
            if (uploadBps.HasValue)
            {
                builder.MaximumUploadRate = (int)Math.Clamp(uploadBps.Value, 0L, int.MaxValue);
            }
            await m.UpdateSettingsAsync(builder.ToSettings()).ConfigureAwait(false);
        });

    public async Task<Result> SetGlobalSpeedLimitsAsync(long downloadBps, long uploadBps, CancellationToken ct = default)
    {
        if (_engine is null)
        {
            // Engine hasn't started yet; SpeedProfileApplier re-runs after StartAsync.
            return Result.Success();
        }

        try
        {
            var builder = new EngineSettingsBuilder(_engine.Settings)
            {
                MaximumDownloadRate = (int)Math.Clamp(downloadBps, 0L, int.MaxValue),
                MaximumUploadRate = (int)Math.Clamp(uploadBps, 0L, int.MaxValue),
            };
            await _engine.UpdateSettingsAsync(builder.ToSettings()).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    public async Task<Result> SetPortForwardingAsync(bool enabled, CancellationToken ct = default)
    {
        if (_engine is null)
        {
            return Result.Success();
        }

        try
        {
            var builder = new EngineSettingsBuilder(_engine.Settings)
            {
                AllowPortForwarding = enabled,
            };
            await _engine.UpdateSettingsAsync(builder.ToSettings()).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    public async Task<Result> SetEncryptionModeAsync(EncryptionMode mode, CancellationToken ct = default)
    {
        if (_engine is null)
        {
            return Result.Success();
        }

        try
        {
            var builder = new EngineSettingsBuilder(_engine.Settings)
            {
                AllowedEncryption = EncryptionMapper.ToMonoTorrent(mode),
            };
            await _engine.UpdateSettingsAsync(builder.ToSettings()).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    public async Task<Result> SetPeerDiscoveryAsync(bool dht, bool pex, bool lsd, CancellationToken ct = default)
    {
        if (_engine is null)
        {
            return Result.Success();
        }

        try
        {
            var engineBuilder = new EngineSettingsBuilder(_engine.Settings)
            {
                AllowLocalPeerDiscovery = lsd,
            };
            await _engine.UpdateSettingsAsync(engineBuilder.ToSettings()).ConfigureAwait(false);

            foreach (var manager in _engine.Torrents)
            {
                var tb = new TorrentSettingsBuilder(manager.Settings)
                {
                    AllowDht = dht,
                    AllowPeerExchange = pex,
                };
                await manager.UpdateSettingsAsync(tb.ToSettings()).ConfigureAwait(false);
            }
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    public Task<Result> SetSuperSeedingAsync(TorrentId id, bool enabled, CancellationToken ct = default) =>
        RunOnManagerAsync(id, async m =>
        {
            var builder = new TorrentSettingsBuilder(m.Settings) { AllowInitialSeeding = enabled };
            await m.UpdateSettingsAsync(builder.ToSettings()).ConfigureAwait(false);
        });

    private TorrentManager? FindManager(TorrentId id) =>
        _engine?.Torrents.FirstOrDefault(m =>
            string.Equals((m.InfoHashes.V1 ?? m.InfoHashes.V2)!.ToHex(), id.Value, StringComparison.OrdinalIgnoreCase));

    private async Task<Result> RunOnManagerAsync(TorrentId id, Func<TorrentManager, Task> action)
    {
        var manager = FindManager(id);
        if (manager is null)
        {
            return Result.Failure($"No torrent with info-hash {id.Value} is currently loaded.");
        }

        try
        {
            await action(manager).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    public Task PersistFastResumeAsync(CancellationToken ct = default)
    {
        if (_engine is null)
        {
            return Task.CompletedTask;
        }

        // Blob extraction via MonoTorrent's FastResume lands with the Add/Remove flow.
        // For now the engine holds no managers, so the autosave loop has nothing to do.
        foreach (var _ in _engine.Torrents)
        {
            ct.ThrowIfCancellationRequested();
        }

        return Task.CompletedTask;
    }

    public void CaptureAndPublishSnapshots()
    {
        var handler = TorrentUpdated;
        if (handler is null)
        {
            return;
        }

        var snapshots = GetSnapshots();

        LogDiagnostics();

        handler(this, snapshots);
    }

    public IReadOnlyList<TorrentSnapshot> GetSnapshots() =>
        _engine is null
            ? Array.Empty<TorrentSnapshot>()
            : _engine.Torrents.Select(Snapshot).ToArray();

    private int _diagTick;

    private void LogDiagnostics()
    {
        if (_engine is null)
        {
            return;
        }

        _diagTick++;
        if (_diagTick % 5 != 0)
        {
            // Throttle to every 5 s.
            return;
        }

        var listen = _engine.Settings.ListenEndPoints is { Count: > 0 } eps
            ? string.Join(", ", eps.Select(kvp => $"{kvp.Key}={kvp.Value}"))
            : "(none)";

        _log.Write($"Engine diag — listen:{listen} torrents:{_engine.Torrents.Count}", LogSeverity.Info);

        foreach (var manager in _engine.Torrents)
        {
            var hash = (manager.InfoHashes.V1 ?? manager.InfoHashes.V2)!.ToHex()[..8];
            var trackerCount = 0;
            var workingTrackers = 0;
            foreach (var tier in manager.TrackerManager.Tiers)
            {
                foreach (var tracker in tier.Trackers)
                {
                    trackerCount++;
                    if (tracker.Status == MonoTorrent.Trackers.TrackerState.Ok)
                    {
                        workingTrackers++;
                    }
                }
            }

            _log.Write(
                $"  {hash} state:{manager.State} seeds:{manager.Peers.Seeds} leeches:{manager.Peers.Leechs} "
                + $"progress:{manager.Progress:0.0}% trackers:{workingTrackers}/{trackerCount} "
                + $"open:{manager.OpenConnections} available:{manager.Peers.Available}",
                LogSeverity.Info);

            foreach (var tier in manager.TrackerManager.Tiers)
            {
                foreach (var tracker in tier.Trackers)
                {
                    var scheme = tracker.Uri.Scheme;
                    var host = tracker.Uri.Host;
                    _log.Write(
                        $"    tracker [{scheme}] {host}:{tracker.Uri.Port} status:{tracker.Status} fail:{tracker.FailureMessage ?? "-"} warn:{tracker.WarningMessage ?? "-"}",
                        LogSeverity.Info);
                }
            }
        }
    }

    private static TorrentSnapshot Snapshot(TorrentManager manager)
    {
        var hash = (manager.InfoHashes.V1 ?? manager.InfoHashes.V2)!.ToHex();
        return new TorrentSnapshot
        {
            Id = TorrentId.FromInfoHash(hash),
            State = MapState(manager.State),
            Progress = manager.Progress / 100.0,
            BytesDownloaded = manager.Monitor.DataBytesReceived,
            BytesUploaded = manager.Monitor.DataBytesSent,
            DownloadSpeedBps = manager.Monitor.DownloadRate,
            UploadSpeedBps = manager.Monitor.UploadRate,
            Ratio = manager.Monitor.DataBytesReceived == 0
                ? 0
                : (double)manager.Monitor.DataBytesSent / manager.Monitor.DataBytesReceived,
            Seeds = manager.Peers.Seeds,
            Peers = manager.Peers.Leechs,
            ErrorMessage = TorrentErrorFormatter.Format(manager.Error),
        };
    }

    private static TorrentState MapState(MonoTorrent.Client.TorrentState state) => state switch
    {
        MonoTorrent.Client.TorrentState.Downloading => TorrentState.Downloading,
        MonoTorrent.Client.TorrentState.Seeding => TorrentState.Seeding,
        MonoTorrent.Client.TorrentState.Paused or MonoTorrent.Client.TorrentState.HashingPaused => TorrentState.Paused,
        MonoTorrent.Client.TorrentState.Hashing or MonoTorrent.Client.TorrentState.FetchingHashes => TorrentState.Checking,
        MonoTorrent.Client.TorrentState.Metadata => TorrentState.Downloading,
        MonoTorrent.Client.TorrentState.Error => TorrentState.Error,
        MonoTorrent.Client.TorrentState.Stopped or MonoTorrent.Client.TorrentState.Stopping => TorrentState.Stopped,
        MonoTorrent.Client.TorrentState.Starting => TorrentState.Queued,
        _ => TorrentState.Stopped,
    };

    public async ValueTask DisposeAsync()
    {
        if (_engine is null)
        {
            return;
        }

        try
        {
            await _engine.StopAllAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort shutdown; individual manager failures shouldn't block disposal.
        }

        _engine.Dispose();
        _engine = null;
    }
}
