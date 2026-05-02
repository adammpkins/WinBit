using CommunityToolkit.Mvvm.ComponentModel;
using WinBit.Core.BitTorrent;

namespace WinBit.ViewModels.Transfers;

/// <summary>
/// One row in the Trackers tab. Instances are created when a tracker URL is first seen and
/// updated in place by <c>TorrentPropertiesViewModel</c> on each 3 s poll tick. The URL is
/// the stable identity key — never re-created while the tracker remains in the list.
/// </summary>
public sealed partial class TrackerRowViewModel : ObservableObject
{
    private readonly string _url;
    private int _tier;

    [ObservableProperty] private string statusDisplay = string.Empty;
    [ObservableProperty] private string seedsDisplay = "--";
    [ObservableProperty] private string leechesDisplay = "--";
    [ObservableProperty] private string completedDisplay = "--";
    [ObservableProperty] private string nextAnnounceDisplay = "--";
    [ObservableProperty] private string? lastError;

    public string Url => _url;

    /// <summary>Tracker tier (0 = primary). Updated each poll tick.</summary>
    public int Tier => _tier;

    public TrackerRowViewModel(TrackerInfo info)
    {
        _url = info.Url.ToString();
        Update(info);
    }

    public void Update(TrackerInfo info)
    {
        _tier = info.Tier;

        StatusDisplay = info.Status switch
        {
            TrackerStatus.NotContacted => "Not contacted",
            TrackerStatus.Updating => "Updating",
            TrackerStatus.Working => "Working",
            TrackerStatus.Failure => "Error",
            _ => "Not contacted",
        };

        SeedsDisplay = info.Seeds == -1 ? "--" : info.Seeds.ToString();
        LeechesDisplay = info.Leeches == -1 ? "--" : info.Leeches.ToString();
        CompletedDisplay = info.Completed == -1 ? "--" : info.Completed.ToString();

        if (info.NextAnnounceUtc is { } next)
        {
            var seconds = (int)Math.Ceiling((next - DateTimeOffset.UtcNow).TotalSeconds);
            NextAnnounceDisplay = seconds > 0 ? $"in {seconds}s" : "--";
        }
        else
        {
            NextAnnounceDisplay = "--";
        }

        LastError = info.LastError;
    }
}
