namespace WinBit.Core.WatchedFolders;

/// <summary>
/// Ports <c>TorrentFilesWatcher::WatchedFolderOptions</c> from
/// <c>qbittorrent/src/base/torrentfileswatcher.cpp</c>. Each folder stores a path plus the
/// policy applied to <c>.torrent</c> files found inside it.
/// </summary>
public sealed record WatchedFolder
{
    public required string Path { get; init; }

    /// <summary>Save path for torrents added from this folder. Null = use the watched folder itself.</summary>
    public string? SavePath { get; init; }

    public bool StartImmediately { get; init; } = true;

    /// <summary>When true, the watcher also picks up <c>.torrent</c> files in subdirectories.</summary>
    public bool Recursive { get; init; }

    /// <summary>When true, the source <c>.torrent</c> is deleted after a successful add.</summary>
    public bool DeleteSourceOnAdd { get; init; } = true;
}
