using System.Net;
using LibtorrentSharp.Enums;

namespace LibtorrentSharp;

/// <summary>
/// Snapshot of a single peer connected to a torrent, returned from
/// <see cref="TorrentHandle.GetPeers"/> / <see cref="MagnetHandle.GetPeers"/>.
/// </summary>
public record PeerInfo(
    IPAddress Address,
    int Port,
    string Client,
    PeerFlags Flags,
    PeerSource Source,
    float Progress,
    int UploadRate,
    int DownloadRate,
    long TotalUploaded,
    long TotalDownloaded,
    PeerConnectionType ConnectionType,
    int NumHashFails,
    int DownloadingPieceIndex,
    int DownloadingBlockIndex,
    int DownloadingProgress,
    int DownloadingTotal,
    int FailCount,
    int PayloadUploadRate,
    int PayloadDownloadRate,
    byte[] PeerId);
