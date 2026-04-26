// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

namespace LibtorrentSharp.Enums;

/// <summary>
/// Identifies which DHT subsystem emitted a <see cref="LibtorrentSharp.Alerts.DhtLogAlert"/>.
/// Mirrors libtorrent's <c>dht_log_alert::dht_module_t</c>.
/// </summary>
public enum DhtModule
{
    /// <summary>The tracker-resolution path inside the DHT (BEP 5 announce + scrape).</summary>
    Tracker = 0,

    /// <summary>The DHT node — top-level routing of incoming/outgoing DHT messages.</summary>
    Node = 1,

    /// <summary>The Kademlia routing table — bucket maintenance, node insertion/eviction.</summary>
    RoutingTable = 2,

    /// <summary>The RPC manager — outstanding DHT request bookkeeping (timeouts, retries).</summary>
    RpcManager = 3,

    /// <summary>An in-flight DHT traversal algorithm (find_node, get_peers, sample_infohashes, etc.).</summary>
    Traversal = 4
}
