namespace LibtorrentSharp.Alerts;

/// <summary>
/// One outstanding DHT lookup — appears in <see cref="DhtStatsAlert.Lookups"/>.
/// Mirrors libtorrent's <c>dht_lookup</c>. <see cref="Type"/> is libtorrent's
/// label for the lookup ("get_peers" / "announce" / "put" / "get" /
/// "obfuscated_get_peers"); the value comes from a static string literal so
/// it is safe to compare with ordinal equality.
/// </summary>
/// <param name="Type">String label for the lookup category.</param>
/// <param name="Target">The node-id or info-hash being looked up (20 bytes).</param>
/// <param name="OutstandingRequests">Requests in flight to individual nodes right now.</param>
/// <param name="Timeouts">Total requests for this lookup that have timed out.</param>
/// <param name="Responses">Total responses received for this lookup.</param>
/// <param name="BranchFactor">Parallel-request budget (may grow as nodes time out).</param>
/// <param name="NodesLeft">Nodes that could still be queried for this lookup.</param>
/// <param name="LastSentSeconds">Seconds since the last still-outstanding request was sent.</param>
/// <param name="FirstTimeoutSeconds">Outstanding requests past the short timeout (have grown branch factor).</param>
public readonly record struct DhtLookup(
    string Type,
    Sha1Hash Target,
    int OutstandingRequests,
    int Timeouts,
    int Responses,
    int BranchFactor,
    int NodesLeft,
    int LastSentSeconds,
    int FirstTimeoutSeconds);
