using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.Client;
using WinBit.Core.Common;
using WinBit.Core.Hosting;
using WinBit.Core.Logging;
using WinBit.Core.Persistence;

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
    private readonly Paths _paths;
    private readonly WinBitCoreOptions _options;
    private ClientEngine? _engine;

    public TorrentSessionService(ILogService log, Paths paths, IOptions<WinBitCoreOptions> options)
    {
        _log = log;
        _paths = paths;
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

        var settings = new EngineSettingsBuilder
        {
            CacheDirectory = cacheDir,
            AutoSaveLoadFastResume = false,
            AllowPortForwarding = false,
            AllowLocalPeerDiscovery = false,
        }.ToSettings();

        _engine = new ClientEngine(settings);
        _log.Write($"Torrent engine started (cache: {cacheDir})", LogSeverity.Info);
        return Task.CompletedTask;
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

            if (parameters.StartImmediately)
            {
                await manager.StartAsync().ConfigureAwait(false);
            }

            var hash = (manager.InfoHashes.V1 ?? manager.InfoHashes.V2)!.ToHex();
            return Result<TorrentId>.Success(TorrentId.FromInfoHash(hash));
        }
        catch (Exception ex)
        {
            return Result<TorrentId>.Failure($"Add failed: {ex.Message}");
        }
    }

    public async Task<Result> RemoveAsync(TorrentId id, CancellationToken ct = default)
    {
        if (_engine is null)
        {
            return Result.Failure("Engine has not been started.");
        }

        var manager = _engine.Torrents.FirstOrDefault(m =>
            string.Equals((m.InfoHashes.V1 ?? m.InfoHashes.V2)!.ToHex(), id.Value, StringComparison.OrdinalIgnoreCase));

        if (manager is null)
        {
            return Result.Failure($"No torrent with info-hash {id.Value} is currently loaded.");
        }

        try
        {
            await _engine.RemoveAsync(manager, RemoveMode.CacheDataOnly).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Remove failed: {ex.Message}");
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

        var snapshots = _engine is null
            ? Array.Empty<TorrentSnapshot>()
            : _engine.Torrents.Select(Snapshot).ToArray();

        handler(this, snapshots);
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
        };
    }

    private static TorrentState MapState(MonoTorrent.Client.TorrentState state) => state switch
    {
        MonoTorrent.Client.TorrentState.Downloading => TorrentState.Downloading,
        MonoTorrent.Client.TorrentState.Seeding => TorrentState.Seeding,
        MonoTorrent.Client.TorrentState.Paused or MonoTorrent.Client.TorrentState.HashingPaused => TorrentState.Paused,
        MonoTorrent.Client.TorrentState.Hashing => TorrentState.Checking,
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
