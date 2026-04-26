using CommunityToolkit.Mvvm.ComponentModel;
using WinBit.Core.BitTorrent;

namespace WinBit.ViewModels.Transfers;

/// <summary>
/// One row in the Peers tab. Instances are created when a peer is first seen and updated
/// in place by <c>TorrentPropertiesViewModel</c> on each 3 s poll tick. Never re-created
/// on tick — addressed by the peer's <c>ip:port</c> string.
/// </summary>
public sealed partial class PeerRowViewModel : ObservableObject
{
    [ObservableProperty] private string address = string.Empty;
    [ObservableProperty] private string client = "—";
    [ObservableProperty] private string flags = "—";
    [ObservableProperty] private double progress;
    [ObservableProperty] private string downloadText = "—";
    [ObservableProperty] private string uploadText = "—";

    public void Update(PeerInfo info)
    {
        Address = info.Address;
        Client = info.Client ?? "—";
        Flags = PeerInfoFormatter.BuildFlags(info);
        Progress = info.Progress;
        DownloadText = PeerInfoFormatter.FormatSpeed(info.DownloadSpeedBps);
        UploadText = PeerInfoFormatter.FormatSpeed(info.UploadSpeedBps);
    }
}
