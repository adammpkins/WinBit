using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI.Collections;
using WinBit.Core.BitTorrent;

namespace WinBit.ViewModels.Transfers;

public sealed partial class TransfersViewModel : ObservableObject
{
    private readonly ITorrentSessionService _session;
    private readonly ObservableCollection<TransferRowViewModel> _rows = new();

    public AdvancedCollectionView Rows { get; }

    [ObservableProperty]
    private bool hasTorrents;

    [ObservableProperty]
    private bool isEmpty;

    public TransfersViewModel(ITorrentSessionService session)
    {
        _session = session;
        Rows = new AdvancedCollectionView(_rows, isLiveShaping: true);
        _rows.CollectionChanged += (_, _) => UpdateCounts();
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        HasTorrents = _rows.Count > 0;
        IsEmpty = _rows.Count == 0;
    }
}
