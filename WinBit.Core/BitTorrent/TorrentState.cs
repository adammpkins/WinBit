namespace WinBit.Core.BitTorrent;

/// <summary>
/// User-facing torrent state. Maps from MonoTorrent's richer TorrentState enum; the UI renders
/// one StatePill per value. Keep this list aligned with docs/ui-design-language.md.
/// </summary>
public enum TorrentState
{
    Stopped,
    Paused,
    Checking,
    Queued,
    Downloading,
    Seeding,
    Stalled,
    Completed,
    Error,
}
