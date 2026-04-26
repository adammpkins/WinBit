using System;

namespace LibtorrentSharp.Enums;

/// <summary>
/// Per-file flag bits from libtorrent's <c>file_storage::file_flags_t</c>.
/// Mirrors the bit assignment in libtorrent 2.x: pad_file=bit 0, hidden=bit 1,
/// executable=bit 2, symlink=bit 3.
/// </summary>
[Flags]
public enum FileFlags : byte
{
    /// <summary>No flags set.</summary>
    None = 0,

    /// <summary>
    /// File is a padding file: synthetic zero-filled content used to align
    /// real file boundaries onto piece boundaries. libtorrent doesn't download
    /// or write these.
    /// </summary>
    PadFile = 1 << 0,

    /// <summary>
    /// File should be treated as hidden by filesystem APIs that honor that bit.
    /// </summary>
    Hidden = 1 << 1,

    /// <summary>
    /// File is marked executable (sets +x on Unix-family filesystems).
    /// </summary>
    Executable = 1 << 2,

    /// <summary>
    /// File is a symbolic link; the link target is stored in the torrent's
    /// symlink dict and surfaces via a future <c>Symlink(fileIndex)</c> accessor.
    /// </summary>
    Symlink = 1 << 3,
}
