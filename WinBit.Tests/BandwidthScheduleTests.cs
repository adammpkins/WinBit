using FluentAssertions;
using WinBit.Core.BitTorrent;
using WinBit.Core.Settings;
using Xunit;

namespace WinBit.Tests;

/// <summary>
/// Parity fixtures for <see cref="BandwidthSchedule.IsTimeForAlternative"/> — exercises every
/// branch of qBittorrent's <c>BandwidthScheduler::isTimeForAlternative</c>.
/// </summary>
public sealed class BandwidthScheduleTests
{
    // 2026-04-20 is a Monday, 2026-04-25 is a Saturday, 2026-04-26 is a Sunday.
    private static DateTimeOffset At(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Straight_range_everyday_alt_on_inside()
    {
        var start = new TimeOnly(9, 0);
        var end = new TimeOnly(17, 0);

        BandwidthSchedule.IsTimeForAlternative(start, end, BandwidthScheduleDays.EveryDay, At(2026, 4, 20, 12, 0))
            .Should().BeTrue();
    }

    [Fact]
    public void Straight_range_everyday_alt_off_outside()
    {
        var start = new TimeOnly(9, 0);
        var end = new TimeOnly(17, 0);

        BandwidthSchedule.IsTimeForAlternative(start, end, BandwidthScheduleDays.EveryDay, At(2026, 4, 20, 8, 0))
            .Should().BeFalse();
        BandwidthSchedule.IsTimeForAlternative(start, end, BandwidthScheduleDays.EveryDay, At(2026, 4, 20, 18, 0))
            .Should().BeFalse();
    }

    [Fact]
    public void Wraparound_range_alt_on_during_overnight_window()
    {
        // Window is 22:00 → 06:00 next day. Inside the overnight window = alt ON.
        var start = new TimeOnly(22, 0);
        var end = new TimeOnly(6, 0);

        // Just after midnight.
        BandwidthSchedule.IsTimeForAlternative(start, end, BandwidthScheduleDays.EveryDay, At(2026, 4, 20, 1, 0))
            .Should().BeTrue();
        // 23:00 still inside the overnight window.
        BandwidthSchedule.IsTimeForAlternative(start, end, BandwidthScheduleDays.EveryDay, At(2026, 4, 20, 23, 0))
            .Should().BeTrue();
    }

    [Fact]
    public void Wraparound_range_alt_off_outside_overnight_window()
    {
        var start = new TimeOnly(22, 0);
        var end = new TimeOnly(6, 0);

        // 12:00 sits between end (06:00) and start (22:00) — outside the overnight window.
        BandwidthSchedule.IsTimeForAlternative(start, end, BandwidthScheduleDays.EveryDay, At(2026, 4, 20, 12, 0))
            .Should().BeFalse();
    }

    [Fact]
    public void Weekday_selector_skips_weekend_inside_range()
    {
        var start = new TimeOnly(9, 0);
        var end = new TimeOnly(17, 0);

        // Monday (weekday, inside range) — ON.
        BandwidthSchedule.IsTimeForAlternative(start, end, BandwidthScheduleDays.Weekday, At(2026, 4, 20, 12, 0))
            .Should().BeTrue();
        // Saturday (inside range) — OFF.
        BandwidthSchedule.IsTimeForAlternative(start, end, BandwidthScheduleDays.Weekday, At(2026, 4, 25, 12, 0))
            .Should().BeFalse();
    }

    [Fact]
    public void Weekend_selector_triggers_only_on_saturday_and_sunday()
    {
        var start = new TimeOnly(9, 0);
        var end = new TimeOnly(17, 0);

        BandwidthSchedule.IsTimeForAlternative(start, end, BandwidthScheduleDays.Weekend, At(2026, 4, 20, 12, 0))
            .Should().BeFalse();
        BandwidthSchedule.IsTimeForAlternative(start, end, BandwidthScheduleDays.Weekend, At(2026, 4, 25, 12, 0))
            .Should().BeTrue();
        BandwidthSchedule.IsTimeForAlternative(start, end, BandwidthScheduleDays.Weekend, At(2026, 4, 26, 12, 0))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(BandwidthScheduleDays.Monday, 20)]
    [InlineData(BandwidthScheduleDays.Tuesday, 21)]
    [InlineData(BandwidthScheduleDays.Wednesday, 22)]
    [InlineData(BandwidthScheduleDays.Thursday, 23)]
    [InlineData(BandwidthScheduleDays.Friday, 24)]
    [InlineData(BandwidthScheduleDays.Saturday, 25)]
    [InlineData(BandwidthScheduleDays.Sunday, 26)]
    public void Specific_day_selector_triggers_only_on_that_day(BandwidthScheduleDays selector, int day)
    {
        var start = new TimeOnly(9, 0);
        var end = new TimeOnly(17, 0);

        BandwidthSchedule.IsTimeForAlternative(start, end, selector, At(2026, 4, day, 12, 0)).Should().BeTrue();
        // One day before — should NOT match.
        BandwidthSchedule.IsTimeForAlternative(start, end, selector, At(2026, 4, day - 1, 12, 0)).Should().BeFalse();
    }

    [Fact]
    public void Wraparound_with_weekday_selector_inverts_as_in_qBittorrent()
    {
        // Overnight window 22:00 → 06:00 with Weekday selector. Parity with qBittorrent:
        // after the swap, `alternative` starts at true. The day-match branch only runs when
        // `now` is inside the swapped range (i.e. outside the real overnight window); inside
        // the overnight window the algorithm returns the swap default unchanged.
        var start = new TimeOnly(22, 0);
        var end = new TimeOnly(6, 0);

        // Tuesday 01:00 — inside overnight window (outside swapped range). Day-match branch
        // skipped, returns the swap default `true`.
        BandwidthSchedule.IsTimeForAlternative(start, end, BandwidthScheduleDays.Weekday, At(2026, 4, 21, 1, 0))
            .Should().BeTrue();
        // Tuesday 12:00 — outside overnight window (inside swapped range), weekday matches so
        // `alternative` flips from true to false.
        BandwidthSchedule.IsTimeForAlternative(start, end, BandwidthScheduleDays.Weekday, At(2026, 4, 21, 12, 0))
            .Should().BeFalse();
        // Saturday 12:00 — outside overnight window (inside swapped range), Weekday selector
        // does NOT match so no flip — returns the swap default `true`.
        BandwidthSchedule.IsTimeForAlternative(start, end, BandwidthScheduleDays.Weekday, At(2026, 4, 25, 12, 0))
            .Should().BeTrue();
    }
}
