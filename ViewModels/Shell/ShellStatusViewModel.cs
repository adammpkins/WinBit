using CommunityToolkit.Mvvm.ComponentModel;
using WinBit.Core.BitTorrent;
using WinBit.Core.Settings;
using WinBit.Infrastructure;

namespace WinBit.ViewModels.Shell;

/// <summary>
/// Drives the MainWindow status bar. Subscribes to <see cref="ITorrentSessionService.TorrentUpdated"/>
/// for the 1 Hz tick cadence and re-reads <see cref="ITorrentSessionService.GetSessionStats"/> on
/// each tick; also mirrors <c>AppSettings.Speed.AltEnabled</c> so the footer stays in sync with
/// the title-bar toggle. All property writes land on the UI thread via
/// <see cref="IDispatcherQueueProvider"/>.
/// </summary>
public sealed partial class ShellStatusViewModel : ObservableObject
{
    private readonly ITorrentSessionService _session;
    private readonly ISettingsService _settings;
    private readonly IDispatcherQueueProvider _dispatcher;

    [ObservableProperty]
    private int dhtNodes;

    [ObservableProperty]
    private long globalDownloadBps;

    [ObservableProperty]
    private long globalUploadBps;

    [ObservableProperty]
    private int openConnections;

    [ObservableProperty]
    private bool altSpeedEnabled;

    public string GlobalDownloadText => $"↓ {FormatRate(GlobalDownloadBps)}";

    public string GlobalUploadText => $"↑ {FormatRate(GlobalUploadBps)}";

    public string DhtNodesText => $"DHT: {DhtNodes}";

    public string ConnectionsText => $"Peers: {OpenConnections}";

    public string AltSpeedText => AltSpeedEnabled ? "Alt speed: On" : "Alt speed: Off";

    public ShellStatusViewModel(
        ITorrentSessionService session,
        ISettingsService settings,
        IDispatcherQueueProvider dispatcher)
    {
        _session = session;
        _settings = settings;
        _dispatcher = dispatcher;

        AltSpeedEnabled = settings.Current.Speed.AltEnabled;

        _session.TorrentUpdated += OnTorrentUpdated;
        _settings.Changed += OnSettingsChanged;
    }

    public async Task ToggleAltSpeedAsync()
    {
        var next = !AltSpeedEnabled;
        await _settings.UpdateAsync(s => s.Speed.AltEnabled = next);
    }

    private void OnTorrentUpdated(object? sender, IReadOnlyList<TorrentSnapshot> batch)
    {
        var stats = _session.GetSessionStats();
        _dispatcher.Enqueue(() =>
        {
            DhtNodes = stats.DhtNodes;
            GlobalDownloadBps = stats.GlobalDownloadBps;
            GlobalUploadBps = stats.GlobalUploadBps;
            OpenConnections = stats.OpenConnections;
        });
    }

    private void OnSettingsChanged(object? sender, AppSettings s)
    {
        var enabled = s.Speed.AltEnabled;
        _dispatcher.Enqueue(() => AltSpeedEnabled = enabled);
    }

    partial void OnGlobalDownloadBpsChanged(long value) => OnPropertyChanged(nameof(GlobalDownloadText));

    partial void OnGlobalUploadBpsChanged(long value) => OnPropertyChanged(nameof(GlobalUploadText));

    partial void OnDhtNodesChanged(int value) => OnPropertyChanged(nameof(DhtNodesText));

    partial void OnOpenConnectionsChanged(int value) => OnPropertyChanged(nameof(ConnectionsText));

    partial void OnAltSpeedEnabledChanged(bool value) => OnPropertyChanged(nameof(AltSpeedText));

    private static string FormatRate(long bytesPerSec)
    {
        if (bytesPerSec <= 0)
        {
            return "0 B/s";
        }
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytesPerSec;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}/s";
    }
}
