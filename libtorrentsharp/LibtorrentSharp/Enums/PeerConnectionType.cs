namespace LibtorrentSharp.Enums;

/// <summary>Typed mirror of libtorrent's <c>peer_info::connection_type_t</c>.</summary>
public enum PeerConnectionType
{
    /// <summary>Standard BitTorrent TCP connection.</summary>
    StandardBitTorrent = 0,
    /// <summary>HTTP-based web seed (BEP-19).</summary>
    WebSeed = 1,
    /// <summary>HTTP seed as defined by the GetRight spec (BEP-17).</summary>
    HttpSeed = 2,
}