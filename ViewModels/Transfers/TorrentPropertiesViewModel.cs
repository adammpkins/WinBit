using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Infrastructure;

namespace WinBit.ViewModels.Transfers;

/// <summary>
/// Drives the properties panel below the transfers grid. Owns the per-tab polling loops
/// that run only while a specific tab is visible and a torrent is selected. The Peers tab
/// polls at 3 s and updates rows in place — no collection rebuilds on tick.
/// </summary>
public sealed partial class TorrentPropertiesViewModel : ObservableObject, IDisposable
{
    private readonly ITorrentSessionService _session;
    private readonly IDispatcherQueueProvider _dispatcher;

    private TorrentId? _selectedId;
    private bool _peersTabActive;
    private CancellationTokenSource? _pollCts;
    private readonly ObservableCollection<PeerRowViewModel> _peers = new();
    private readonly Dictionary<string, PeerRowViewModel> _peersByAddress = new();

    private bool _trackersTabActive;
    private CancellationTokenSource? _trackersCts;
    private readonly ObservableCollection<TrackerRowViewModel> _trackers = new();
    private readonly Dictionary<string, TrackerRowViewModel> _trackersByUrl = new();

    public ObservableCollection<PeerRowViewModel> Peers => _peers;
    public ObservableCollection<TrackerRowViewModel> Trackers => _trackers;

    public TorrentPropertiesViewModel(
        ITorrentSessionService session,
        IDispatcherQueueProvider dispatcher)
    {
        _session = session;
        _dispatcher = dispatcher;
    }

    public void SetSelectedTorrent(TorrentId? id)
    {
        if (_selectedId == id) return;
        _selectedId = id;
        RestartPollIfNeeded();
        RestartTrackersPollIfNeeded();
    }

    public void SetPeersTabActive(bool active)
    {
        if (_peersTabActive == active) return;
        _peersTabActive = active;
        RestartPollIfNeeded();
    }

    public void SetTrackersTabActive(bool active)
    {
        if (_trackersTabActive == active) return;
        _trackersTabActive = active;
        RestartTrackersPollIfNeeded();
    }

    private void RestartPollIfNeeded()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;

        if (_selectedId is null || !_peersTabActive)
        {
            _dispatcher.Enqueue(() =>
            {
                _peers.Clear();
                _peersByAddress.Clear();
            });
            return;
        }

        var cts = new CancellationTokenSource();
        _pollCts = cts;
        _ = PollLoopAsync(_selectedId.Value, cts.Token);
    }

    private void RestartTrackersPollIfNeeded()
    {
        _trackersCts?.Cancel();
        _trackersCts?.Dispose();
        _trackersCts = null;

        if (_selectedId is null || !_trackersTabActive)
        {
            _dispatcher.Enqueue(() =>
            {
                _trackers.Clear();
                _trackersByUrl.Clear();
            });
            return;
        }

        var cts = new CancellationTokenSource();
        _trackersCts = cts;
        _ = PollTrackersAsync(_selectedId.Value, cts.Token);
    }

    private async Task PollLoopAsync(TorrentId id, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            do
            {
                var peers = await _session.GetPeersAsync(id, ct).ConfigureAwait(false);
                _dispatcher.Enqueue(() => ApplyPeers(peers));
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
    }

    private async Task PollTrackersAsync(TorrentId id, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            do
            {
                var trackers = await _session.GetTrackersAsync(id, ct).ConfigureAwait(false);
                _dispatcher.Enqueue(() => ApplyTrackers(trackers));
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
    }

    private void ApplyPeers(IReadOnlyList<PeerInfo> incoming)
    {
        var seen = new HashSet<string>();
        foreach (var info in incoming)
        {
            seen.Add(info.Address);
            if (_peersByAddress.TryGetValue(info.Address, out var row))
            {
                row.Update(info);
            }
            else
            {
                var newRow = new PeerRowViewModel();
                newRow.Update(info);
                _peersByAddress[info.Address] = newRow;
                _peers.Add(newRow);
            }
        }

        // Remove departed peers (no longer in the incoming snapshot).
        var departed = _peersByAddress.Keys.Except(seen).ToList();
        foreach (var addr in departed)
        {
            if (_peersByAddress.Remove(addr, out var row))
                _peers.Remove(row);
        }
    }

    private void ApplyTrackers(IReadOnlyList<TrackerInfo> incoming)
    {
        var seen = new HashSet<string>();
        foreach (var info in incoming)
        {
            var key = info.Url.ToString();
            seen.Add(key);
            if (_trackersByUrl.TryGetValue(key, out var row))
            {
                row.Update(info);
            }
            else
            {
                var newRow = new TrackerRowViewModel(info);
                _trackersByUrl[key] = newRow;
                _trackers.Add(newRow);
            }
        }

        // Remove trackers no longer present in the incoming snapshot.
        var departed = _trackersByUrl.Keys.Except(seen).ToList();
        foreach (var url in departed)
        {
            if (_trackersByUrl.Remove(url, out var row))
                _trackers.Remove(row);
        }
    }

    public void Dispose()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _trackersCts?.Cancel();
        _trackersCts?.Dispose();
    }
}
