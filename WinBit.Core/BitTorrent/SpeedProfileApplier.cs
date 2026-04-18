using Microsoft.Extensions.Hosting;
using WinBit.Core.Logging;
using WinBit.Core.Settings;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Maps <c>AppSettings.Speed</c> onto the engine's global rate limits. Subscribes to
/// <see cref="ISettingsService.Changed"/> so toggling alt-speed (from the title-bar button or
/// the Speed settings page) immediately re-applies the effective profile. Registered after
/// <c>WinBitHostedService</c> in the Core DI chain so the engine is running on first apply.
/// </summary>
public sealed class SpeedProfileApplier : IHostedService
{
    private readonly ITorrentSessionService _session;
    private readonly ISettingsService _settings;
    private readonly ILogService _log;

    public SpeedProfileApplier(ITorrentSessionService session, ISettingsService settings, ILogService log)
    {
        _session = session;
        _settings = settings;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _settings.Changed += OnSettingsChanged;
        await ApplyAsync(_settings.Current, ct).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken ct)
    {
        _settings.Changed -= OnSettingsChanged;
        return Task.CompletedTask;
    }

    /// <summary>Exposed for tests. Computes the effective profile and pushes it to the engine.</summary>
    public Task ApplyAsync(AppSettings settings, CancellationToken ct)
    {
        var speed = settings.Speed;
        var (down, up) = speed.AltEnabled
            ? (speed.AltDownBps, speed.AltUpBps)
            : (speed.GlobalDownBps, speed.GlobalUpBps);
        return ApplyRatesAsync(down, up, ct);
    }

    private async Task ApplyRatesAsync(long down, long up, CancellationToken ct)
    {
        var result = await _session.SetGlobalSpeedLimitsAsync(down, up, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            _log.Write($"Speed profile apply failed: {result.Error}", LogSeverity.Warning);
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings s) =>
        _ = ApplyAsync(s, CancellationToken.None);
}
