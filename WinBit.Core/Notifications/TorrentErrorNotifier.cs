using Microsoft.Extensions.Hosting;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Logging;

namespace WinBit.Core.Notifications;

/// <summary>
/// Fans out an error toast the first tick a torrent transitions into <see cref="TorrentState.Error"/>
/// from a non-error state. Leaving the error state resets the tracking flag so a future re-entry
/// triggers a fresh notification — relevant when the user Resumes a torrent that then errors
/// again for a new reason.
/// </summary>
public sealed class TorrentErrorNotifier : IHostedService
{
    private readonly ITorrentSessionService _session;
    private readonly INotificationService _notifications;
    private readonly ILogService _log;
    private readonly Dictionary<TorrentId, bool> _inError = new();
    private readonly object _gate = new();

    public TorrentErrorNotifier(
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

    /// <summary>Test hook — invokes the error-detection path without an event subscription.</summary>
    public void Absorb(IReadOnlyList<TorrentSnapshot> batch) => OnTorrentUpdated(this, batch);

    private void OnTorrentUpdated(object? sender, IReadOnlyList<TorrentSnapshot> batch)
    {
        List<(TorrentId Id, string? Message)>? justErrored = null;
        lock (_gate)
        {
            foreach (var snap in batch)
            {
                var nowError = snap.State == TorrentState.Error;
                var firstSeen = !_inError.ContainsKey(snap.Id);
                var wasError = !firstSeen && _inError[snap.Id];
                // Only fire on a genuine transition. Torrents first observed already errored
                // (fast-resume at startup) are recorded silently.
                if (!firstSeen && nowError && !wasError)
                {
                    justErrored ??= new List<(TorrentId, string?)>();
                    justErrored.Add((snap.Id, snap.ErrorMessage));
                }
                _inError[snap.Id] = nowError;
            }
        }

        if (justErrored is null)
        {
            return;
        }

        foreach (var entry in justErrored)
        {
            var name = _session.GetName(entry.Id) ?? entry.Id.ToString();
            _ = FireAsync(name, entry.Message);
        }
    }

    private async Task FireAsync(string name, string? errorMessage)
    {
        try
        {
            await _notifications.NotifyTorrentErrorAsync(name, errorMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Write($"Error toast for \"{name}\" failed: {ex.Message}", LogSeverity.Warning);
        }
    }
}
