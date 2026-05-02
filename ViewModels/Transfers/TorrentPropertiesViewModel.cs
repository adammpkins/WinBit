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

    private bool _contentTabActive;
    private CancellationTokenSource? _contentCts;
    private readonly ObservableCollection<TorrentFileEntry> _files = new();
    private readonly Dictionary<int, TorrentFileEntry> _filesByIndex = new();

    private bool _webSeedsTabActive;
    private CancellationTokenSource? _webSeedsCts;
    private readonly ObservableCollection<WebSeedRowViewModel> _webSeeds = new();
    private readonly Dictionary<string, WebSeedRowViewModel> _webSeedsByUrl = new();

    private bool _piecesTabActive;
    private CancellationTokenSource? _piecesCts;

    [ObservableProperty]
    private bool _hasFiles;

    [ObservableProperty]
    private bool _hasSelectedTorrent;

    [ObservableProperty]
    private WebSeedRowViewModel? _selectedWebSeed;

    /// <summary>True when a web seed row is selected; drives button IsEnabled bindings.</summary>
    public bool HasSelectedWebSeed => SelectedWebSeed is not null;

    [ObservableProperty]
    private TrackerRowViewModel? _selectedTracker;

    /// <summary>True when a tracker row is selected; drives button IsEnabled bindings.</summary>
    public bool HasSelectedTracker => SelectedTracker is not null;

    [ObservableProperty]
    private IReadOnlyList<bool> _pieceMap = Array.Empty<bool>();

    [ObservableProperty]
    private string _generalInfoHash = string.Empty;

    [ObservableProperty]
    private string _generalSavePath = string.Empty;

    [ObservableProperty]
    private string _generalComment = string.Empty;

    [ObservableProperty]
    private string _generalCreator = string.Empty;

    [ObservableProperty]
    private string _generalCreationDate = string.Empty;

    [ObservableProperty]
    private string _generalAddedDate = string.Empty;

    [ObservableProperty]
    private string _generalCompletionDate = string.Empty;

    [ObservableProperty]
    private string _generalPieces = string.Empty;

    public ObservableCollection<PeerRowViewModel> Peers => _peers;
    public ObservableCollection<TrackerRowViewModel> Trackers => _trackers;
    public ObservableCollection<TorrentFileEntry> Files => _files;
    public ObservableCollection<WebSeedRowViewModel> WebSeeds => _webSeeds;

    public TorrentPropertiesViewModel(
        ITorrentSessionService session,
        IDispatcherQueueProvider dispatcher)
    {
        _session = session;
        _dispatcher = dispatcher;
    }

    partial void OnSelectedTrackerChanged(TrackerRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedTracker));
    }

    partial void OnSelectedWebSeedChanged(WebSeedRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedWebSeed));
    }

    public void SetSelectedTorrent(TorrentId? id)
    {
        if (_selectedId == id) return;
        _selectedId = id;
        HasSelectedTorrent = id is not null;
        RestartPollIfNeeded();
        RestartTrackersPollIfNeeded();
        RestartContentPollIfNeeded();
        RestartPiecesPollIfNeeded();
        RestartWebSeedsPollIfNeeded();

        if (id is null)
        {
            _dispatcher.Enqueue(ClearGeneralFields);
        }
        else
        {
            _ = RefreshGeneralDetailAsync(id.Value);
        }
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

    public void SetContentTabActive(bool active)
    {
        if (_contentTabActive == active) return;
        _contentTabActive = active;
        RestartContentPollIfNeeded();
    }

    public void SetPiecesTabActive(bool active)
    {
        if (_piecesTabActive == active) return;
        _piecesTabActive = active;
        RestartPiecesPollIfNeeded();
    }

    public void SetWebSeedsTabActive(bool active)
    {
        if (_webSeedsTabActive == active) return;
        _webSeedsTabActive = active;
        RestartWebSeedsPollIfNeeded();
    }

    private void RestartPiecesPollIfNeeded()
    {
        _piecesCts?.Cancel();
        _piecesCts?.Dispose();
        _piecesCts = null;

        if (_selectedId is null || !_piecesTabActive)
        {
            _dispatcher.Enqueue(() => PieceMap = Array.Empty<bool>());
            return;
        }

        var cts = new CancellationTokenSource();
        _piecesCts = cts;
        _ = PollPiecesAsync(_selectedId.Value, cts.Token);
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

    private void RestartWebSeedsPollIfNeeded()
    {
        _webSeedsCts?.Cancel();
        _webSeedsCts?.Dispose();
        _webSeedsCts = null;

        if (_selectedId is null || !_webSeedsTabActive)
        {
            _dispatcher.Enqueue(() =>
            {
                _webSeeds.Clear();
                _webSeedsByUrl.Clear();
            });
            return;
        }

        var cts = new CancellationTokenSource();
        _webSeedsCts = cts;
        _ = PollWebSeedsAsync(_selectedId.Value, cts.Token);
    }

    private async Task PollWebSeedsAsync(TorrentId id, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            do
            {
                var seeds = await _session.GetWebSeedsAsync(id, ct).ConfigureAwait(false);
                _dispatcher.Enqueue(() => ApplyWebSeeds(seeds));
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
    }

    private void RestartContentPollIfNeeded()
    {
        _contentCts?.Cancel();
        _contentCts?.Dispose();
        _contentCts = null;

        if (_selectedId is null || !_contentTabActive)
        {
            _dispatcher.Enqueue(() =>
            {
                _files.Clear();
                _filesByIndex.Clear();
                HasFiles = false;
            });
            return;
        }

        var cts = new CancellationTokenSource();
        _contentCts = cts;
        _ = PollContentAsync(_selectedId.Value, cts.Token);
    }

    private async Task PollContentAsync(TorrentId id, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            do
            {
                var files = await _session.GetTorrentFilesAsync(id, ct).ConfigureAwait(false);
                _dispatcher.Enqueue(() => ApplyFiles(files));
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
    }

    private async Task PollPiecesAsync(TorrentId id, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            do
            {
                var pieces = await _session.GetPiecesAsync(id, ct).ConfigureAwait(false);
                _dispatcher.Enqueue(() => PieceMap = pieces);
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

    private void ApplyWebSeeds(IReadOnlyList<WebSeedInfo> incoming)
    {
        var seen = new HashSet<string>();
        foreach (var info in incoming)
        {
            var key = info.Url.ToString();
            seen.Add(key);
            if (_webSeedsByUrl.TryGetValue(key, out var row))
            {
                row.Url = key;
            }
            else
            {
                var newRow = new WebSeedRowViewModel { Url = key };
                _webSeedsByUrl[key] = newRow;
                _webSeeds.Add(newRow);
            }
        }

        // Remove web seeds no longer present in the incoming snapshot.
        var departed = _webSeedsByUrl.Keys.Except(seen).ToList();
        foreach (var url in departed)
        {
            if (_webSeedsByUrl.Remove(url, out var row))
                _webSeeds.Remove(row);
        }
    }

    private void ApplyFiles(IReadOnlyList<TorrentFileEntry> incoming)
    {
        var seen = new HashSet<int>();
        foreach (var entry in incoming)
        {
            seen.Add(entry.Index);
            if (_filesByIndex.ContainsKey(entry.Index))
            {
                // Records are immutable — replace the existing item in the collection
                // so INPC-aware bindings see the update.
                var pos = _files.IndexOf(_filesByIndex[entry.Index]);
                _filesByIndex[entry.Index] = entry;
                if (pos >= 0)
                    _files[pos] = entry;
            }
            else
            {
                _filesByIndex[entry.Index] = entry;
                _files.Add(entry);
            }
        }

        // Remove files no longer present in the incoming snapshot.
        var departed = _filesByIndex.Keys.Except(seen).ToList();
        foreach (var idx in departed)
        {
            if (_filesByIndex.Remove(idx, out var entry))
                _files.Remove(entry);
        }

        HasFiles = _files.Count > 0;
    }

    private async Task RefreshGeneralDetailAsync(TorrentId id)
    {
        TorrentDetailInfo? detail;
        try
        {
            detail = await _session.GetTorrentDetailAsync(id).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        if (detail is null)
        {
            _dispatcher.Enqueue(ClearGeneralFields);
            return;
        }

        const string Dash = "—";
        const string DateFormat = "yyyy-MM-dd HH:mm:ss";

        var infoHash = detail.InfoHash;
        var savePath = string.IsNullOrEmpty(detail.SavePath) ? Dash : detail.SavePath;
        var comment = string.IsNullOrEmpty(detail.Comment) ? Dash : detail.Comment;
        var creator = string.IsNullOrEmpty(detail.Creator) ? Dash : detail.Creator;
        var creationDate = detail.CreationDate.HasValue
            ? detail.CreationDate.Value.ToLocalTime().ToString(DateFormat)
            : Dash;
        var addedDate = detail.AddedDate.ToLocalTime().ToString(DateFormat);
        var completionDate = detail.CompletionDate.HasValue
            ? detail.CompletionDate.Value.ToLocalTime().ToString(DateFormat)
            : Dash;
        var pieces = detail.TotalPieces > 0 && detail.PieceLength > 0
            ? $"{detail.TotalPieces} × {detail.PieceLength / 1024} KiB"
            : Dash;

        _dispatcher.Enqueue(() =>
        {
            GeneralInfoHash = infoHash;
            GeneralSavePath = savePath;
            GeneralComment = comment;
            GeneralCreator = creator;
            GeneralCreationDate = creationDate;
            GeneralAddedDate = addedDate;
            GeneralCompletionDate = completionDate;
            GeneralPieces = pieces;
        });
    }

    private void ClearGeneralFields()
    {
        GeneralInfoHash = string.Empty;
        GeneralSavePath = string.Empty;
        GeneralComment = string.Empty;
        GeneralCreator = string.Empty;
        GeneralCreationDate = string.Empty;
        GeneralAddedDate = string.Empty;
        GeneralCompletionDate = string.Empty;
        GeneralPieces = string.Empty;
    }

    public async Task<Result> AddTrackerAsync(string url, int tier)
    {
        if (_selectedId is not { } id) return Result.Failure("No torrent selected.");
        return await _session.AddTrackerAsync(id, url, tier);
    }

    public async Task<Result> RemoveTrackerAsync(string url)
    {
        if (_selectedId is not { } id) return Result.Failure("No torrent selected.");
        return await _session.RemoveTrackerAsync(id, url);
    }

    public async Task<Result> EditTrackerAsync(string oldUrl, string newUrl, int newTier)
    {
        if (_selectedId is not { } id) return Result.Failure("No torrent selected.");
        return await _session.EditTrackerAsync(id, oldUrl, newUrl, newTier);
    }

    public async Task RenameFileAsync(int fileIndex, string newRelativePath)
    {
        if (_selectedId is not { } id) return;
        await _session.RenameFileAsync(id, fileIndex, newRelativePath);
    }

    public async Task SetFilePriorityAsync(int fileIndex, FileDownloadPriority priority)
    {
        if (_selectedId is not { } id) return;
        await _session.SetFilePriorityAsync(id, fileIndex, priority);
    }

    public async Task RenameFolderAsync(string oldFolderPath, string newFolderPath)
    {
        if (_selectedId is not { } id) return;
        foreach (var (index, newPath) in FolderRenameHelper.BuildRenamedPaths(_filesByIndex.Values, oldFolderPath, newFolderPath))
            await _session.RenameFileAsync(id, index, newPath);
    }

    public async Task<Result> AddWebSeedAsync(string url)
    {
        if (_selectedId is not { } id) return Result.Failure("No torrent selected.");
        return await _session.AddWebSeedAsync(id, url);
    }

    public async Task<Result> RemoveWebSeedAsync(string url)
    {
        if (_selectedId is not { } id) return Result.Failure("No torrent selected.");
        return await _session.RemoveWebSeedAsync(id, url);
    }

    public void Dispose()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _trackersCts?.Cancel();
        _trackersCts?.Dispose();
        _contentCts?.Cancel();
        _contentCts?.Dispose();
        _piecesCts?.Cancel();
        _piecesCts?.Dispose();
        _webSeedsCts?.Cancel();
        _webSeedsCts?.Dispose();
    }
}
