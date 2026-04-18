using CommunityToolkit.Mvvm.ComponentModel;
using WinBit.Core.BitTorrent;
using WinBit.Core.Settings;
using WinBit.Core.Stats;
using WinBit.Infrastructure;

namespace WinBit.ViewModels.Stats;

/// <summary>
/// Drives the StatsPage. Subscribes to <see cref="ITorrentSessionService.TorrentUpdated"/> for
/// the 1 Hz cadence, reads <see cref="ITorrentSessionService.GetSessionStats"/> + the all-time
/// counters kept by <see cref="IAllTimeStatsService"/>, and surfaces formatted text for each
/// stat card. Writes land on the UI thread via <see cref="IDispatcherQueueProvider"/>.
/// </summary>
public sealed partial class StatsViewModel : ObservableObject
{
    private readonly ITorrentSessionService _session;
    private readonly IAllTimeStatsService _allTime;
    private readonly ISettingsService _settings;
    private readonly IDispatcherQueueProvider _dispatcher;

    [ObservableProperty]
    private long allTimeDownloadedBytes;

    [ObservableProperty]
    private long allTimeUploadedBytes;

    [ObservableProperty]
    private long sessionDownloadedBytes;

    [ObservableProperty]
    private long sessionUploadedBytes;

    [ObservableProperty]
    private int dhtNodes;

    [ObservableProperty]
    private long freeSpaceBytes;

    [ObservableProperty]
    private string savePath = "—";

    public string AllTimeDownloadedText => FormatBytes(AllTimeDownloadedBytes);

    public string AllTimeUploadedText => FormatBytes(AllTimeUploadedBytes);

    public string AllTimeRatioText => FormatRatio(AllTimeUploadedBytes, AllTimeDownloadedBytes);

    public string SessionRatioText => FormatRatio(SessionUploadedBytes, SessionDownloadedBytes);

    public string DhtNodesText => DhtNodes.ToString();

    public string FreeSpaceText => FreeSpaceBytes > 0 ? FormatBytes(FreeSpaceBytes) : "—";

    public StatsViewModel(
        ITorrentSessionService session,
        IAllTimeStatsService allTime,
        ISettingsService settings,
        IDispatcherQueueProvider dispatcher)
    {
        _session = session;
        _allTime = allTime;
        _settings = settings;
        _dispatcher = dispatcher;

        _session.TorrentUpdated += OnTorrentUpdated;
        Refresh();
    }

    public void Refresh()
    {
        var stats = _session.GetSessionStats();
        var all = _allTime.Current;
        var path = _settings.Current.Downloads.DefaultSavePath;
        long free = 0;
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(root))
                {
                    var drive = new DriveInfo(root);
                    if (drive.IsReady)
                    {
                        free = drive.AvailableFreeSpace;
                    }
                }
            }
            catch
            {
                // Best-effort — unavailable path, removed drive, permissions — all silently fall
                // back to "—" in the UI.
            }
        }

        AllTimeDownloadedBytes = all.DownloadedBytes + stats.SessionDownloadedBytes;
        AllTimeUploadedBytes = all.UploadedBytes + stats.SessionUploadedBytes;
        SessionDownloadedBytes = stats.SessionDownloadedBytes;
        SessionUploadedBytes = stats.SessionUploadedBytes;
        DhtNodes = stats.DhtNodes;
        FreeSpaceBytes = free;
        SavePath = string.IsNullOrWhiteSpace(path) ? "—" : path;
    }

    private void OnTorrentUpdated(object? sender, IReadOnlyList<TorrentSnapshot> batch) =>
        _dispatcher.Enqueue(Refresh);

    partial void OnAllTimeDownloadedBytesChanged(long value)
    {
        OnPropertyChanged(nameof(AllTimeDownloadedText));
        OnPropertyChanged(nameof(AllTimeRatioText));
    }

    partial void OnAllTimeUploadedBytesChanged(long value)
    {
        OnPropertyChanged(nameof(AllTimeUploadedText));
        OnPropertyChanged(nameof(AllTimeRatioText));
    }

    partial void OnSessionDownloadedBytesChanged(long value) => OnPropertyChanged(nameof(SessionRatioText));

    partial void OnSessionUploadedBytesChanged(long value) => OnPropertyChanged(nameof(SessionRatioText));

    partial void OnDhtNodesChanged(int value) => OnPropertyChanged(nameof(DhtNodesText));

    partial void OnFreeSpaceBytesChanged(long value) => OnPropertyChanged(nameof(FreeSpaceText));

    private static string FormatRatio(long numerator, long denominator)
    {
        if (denominator <= 0)
        {
            return numerator > 0 ? "∞" : "0.00";
        }
        return ((double)numerator / denominator).ToString("0.00");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        string[] units = { "KB", "MB", "GB", "TB", "PB" };
        double value = bytes;
        int unit = -1;
        do
        {
            value /= 1024;
            unit++;
        } while (value >= 1024 && unit < units.Length - 1);
        return $"{value:0.##} {units[unit]}";
    }
}
