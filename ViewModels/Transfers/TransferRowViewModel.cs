using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;

namespace WinBit.ViewModels.Transfers;

/// <summary>
/// One row in the transfers grid. Instances are created when a torrent is added and updated in
/// place by <c>TransfersViewModel</c> per polling tick — never re-created, never removed and
/// re-added (see CLAUDE.md threading rules). Formatted companion strings live alongside raw
/// typed properties so columns can bind directly without per-column converters.
/// </summary>
public sealed partial class TransferRowViewModel : ObservableObject
{
    public TransferRowViewModel(TorrentId id, string name)
    {
        Id = id;
        this.name = name;
    }

    public TorrentId Id { get; }

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private long totalSize;

    /// <summary>Progress in 0..1.</summary>
    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private TorrentState state;

    [ObservableProperty]
    private int seeds;

    [ObservableProperty]
    private int peers;

    [ObservableProperty]
    private long downloadSpeedBps;

    [ObservableProperty]
    private long uploadSpeedBps;

    [ObservableProperty]
    private double ratio;

    [ObservableProperty]
    private TimeSpan? eta;

    [ObservableProperty]
    private DateTime addedUtc;

    [ObservableProperty]
    private DateTime? completedUtc;

    [ObservableProperty]
    private string? category;

    [ObservableProperty]
    private IReadOnlyList<string> tags = Array.Empty<string>();

    public double ProgressPercent => Progress * 100.0;

    public string SizeText => FormatBytes(TotalSize);

    public string ProgressText => $"{Progress * 100:0.0}%";

    public string StateLabel => State.ToString();

    public string DownloadText => FormatSpeed(DownloadSpeedBps);

    public string UploadText => FormatSpeed(UploadSpeedBps);

    public string RatioText => Ratio.ToString("0.00", CultureInfo.CurrentCulture);

    public string EtaText => Eta is { } e ? FormatEta(e) : "∞";

    public string AddedText => AddedUtc == default ? "—" : AddedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string CompletedText => CompletedUtc is { } c ? c.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "—";

    public string TagsText => Tags.Count == 0 ? string.Empty : string.Join(", ", Tags);

    partial void OnTotalSizeChanged(long value) => OnPropertyChanged(nameof(SizeText));
    partial void OnProgressChanged(double value)
    {
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(ProgressText));
    }
    partial void OnStateChanged(TorrentState value) => OnPropertyChanged(nameof(StateLabel));
    partial void OnDownloadSpeedBpsChanged(long value) => OnPropertyChanged(nameof(DownloadText));
    partial void OnUploadSpeedBpsChanged(long value) => OnPropertyChanged(nameof(UploadText));
    partial void OnRatioChanged(double value) => OnPropertyChanged(nameof(RatioText));
    partial void OnEtaChanged(TimeSpan? value) => OnPropertyChanged(nameof(EtaText));
    partial void OnAddedUtcChanged(DateTime value) => OnPropertyChanged(nameof(AddedText));
    partial void OnCompletedUtcChanged(DateTime? value) => OnPropertyChanged(nameof(CompletedText));
    partial void OnTagsChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(TagsText));

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

    private static string FormatSpeed(long bytesPerSec)
    {
        if (bytesPerSec <= 0)
        {
            return "—";
        }
        return $"{FormatBytes(bytesPerSec)}/s";
    }

    private static string FormatEta(TimeSpan eta)
    {
        if (eta.TotalDays >= 1)
        {
            return $"{(int)eta.TotalDays}d {eta.Hours}h";
        }
        if (eta.TotalHours >= 1)
        {
            return $"{eta.Hours}h {eta.Minutes}m";
        }
        if (eta.TotalMinutes >= 1)
        {
            return $"{eta.Minutes}m {eta.Seconds}s";
        }
        return $"{eta.Seconds}s";
    }
}
