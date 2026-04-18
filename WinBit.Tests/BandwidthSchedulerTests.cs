using FluentAssertions;
using WinBit.Core.BitTorrent;
using WinBit.Core.Settings;
using Xunit;

namespace WinBit.Tests;

public sealed class BandwidthSchedulerTests
{
    /// <summary>
    /// Fixed-time TimeProvider. Returns the same wall clock for both UTC and local so the
    /// scheduler's <c>GetLocalNow()</c> reads exactly what the test sets.
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public int UpdateCalls { get; private set; }
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(Current);
        public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Action<AppSettings> mutate, CancellationToken ct = default)
        {
            UpdateCalls++;
            mutate(Current);
            Changed?.Invoke(this, Current);
            return Task.CompletedTask;
        }
        public event EventHandler<AppSettings>? Changed;
    }

    [Fact]
    public async Task Disabled_scheduler_does_not_touch_settings()
    {
        var settings = new InMemorySettingsService();
        settings.Current.Speed = new SpeedSettings
        {
            SchedulerEnabled = false,
            AltEnabled = false,
        };

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero));
        var scheduler = new BandwidthScheduler(settings, time);
        await scheduler.TickAsync(CancellationToken.None);

        settings.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task First_tick_aligns_AltEnabled_with_the_schedule()
    {
        var settings = new InMemorySettingsService();
        settings.Current.Speed = new SpeedSettings
        {
            SchedulerEnabled = true,
            SchedulerStartTime = new TimeOnly(9, 0),
            SchedulerEndTime = new TimeOnly(17, 0),
            SchedulerDays = BandwidthScheduleDays.EveryDay,
            AltEnabled = false,
        };

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero));
        var scheduler = new BandwidthScheduler(settings, time);
        await scheduler.TickAsync(CancellationToken.None);

        settings.Current.Speed.AltEnabled.Should().BeTrue();
        settings.UpdateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Stable_window_does_not_re_write_on_repeat_ticks()
    {
        var settings = new InMemorySettingsService();
        settings.Current.Speed = new SpeedSettings
        {
            SchedulerEnabled = true,
            SchedulerStartTime = new TimeOnly(9, 0),
            SchedulerEndTime = new TimeOnly(17, 0),
            SchedulerDays = BandwidthScheduleDays.EveryDay,
            AltEnabled = false,
        };

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero));
        var scheduler = new BandwidthScheduler(settings, time);
        await scheduler.TickAsync(CancellationToken.None);
        await scheduler.TickAsync(CancellationToken.None);
        await scheduler.TickAsync(CancellationToken.None);

        settings.UpdateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Leaving_the_window_flips_AltEnabled_back_off()
    {
        var settings = new InMemorySettingsService();
        settings.Current.Speed = new SpeedSettings
        {
            SchedulerEnabled = true,
            SchedulerStartTime = new TimeOnly(9, 0),
            SchedulerEndTime = new TimeOnly(17, 0),
            SchedulerDays = BandwidthScheduleDays.EveryDay,
            AltEnabled = false,
        };

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero));
        var scheduler = new BandwidthScheduler(settings, time);
        await scheduler.TickAsync(CancellationToken.None);
        settings.Current.Speed.AltEnabled.Should().BeTrue();

        time.Advance(TimeSpan.FromHours(6));
        await scheduler.TickAsync(CancellationToken.None);

        settings.Current.Speed.AltEnabled.Should().BeFalse();
        settings.UpdateCalls.Should().Be(2);
    }

    [Fact]
    public async Task Manual_toggle_between_ticks_survives_until_next_transition()
    {
        var settings = new InMemorySettingsService();
        settings.Current.Speed = new SpeedSettings
        {
            SchedulerEnabled = true,
            SchedulerStartTime = new TimeOnly(9, 0),
            SchedulerEndTime = new TimeOnly(17, 0),
            SchedulerDays = BandwidthScheduleDays.EveryDay,
            AltEnabled = false,
        };

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero));
        var scheduler = new BandwidthScheduler(settings, time);
        await scheduler.TickAsync(CancellationToken.None);
        // Scheduler has written AltEnabled=true and recorded _lastAlternative=true.

        // User toggles off manually.
        settings.Current.Speed.AltEnabled = false;

        // Time hasn't crossed a transition — scheduler still thinks alt should be ON, so it
        // should leave the user's manual OFF in place.
        await scheduler.TickAsync(CancellationToken.None);
        settings.Current.Speed.AltEnabled.Should().BeFalse();

        // Leave the window — scheduler flips to "alt OFF" which matches the current state.
        time.Advance(TimeSpan.FromHours(6));
        await scheduler.TickAsync(CancellationToken.None);
        settings.Current.Speed.AltEnabled.Should().BeFalse();
    }
}
