using LibtorrentSharp.Enums;

namespace LibtorrentSharp;

/// <summary>
/// One entry in libtorrent's session-stats metric registry. Pairs a metric
/// <see cref="Name"/> with the <see cref="Index"/> at which its value lives in
/// <see cref="Alerts.SessionStatsAlert.Counters"/>. The mapping is stable for
/// a given libtorrent build but may shift across versions, so consumers should
/// resolve indices once at startup and reuse them.
/// </summary>
/// <param name="Name">Metric identifier (e.g. <c>"net.recv_bytes"</c>); never null.</param>
/// <param name="Index">Position into <see cref="Alerts.SessionStatsAlert.Counters"/>.</param>
/// <param name="Type">Counter (monotonic) vs gauge (fluctuating).</param>
public readonly record struct SessionStatsMetric(string Name, int Index, MetricType Type);
