namespace LibtorrentSharp.Enums;

/// <summary>
/// Classification of a session-stats metric. Mirrors libtorrent's
/// <c>metric_type_t</c>. <see cref="Counter"/> values monotonically accumulate
/// for the lifetime of the session; <see cref="Gauge"/> values fluctuate up and
/// down (current peer count, queue depth, etc.).
/// </summary>
public enum MetricType
{
    Counter = 0,
    Gauge = 1
}
