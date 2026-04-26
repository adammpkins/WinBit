namespace LibtorrentSharp.Enums;

/// <summary>
/// Bitmask describing how a peer was discovered. Mirrors libtorrent's
/// <c>peer_info::peer_source_flags</c>. Useful for surfacing in a Peers
/// tab why a given peer is connected (e.g. distinguishing peers from
/// the tracker's announce response, DHT lookups, peer exchange with
/// other connected peers, or local-network discovery).
/// </summary>
[System.Flags]
public enum PeerSource : byte
{
    /// <summary>No source recorded — typically only for placeholder entries.</summary>
    None = 0,

    /// <summary>Peer was returned by a tracker's announce response.</summary>
    Tracker = 1,

    /// <summary>Peer was discovered via DHT (BEP-5).</summary>
    Dht = 2,

    /// <summary>Peer was learned from another connected peer via peer exchange (PEX, BEP-11).</summary>
    Pex = 4,

    /// <summary>Peer was discovered via Local Service Discovery (LSD, BEP-14).</summary>
    Lsd = 8,

    /// <summary>Peer was loaded from a previously-saved resume blob.</summary>
    ResumeData = 16,

    /// <summary>Peer initiated the connection to us (incoming).</summary>
    Incoming = 32,
}
