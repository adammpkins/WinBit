using System.Net;
using LibtorrentSharp.Enums;

namespace LibtorrentSharp;

/// <summary>
/// Snapshot of a single peer connected to a torrent, returned from
/// <see cref="TorrentHandle.GetPeers"/> / <see cref="MagnetHandle.GetPeers"/>.
/// </summary>
/// <param name="Address">Peer's IP. V4 peers are canonicalized from libtorrent's v4-mapped form.</param>
/// <param name="Port">Peer's TCP port.</param>
/// <param name="Client">Reported client string (user-agent-like). Empty when unknown.</param>
/// <param name="Flags">Raw libtorrent <c>peer_info::flags_t</c> bitmask. Typed enum TBD.</param>
/// <param name="Source">Bitmask of how this peer was discovered (slice 132 — typed mirror of libtorrent's peer_info::peer_source_flags).</param>
/// <param name="Progress">Peer's download progress, 0.0 to 1.0.</param>
/// <param name="UploadRate">Bytes per second we're uploading to this peer.</param>
/// <param name="DownloadRate">Bytes per second this peer is sending us.</param>
/// <param name="TotalUploaded">Total bytes uploaded to this peer this session.</param>
/// <param name="TotalDownloaded">Total bytes received from this peer this session.</param>
public record PeerInfo(
    IPAddress Address,
    int Port,
    string Client,
    uint Flags,
    PeerSource Source,
    float Progress,
    int UploadRate,
    int DownloadRate,
    long TotalUploaded,
    long TotalDownloaded);
