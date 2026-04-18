using Microsoft.Extensions.Hosting;
using WinBit.Core.BitTorrent;
using WinBit.Core.Logging;

namespace WinBit.Core.Hosting;

/// <summary>
/// Owns the torrent-engine lifecycle. <see cref="StartAsync"/> brings the engine up via
/// <see cref="ITorrentSessionService"/>; <see cref="StopAsync"/> cancels the autosave loop,
/// flushes fast-resume once more, stops, and disposes. Between start and stop, a 60 s
/// <see cref="PeriodicTimer"/> drives periodic fast-resume autosave per
/// <c>docs/torrent-engine.md</c>.
/// </summary>
public sealed class WinBitHostedService : IHostedService
{
    private static readonly TimeSpan AutosaveInterval = TimeSpan.FromSeconds(60);

    private readonly ITorrentSessionService _session;
    private readonly ILogService _log;
    private CancellationTokenSource? _autosaveCts;
    private Task? _autosaveTask;

    public WinBitHostedService(ITorrentSessionService session, ILogService log)
    {
        _session = session;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await _session.StartAsync(ct).ConfigureAwait(false);
        _log.Write("WinBit host started", LogSeverity.Info);

        _autosaveCts = new CancellationTokenSource();
        _autosaveTask = RunAutosaveLoopAsync(_autosaveCts.Token);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _autosaveCts?.Cancel();

        if (_autosaveTask is not null)
        {
            try
            {
                await _autosaveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        try
        {
            await _session.PersistFastResumeAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Write($"Final fast-resume flush failed: {ex.Message}", LogSeverity.Warning);
        }

        await _session.StopAsync(ct).ConfigureAwait(false);
        await _session.DisposeAsync().ConfigureAwait(false);
        _log.Write("WinBit host stopped", LogSeverity.Info);
    }

    private async Task RunAutosaveLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(AutosaveInterval);

        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await _session.PersistFastResumeAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.Write($"Fast-resume autosave failed: {ex.Message}", LogSeverity.Warning);
            }
        }
    }
}
