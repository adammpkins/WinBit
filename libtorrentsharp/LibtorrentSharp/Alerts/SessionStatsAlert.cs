using System;
using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Snapshot of the session's full performance-counter array, fired in response
/// to <see cref="LibtorrentSession.PostSessionStats"/>. The counter array is
/// flat — the mapping from metric name to index is queried separately via the
/// session_stats_metrics surface (a follow-up slice). The index layout is
/// stable for a given libtorrent build but may shift across libtorrent
/// versions, so consumers should resolve indices once at startup and reuse
/// them for the process lifetime.
/// </summary>
public class SessionStatsAlert : Alert
{
    internal SessionStatsAlert(NativeEvents.SessionStatsAlert alert)
        : base(alert.info)
    {
        if (alert.counters == IntPtr.Zero || alert.counters_count <= 0)
        {
            Counters = Array.Empty<long>();
        }
        else
        {
            var managed = new long[alert.counters_count];
            Marshal.Copy(alert.counters, managed, 0, alert.counters_count);
            Counters = managed;
        }
    }

    /// <summary>
    /// The full counter array as sampled at alert-emission time. Indexed
    /// positionally — consult session_stats_metrics for the name→index map.
    /// Always non-null; empty only on a degenerate/empty native dispatch.
    /// </summary>
    public long[] Counters { get; }
}
