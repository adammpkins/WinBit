namespace WinBit.Core.BitTorrent;

/// <summary>
/// Point-in-time snapshot of static torrent metadata for the General detail tab.
/// Fields sourced from libtorrent's torrent_info are null when metadata has not yet
/// resolved (e.g. a magnet that hasn't received its info dictionary).
/// </summary>
public sealed record TorrentDetailInfo(
    string InfoHash,
    string? SavePath,
    string? Comment,
    string? Creator,
    DateTimeOffset? CreationDate,
    DateTimeOffset? AddedDate,
    DateTimeOffset? CompletionDate,
    int TotalPieces,
    long PieceLength
);
