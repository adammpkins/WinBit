using CommunityToolkit.Mvvm.ComponentModel;
using WinBit.Core.BitTorrent;

namespace WinBit.ViewModels.Transfers;

public sealed partial class TransfersViewModel : ObservableObject
{
    private readonly ITorrentSessionService _session;

    [ObservableProperty]
    private bool hasTorrents;

    public TransfersViewModel(ITorrentSessionService session)
    {
        _session = session;
        HasTorrents = _session.Torrents.Count > 0;
    }
}
