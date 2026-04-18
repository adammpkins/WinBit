using WinBit.Core.Common;
using WinBit.Core.Logging;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// M1 stub. M3 replaces the internals with a MonoTorrent ClientEngine wrapper.
/// </summary>
public sealed class TorrentSessionService : ITorrentSessionService
{
    private readonly ILogService _log;

    public TorrentSessionService(ILogService log) => _log = log;

    public bool IsRunning { get; private set; }

    public IReadOnlyList<TorrentId> Torrents => Array.Empty<TorrentId>();

    public Task StartAsync(CancellationToken ct = default)
    {
        IsRunning = true;
        _log.Write("Torrent session started (stub).", LogSeverity.Info);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        IsRunning = false;
        _log.Write("Torrent session stopped (stub).", LogSeverity.Info);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsRunning = false;
        return ValueTask.CompletedTask;
    }
}
