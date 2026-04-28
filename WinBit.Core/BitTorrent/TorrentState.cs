namespace WinBit.Core.BitTorrent;

/// <summary>
/// User-facing torrent state. Maps from libtorrent's richer torrent_status::state_t plus
/// pause/error flags; the UI renders one StatePill per value. Keep this list aligned with
/// docs/ui-design-language.md.
/// </summary>
public enum TorrentState
{
    Stopped,
    Paused,
    Checking,
    Queued,
    // Magnet add: fetching the .torrent manifest from peers via BEP 9 ut_metadata.
    // Progress is 0 in this phase because there is no piece layout yet.
    Metadata,
    Downloading,
    Seeding,
    Stalled,
    Completed,
    Error,
}
