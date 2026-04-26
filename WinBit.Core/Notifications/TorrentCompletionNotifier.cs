using Microsoft.Extensions.Hosting;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Logging;

namespace WinBit.Core.Notifications;

/// <summary>
/// Watches <see cref="ITorrentSessionService.TorrentUpdated"/> for per-torrent progress
/// transitions from &lt;1.0 to ≥1.0 and fans each in-session completion out through
/// <see cref="INotificationService.NotifyTorrentCompletedAsync"/>. Torrents seen for the first
/// time at 100% are recorded silently — the toast is for work that just finished, not for state
/// reloaded from fast-resume at startup.
/// </summary>
public sealed class TorrentCompletionNotifier : IHostedService
{
    private readonly ITorrentSessionService _session;
    private readonly INotificationService _notifications;
    private readonly ILogService _log;
    private readonly Dictionary<TorrentId, double> _lastProgress = new();
    private readonly object _gate = new();

    public TorrentCompletionNotifier(
        ITorrentSessionService session,
        INotificationService notifications,
        ILogService log)
    {
        _session = session;
        _notifications = notifications;
        _log = log;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _session.TorrentUpdated += OnTorrentUpdated;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _session.TorrentUpdated -= OnTorrentUpdated;
        return Task.CompletedTask;
    }

    /// <summary>Test hook — invokes the completion-detection path without an event subscription.</summary>
    public void Absorb(IReadOnlyList<TorrentSnapshot> batch) => OnTorrentUpdated(this, batch);

    private void OnTorrentUpdated(object? sender, IReadOnlyList<TorrentSnapshot> batch)
    {
        List<TorrentId>? justCompleted = null;
        lock (_gate)
        {
            foreach (var snap in batch)
            {
                if (_lastProgress.TryGetValue(snap.Id, out var prev))
                {
                    if (prev < 1.0 && snap.Progress >= 1.0)
                    {
                        justCompleted ??= new List<TorrentId>();
                        justCompleted.Add(snap.Id);
                    }
                }
                _lastProgress[snap.Id] = snap.Progress;
            }
        }

        if (justCompleted is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            foreach (var id in justCompleted)
            {
                var name = _session.GetName(id) ?? id.ToString();
                var savePath = _session.GetSavePath(id) ?? string.Empty;
                await FireAsync(name, savePath);
            }
        });
    }

    private async Task FireAsync(string name, string savePath)
    {
        try
        {
            await _notifications.NotifyTorrentCompletedAsync(name, savePath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Write($"Completion toast for \"{name}\" failed: {ex.Message}", LogSeverity.Warning);
        }
    }
}
