using Microsoft.Extensions.Hosting;
using WinBit.Core.BitTorrent;

namespace WinBit.Core.Hosting;

/// <summary>
/// 1 Hz background loop that asks <see cref="ITorrentSessionService"/> to snapshot every active
/// torrent and raise a batched <c>TorrentUpdated</c>. Exactly one dispatcher hop per tick, even
/// with hundreds of torrents — see CLAUDE.md threading rules.
/// </summary>
public sealed class StatusPollingLoop : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly ITorrentSessionService _session;

    public StatusPollingLoop(ITorrentSessionService session) => _session = session;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (!_session.IsRunning)
                {
                    continue;
                }

                _session.CaptureAndPublishSnapshots();
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutdown — expected.
        }
    }
}
