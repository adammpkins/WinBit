namespace LibtorrentSharp.Enums;

/// <summary>
/// Bitmask describing how a tracker entry came to be attached to a
/// torrent. Mirrors libtorrent's <c>announce_entry::tracker_source</c>.
/// Useful for surfacing in a Trackers tab why a given tracker is
/// attached (e.g. distinguishing a manually-added tracker from one
/// in the .torrent's announce list, or one obtained via tracker
/// exchange with a peer).
/// </summary>
[System.Flags]
public enum TrackerSource : byte
{
    /// <summary>No source recorded — typically only for placeholder entries before libtorrent has classified them.</summary>
    None = 0,

    /// <summary>The tracker was listed in the .torrent file's announce-list (the most common case for typical .torrents).</summary>
    TorrentFile = 1,

    /// <summary>The tracker was added via the C++ API after the torrent was added (e.g. via a manual "Add Tracker" UI action).</summary>
    Client = 2,

    /// <summary>The tracker was extracted from a magnet URI's <c>tr=</c> parameter.</summary>
    MagnetLink = 4,

    /// <summary>The tracker was discovered via tracker exchange (TEX, BEP-28) — a peer told us about it.</summary>
    TrackerExchange = 8,
}
