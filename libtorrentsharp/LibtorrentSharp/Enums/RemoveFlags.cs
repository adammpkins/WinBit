// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System;

namespace LibtorrentSharp.Enums;

/// <summary>
/// Mirrors libtorrent's <c>remove_flags_t</c>. Passed to
/// <see cref="LibtorrentSession.DetachTorrent(TorrentHandle, RemoveFlags)"/>
/// to control what happens to the torrent's on-disk data when it's removed.
/// </summary>
[Flags]
public enum RemoveFlags
{
    /// <summary>No on-disk side effects — metadata is detached, files are left in place.</summary>
    None = 0,

    /// <summary>Delete the torrent's completed data files after detaching.</summary>
    DeleteFiles = 1,

    /// <summary>Delete the torrent's .parts file (libtorrent's partial-piece cache) after detaching.</summary>
    DeletePartfile = 2
}
