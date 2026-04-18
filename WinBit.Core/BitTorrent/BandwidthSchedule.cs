using WinBit.Core.Settings;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Pure evaluator mirroring <c>BandwidthScheduler::isTimeForAlternative</c> in
/// <c>qbittorrent/src/base/bittorrent/bandwidthscheduler.cpp</c>. Given a start/end
/// <see cref="TimeOnly"/>, a <see cref="BandwidthScheduleDays"/> selector, and the current
/// time, returns whether the alt-speed profile should be active right now.
/// </summary>
public static class BandwidthSchedule
{
    public static bool IsTimeForAlternative(
        TimeOnly start,
        TimeOnly end,
        BandwidthScheduleDays days,
        DateTimeOffset now)
    {
        var current = TimeOnly.FromTimeSpan(now.TimeOfDay);
        var alternative = false;

        // Start > End means the window crosses midnight. qBittorrent swaps the two and starts
        // with `alternative = true`; the in-range branch then flips back to false, so "inside
        // the swapped range" maps to "outside the real, overnight range" and vice versa.
        if (start > end)
        {
            (start, end) = (end, start);
            alternative = true;
        }

        if (start <= current && end >= current)
        {
            if (DayMatches(days, now))
            {
                alternative = !alternative;
            }
        }

        return alternative;
    }

    private static bool DayMatches(BandwidthScheduleDays selector, DateTimeOffset now)
    {
        // Convert .NET DayOfWeek (Sunday=0 .. Saturday=6) to Qt's QDate::dayOfWeek
        // (Monday=1 .. Sunday=7) so the switch below stays a direct port.
        var isoDay = ((int)now.DayOfWeek + 6) % 7 + 1;

        return selector switch
        {
            BandwidthScheduleDays.EveryDay => true,
            BandwidthScheduleDays.Weekday => isoDay >= 1 && isoDay <= 5,
            BandwidthScheduleDays.Weekend => isoDay == 6 || isoDay == 7,
            BandwidthScheduleDays.Monday => isoDay == 1,
            BandwidthScheduleDays.Tuesday => isoDay == 2,
            BandwidthScheduleDays.Wednesday => isoDay == 3,
            BandwidthScheduleDays.Thursday => isoDay == 4,
            BandwidthScheduleDays.Friday => isoDay == 5,
            BandwidthScheduleDays.Saturday => isoDay == 6,
            BandwidthScheduleDays.Sunday => isoDay == 7,
            _ => false,
        };
    }
}
