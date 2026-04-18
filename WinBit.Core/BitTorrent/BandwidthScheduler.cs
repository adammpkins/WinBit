using Microsoft.Extensions.Hosting;
using WinBit.Core.Settings;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Hosted service port of qBittorrent's <c>BandwidthScheduler</c> (see
/// <c>qbittorrent/src/base/bittorrent/bandwidthscheduler.cpp</c>). Ticks every 30 s; when the
/// computed "should-be-alternative" value changes from its last observed state, writes
/// <c>AppSettings.Speed.AltEnabled</c> through <see cref="ISettingsService"/>, which
/// <c>SpeedProfileApplier</c> propagates to the engine. Only runs when
/// <c>AppSettings.Speed.SchedulerEnabled</c> is true; tracks its own last-emitted state so a
/// manual AltEnabled toggle between ticks isn't immediately reverted.
/// </summary>
public sealed class BandwidthScheduler : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly ISettingsService _settings;
    private readonly TimeProvider _time;

    private bool _lastAlternative;
    private bool _hasInitialState;

    public BandwidthScheduler(ISettingsService settings, TimeProvider? time = null)
    {
        _settings = settings;
        _time = time ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);

        try
        {
            await TickAsync(stoppingToken).ConfigureAwait(false);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutdown — normal.
        }
    }

    /// <summary>
    /// Runs a single evaluation pass. Public so tests can drive the scheduler without relying on
    /// <see cref="PeriodicTimer"/>.
    /// </summary>
    public async Task TickAsync(CancellationToken ct)
    {
        var speed = _settings.Current.Speed;
        if (!speed.SchedulerEnabled)
        {
            _hasInitialState = false;
            return;
        }

        var alt = BandwidthSchedule.IsTimeForAlternative(
            speed.SchedulerStartTime,
            speed.SchedulerEndTime,
            speed.SchedulerDays,
            _time.GetLocalNow());

        // First tick after scheduler is enabled — always push the computed state so AltEnabled
        // matches the scheduler's view. Mirrors qBittorrent's unconditional emit on start().
        if (!_hasInitialState)
        {
            _lastAlternative = alt;
            _hasInitialState = true;
            if (_settings.Current.Speed.AltEnabled != alt)
            {
                await _settings.UpdateAsync(s => s.Speed.AltEnabled = alt, ct).ConfigureAwait(false);
            }
            return;
        }

        // Subsequent ticks: only write on transitions so a user's manual AltEnabled toggle
        // between ticks survives until the next scheduled change.
        if (alt != _lastAlternative)
        {
            _lastAlternative = alt;
            await _settings.UpdateAsync(s => s.Speed.AltEnabled = alt, ct).ConfigureAwait(false);
        }
    }
}
