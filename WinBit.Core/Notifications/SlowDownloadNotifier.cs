using Microsoft.Extensions.Hosting;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Logging;
using WinBit.Core.Settings;

namespace WinBit.Core.Notifications;

/// <summary>
/// Warns once when a long-running download stalls. A per-<see cref="TorrentId"/> timer starts
/// the first tick the torrent is observed in <see cref="TorrentState.Downloading"/>; after the
/// configured minimum age, if the current rate is below the user's floor, a toast fires. The
/// flag clears the next time the torrent leaves Downloading, so a subsequent re-entry arms the
/// warning again. Disabled entirely unless
/// <see cref="BehaviorSettings.SlowDownloadWarningEnabled"/> is true.
/// </summary>
public sealed class SlowDownloadNotifier : IHostedService
{
    private readonly ITorrentSessionService _session;
    private readonly INotificationService _notifications;
    private readonly ISettingsService _settings;
    private readonly ILogService _log;
    private readonly TimeProvider _clock;
    private readonly Dictionary<TorrentId, Entry> _entries = new();
    private readonly object _gate = new();

    private sealed class Entry
    {
        public DateTimeOffset DownloadingSince;
        public bool Notified;
    }

    public SlowDownloadNotifier(
        ITorrentSessionService session,
        INotificationService notifications,
        ISettingsService settings,
        ILogService log,
        TimeProvider clock)
    {
        _session = session;
        _notifications = notifications;
        _settings = settings;
        _log = log;
        _clock = clock;
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

    /// <summary>Test hook — runs detection without an event subscription.</summary>
    public void Absorb(IReadOnlyList<TorrentSnapshot> batch) => OnTorrentUpdated(this, batch);

    private void OnTorrentUpdated(object? sender, IReadOnlyList<TorrentSnapshot> batch)
    {
        var behavior = _settings.Current.Behavior;
        if (!behavior.SlowDownloadWarningEnabled)
        {
            return;
        }

        var now = _clock.GetUtcNow();
        var minAge = TimeSpan.FromMinutes(Math.Max(1, behavior.SlowDownloadWarningAfterMinutes));
        var rateFloor = Math.Max(0, behavior.SlowDownloadWarningRateBps);

        List<(TorrentId Id, long Rate)>? toFire = null;
        lock (_gate)
        {
            foreach (var snap in batch)
            {
                if (snap.State != TorrentState.Downloading)
                {
                    // Leaving Downloading clears the timer so a future re-entry can arm a fresh
                    // warning.
                    _entries.Remove(snap.Id);
                    continue;
                }

                if (!_entries.TryGetValue(snap.Id, out var entry))
                {
                    entry = new Entry { DownloadingSince = now };
                    _entries[snap.Id] = entry;
                }

                if (entry.Notified)
                {
                    continue;
                }

                if (now - entry.DownloadingSince < minAge)
                {
                    continue;
                }

                if (snap.DownloadSpeedBps >= rateFloor)
                {
                    continue;
                }

                entry.Notified = true;
                toFire ??= new List<(TorrentId, long)>();
                toFire.Add((snap.Id, snap.DownloadSpeedBps));
            }
        }

        if (toFire is null)
        {
            return;
        }

        foreach (var entry in toFire)
        {
            var name = _session.GetName(entry.Id) ?? entry.Id.ToString();
            _ = FireAsync(name, entry.Rate);
        }
    }

    private async Task FireAsync(string name, long rate)
    {
        try
        {
            await _notifications.NotifyDownloadRateLowAsync(name, rate).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Write($"Slow-download toast for \"{name}\" failed: {ex.Message}", LogSeverity.Warning);
        }
    }
}
