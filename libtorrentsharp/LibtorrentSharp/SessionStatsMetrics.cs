#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp;

/// <summary>
/// Static accessor for libtorrent's session-stats metric registry. The registry
/// is build-static — every call returns the same list, so the binding caches it
/// once on the native side and the managed list lazily on first access. Pair
/// with <see cref="LibtorrentSession.PostSessionStats"/> +
/// <see cref="Alerts.SessionStatsAlert"/>: enumerate <see cref="All"/> (or
/// resolve specific names via <see cref="FindIndex"/>) once at startup, then
/// index into <see cref="Alerts.SessionStatsAlert.Counters"/> at runtime.
/// </summary>
public static class SessionStatsMetrics
{
    private static IReadOnlyList<SessionStatsMetric>? _all;

    /// <summary>
    /// All metrics libtorrent exposes via <c>session_stats_metrics()</c>.
    /// Lazily populated on first access; cached for the process lifetime.
    /// Always non-null and non-empty in a healthy native build.
    /// </summary>
    public static IReadOnlyList<SessionStatsMetric> All => _all ??= LoadAll();

    /// <summary>
    /// Resolves <paramref name="metricName"/> to the index at which its value
    /// appears in <see cref="Alerts.SessionStatsAlert.Counters"/>. Returns
    /// <c>-1</c> if no metric matches. Forwards to libtorrent's
    /// <c>find_metric_idx</c> (O(log N) lookup).
    /// </summary>
    public static int FindIndex(string metricName)
    {
        ArgumentNullException.ThrowIfNull(metricName);
        return NativeMethods.SessionStatsFindMetricIdx(metricName);
    }

    private static IReadOnlyList<SessionStatsMetric> LoadAll()
    {
        var count = NativeMethods.SessionStatsMetricCount();
        if (count <= 0)
        {
            return Array.Empty<SessionStatsMetric>();
        }

        var managed = new SessionStatsMetric[count];
        for (var i = 0; i < count; i++)
        {
            NativeMethods.SessionStatsMetricAt(i, out var namePtr, out var valueIndex, out var typeRaw);
            // namePtr is a libtorrent-owned static literal — copy bytes, don't free.
            var name = namePtr == IntPtr.Zero
                ? string.Empty
                : Marshal.PtrToStringUTF8(namePtr) ?? string.Empty;
            managed[i] = new SessionStatsMetric(name, valueIndex, (MetricType)typeRaw);
        }
        return managed;
    }
}
