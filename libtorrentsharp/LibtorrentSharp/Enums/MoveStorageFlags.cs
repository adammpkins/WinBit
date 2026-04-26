namespace LibtorrentSharp.Enums;

/// <summary>
/// Collision-handling strategy when relocating a torrent's data via
/// <see cref="TorrentHandle.MoveStorage"/>. Mirrors libtorrent's
/// <c>move_flags_t</c>.
/// </summary>
public enum MoveStorageFlags : byte
{
    /// <summary>Overwrite any file that already exists at the destination.</summary>
    AlwaysReplaceFiles = 0,

    /// <summary>Abort the move if any destination file already exists.</summary>
    FailIfExists = 1,

    /// <summary>Skip files that already exist at the destination, move the rest.</summary>
    DontReplace = 2,

    /// <summary>Update the save path without moving any data. Deprecated in libtorrent.</summary>
    ResetSavePath = 3
}
