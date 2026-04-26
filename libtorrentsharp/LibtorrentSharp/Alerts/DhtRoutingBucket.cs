namespace LibtorrentSharp.Alerts;

/// <summary>
/// One bucket of the DHT routing table — appears in
/// <see cref="DhtStatsAlert.Buckets"/> in declaration order (closest first).
/// Mirrors libtorrent's <c>dht_routing_bucket</c>.
/// </summary>
/// <param name="NumNodes">Currently-live nodes in the bucket.</param>
/// <param name="NumReplacements">Pending replacement candidates.</param>
/// <param name="LastActiveSeconds">
/// Seconds since the bucket last saw activity. <b>Sentinel:</b> when the bucket
/// has never been active (typically on a freshly-bootstrapped DHT with empty
/// buckets — <see cref="NumNodes"/> == 0), libtorrent computes this as
/// <c>now - last_active_time_point</c> against an unset <c>time_point</c>, which
/// underflows to a large negative value (observed: ≈ <c>-2.1e9</c>, near
/// <c>int.MinValue</c>). Treat negative values as "never active" rather than
/// "active in the future". The marshal preserves the raw underflow rather than
/// clamping so consumers can distinguish "never-active" from "active right now"
/// (which is legitimately 0).
/// </param>
public readonly record struct DhtRoutingBucket(int NumNodes, int NumReplacements, int LastActiveSeconds);
