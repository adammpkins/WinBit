// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
// csdl - a cross-platform libtorrent wrapper for .NET
// Licensed under Apache-2.0 - see the license file for more information

namespace LibtorrentSharp.Enums;

/// <summary>
/// Download priority for a single file (or a single piece) within a torrent.
/// Surfaced via <see cref="LibtorrentSharp.TorrentHandle.TorrentManagerFile.Priority"/>
/// (per-file), <see cref="LibtorrentSharp.TorrentHandle"/> / <see cref="LibtorrentSharp.MagnetHandle"/>
/// piece-priority APIs (<c>GetPiecePriority</c> / <c>SetPiecePriority</c> /
/// <c>GetPiecePriorities</c> / <c>SetPiecePriorities</c>), and <c>SetFileFirstLastPiecePriority</c>.
/// Values map to libtorrent's <c>download_priority_t</c> 0-7 scale, where 0 means
/// "skip" and higher values get more frequent piece-picker attention. The binding
/// pre-selects the four commonly-used levels rather than exposing every byte
/// value — passing or receiving an out-of-range value (2/3/5/6) round-trips
/// through the underlying byte but is not named here. Default for newly-attached
/// torrents is <see cref="Normal"/>.
/// </summary>
public enum FileDownloadPriority : byte
{
    /// <summary>Skip this file/piece entirely — the piece picker won't request it from peers, and existing data on disk for it is retained but not extended. Use to deselect unwanted files in a multi-file torrent (e.g. samples, NFOs, extras).</summary>
    DoNotDownload = 0,

    /// <summary>Lowest active priority — picked only after <see cref="Normal"/> and <see cref="High"/> pieces are queued. Useful for trailing files in a sequential-download UX where higher tiers are reserved for the playback head.</summary>
    Low = 1,

    /// <summary>Default priority assigned to every file/piece on attach. Maps to libtorrent's <c>download_priority_t::default_priority</c> (4), the midpoint of the 0-7 scale.</summary>
    Normal = 4,

    /// <summary>Highest named priority — picker biases toward these pieces. Used by <c>SetFileFirstLastPiecePriority</c> for the first/last pieces of streaming-target files so playback can start before the tail finishes.</summary>
    High = 7
}
