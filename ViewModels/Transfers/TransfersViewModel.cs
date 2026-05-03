using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI.Collections;
using WinBit.Core.BitTorrent;
using WinBit.Core.Categories;
using WinBit.Core.Common;
using WinBit.Core.Filters;
using WinBit.Core.Tags;
using WinBit.Infrastructure;

namespace WinBit.ViewModels.Transfers;

/// <summary>
/// Drives the transfers grid. Subscribes to <see cref="ITorrentSessionService.TorrentUpdated"/>
/// and applies each 1 Hz snapshot batch *in place* via INPC on existing
/// <see cref="TransferRowViewModel"/> instances. New torrents add one row; removed torrents
/// remove one row. The collection is never cleared or rebuilt on a tick (CLAUDE.md threading
/// rules). The sidebar filter controls which rows are visible via
/// <see cref="AdvancedCollectionView.Filter"/>.
/// </summary>
public sealed partial class TransfersViewModel : ObservableObject
{
    private readonly ITorrentSessionService _session;
    private readonly IDispatcherQueueProvider _dispatcher;
    private readonly ICategoryService _categories;
    private readonly ITagService _tags;
    private readonly ObservableCollection<TransferRowViewModel> _rows = new();
    private readonly Dictionary<TorrentId, TransferRowViewModel> _rowsById = new();

    public TorrentPropertiesViewModel Properties { get; }

    public AdvancedCollectionView Rows { get; }

    [ObservableProperty]
    private bool hasTorrents;

    [ObservableProperty]
    private bool isEmpty;

    [ObservableProperty]
    private IReadOnlyList<Category> categoryOptions = Array.Empty<Category>();

    [ObservableProperty]
    private IReadOnlyList<string> tagOptions = Array.Empty<string>();

    [ObservableProperty]
    private IReadOnlyList<string> trackerHostOptions = Array.Empty<string>();

    [ObservableProperty]
    private TransferFilter selectedFilter = TransferFilter.All;

    [ObservableProperty]
    private object? selectedTorrentRow;

    partial void OnSelectedTorrentRowChanged(object? value) =>
        Properties.SetSelectedTorrent((value as TransferRowViewModel)?.Id);

    public TransfersViewModel(
        ITorrentSessionService session,
        IDispatcherQueueProvider dispatcher,
        ICategoryService categories,
        ITagService tags)
    {
        _session = session;
        _dispatcher = dispatcher;
        _categories = categories;
        _tags = tags;

        Properties = new TorrentPropertiesViewModel(session, dispatcher);

        Rows = new AdvancedCollectionView(_rows, isLiveShaping: true)
        {
            Filter = MatchesSelectedFilter,
        };
        _rows.CollectionChanged += (_, _) => UpdateCounts();
        UpdateCounts();

        _session.TorrentUpdated += OnTorrentUpdated;
    }

    public async Task RefreshFilterOptionsAsync(CancellationToken ct = default)
    {
        CategoryOptions = await _categories.GetAllAsync(ct).ConfigureAwait(false);
        TagOptions = await _tags.GetAllAsync(ct).ConfigureAwait(false);
    }

    public void ApplyFilter(TransferFilter filter)
    {
        SelectedFilter = filter;
        Rows.RefreshFilter();
    }

    private bool MatchesSelectedFilter(object? item) =>
        item is TransferRowViewModel row && SelectedFilter.Matches(new TransferFilterInputs(
            Category: row.Category,
            Tags: row.Tags,
            State: row.State,
            Progress: row.Progress,
            DownloadSpeedBps: row.DownloadSpeedBps,
            UploadSpeedBps: row.UploadSpeedBps,
            TrackerHosts: row.TrackerHosts));

    private void OnTorrentUpdated(object? sender, IReadOnlyList<TorrentSnapshot> batch)
    {
        _dispatcher.Enqueue(() => Apply(batch));
    }

    private void Apply(IReadOnlyList<TorrentSnapshot> batch)
    {
        var present = new HashSet<TorrentId>();

        foreach (var snap in batch)
        {
            present.Add(snap.Id);

            if (!_rowsById.TryGetValue(snap.Id, out var row))
            {
                var name = _session.GetName(snap.Id) ?? snap.Id.Value;
                row = new TransferRowViewModel(snap.Id, name);
                row.TrackerHosts = _session.GetTrackerHosts(snap.Id);
                // AddedUtc is immutable once set; CompletedUtc is null-to-value-only, assigned here
                // for torrents that were already complete at startup, and updated below for mid-session
                // completions on existing rows.
                row.AddedUtc = snap.AddedUtc;
                row.CompletedUtc = snap.CompletedUtc;
                _rowsById[snap.Id] = row;
                _rows.Add(row);
            }
            else
            {
                var freshName = _session.GetName(snap.Id);
                if (!string.IsNullOrEmpty(freshName) && row.Name != freshName)
                {
                    row.Name = freshName;
                }

                // Magnets don't know their trackers until metadata arrives; keep the row's list
                // in sync with the engine's so the sidebar picks up new hosts.
                if (row.TrackerHosts.Count == 0)
                {
                    var hosts = _session.GetTrackerHosts(snap.Id);
                    if (hosts.Count > 0)
                    {
                        row.TrackerHosts = hosts;
                    }
                }

                // CompletedUtc transitions null → value when a torrent finishes mid-session;
                // never update once set so the timestamp stays anchored to first completion.
                if (!row.CompletedUtc.HasValue && snap.CompletedUtc.HasValue)
                    row.CompletedUtc = snap.CompletedUtc;
            }

            row.State = snap.State;
            row.Progress = snap.Progress;
            row.DownloadSpeedBps = snap.DownloadSpeedBps;
            row.UploadSpeedBps = snap.UploadSpeedBps;
            row.Ratio = snap.Ratio;
            row.Eta = snap.Eta;
            row.Seeds = snap.Seeds;
            row.Peers = snap.Peers;
            row.IsSequentialDownload = snap.IsSequentialDownload;
            row.IsFirstLastPiecePriority = snap.HasFirstLastPiecePriority;
            row.IsForceStart = snap.IsForceStart;
            row.TotalSize = snap.TotalSize;
        }

        for (var i = _rows.Count - 1; i >= 0; i--)
        {
            var row = _rows[i];
            if (!present.Contains(row.Id))
            {
                _rowsById.Remove(row.Id);
                _rows.RemoveAt(i);
            }
        }

        // Status filters read fields that change every tick (state, progress, speeds). Refresh
        // so a torrent that just transitioned state migrates to the right bucket. Category/tag
        // filters don't need this — those fields only change on user edits.
        if (SelectedFilter.Kind == TransferFilterKind.Status)
        {
            Rows.RefreshFilter();
        }

        RefreshTrackerHostOptions();
    }

    private void RefreshTrackerHostOptions()
    {
        var distinct = _rows
            .SelectMany(r => r.TrackerHosts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!distinct.SequenceEqual(TrackerHostOptions, StringComparer.OrdinalIgnoreCase))
        {
            TrackerHostOptions = distinct;
        }
    }

    private void UpdateCounts()
    {
        HasTorrents = _rows.Count > 0;
        IsEmpty = _rows.Count == 0;
    }
}
