using Microsoft.Extensions.Hosting;
using WinBit.Core.BitTorrent;
using WinBit.Core.Settings;

namespace WinBit.Core.Power;

/// <summary>
/// Watches the per-tick torrent snapshot stream and keeps the OS awake while at least one torrent
/// is actively transferring bytes. Respects
/// <see cref="BehaviorSettings.PreventSleepWhileActive"/>: when the setting flips off, the
/// current sleep block (if any) is released immediately.
/// </summary>
public sealed class PowerManagementService : IHostedService
{
    private readonly ITorrentSessionService _session;
    private readonly ISettingsService _settings;
    private readonly ISleepInhibitor _inhibitor;

    public PowerManagementService(
        ITorrentSessionService session,
        ISettingsService settings,
        ISleepInhibitor inhibitor)
    {
        _session = session;
        _settings = settings;
        _inhibitor = inhibitor;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _session.TorrentUpdated += OnTorrentUpdated;
        _settings.Changed += OnSettingsChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _session.TorrentUpdated -= OnTorrentUpdated;
        _settings.Changed -= OnSettingsChanged;
        _inhibitor.SetActive(false);
        return Task.CompletedTask;
    }

    /// <summary>Test hook — runs the decision without an event subscription.</summary>
    public void Absorb(IReadOnlyList<TorrentSnapshot> batch) => OnTorrentUpdated(this, batch);

    private void OnTorrentUpdated(object? sender, IReadOnlyList<TorrentSnapshot> batch)
    {
        if (!_settings.Current.Behavior.PreventSleepWhileActive)
        {
            _inhibitor.SetActive(false);
            return;
        }

        _inhibitor.SetActive(HasActiveTransfer(batch));
    }

    private static bool HasActiveTransfer(IReadOnlyList<TorrentSnapshot> batch)
    {
        foreach (var snap in batch)
        {
            if (snap.DownloadSpeedBps > 0 || snap.UploadSpeedBps > 0)
            {
                return true;
            }
        }
        return false;
    }

    private void OnSettingsChanged(object? sender, AppSettings s)
    {
        if (!s.Behavior.PreventSleepWhileActive)
        {
            _inhibitor.SetActive(false);
        }
    }
}
