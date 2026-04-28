namespace WinBit.Core.BitTorrent;

public enum FileDownloadPriority
{
    DoNotDownload = 0,
    Low = 1,
    Normal = 4,
    // qBittorrent exposes High (6) and Maximum (7) as distinct user-facing levels.
    // libtorrent's download_priority_t scale is 0-7; both 6 and 7 are "active" but
    // Maximum gets stronger piece-picker bias for streaming-first scenarios.
    High = 6,
    Maximum = 7,
}
