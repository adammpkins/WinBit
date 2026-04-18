namespace WinBit.Core.BitTorrent;

/// <summary>
/// Snapshot of a single connected peer, rendered on the torrent Properties → Peers tab.
/// Captured per-tab at 3 s only while the tab is visible.
/// </summary>
public sealed record PeerInfo
{
    /// <summary>Endpoint in <c>ip:port</c> form.</summary>
    public required string Address { get; init; }

    /// <summary>Detected client name + version (e.g. "qBittorrent 4.6.0").</summary>
    public string? Client { get; init; }

    /// <summary>Peer's completion ratio in the range 0..1.</summary>
    public double Progress { get; init; }

    public long DownloadSpeedBps { get; init; }

    public long UploadSpeedBps { get; init; }

    public bool IsSeeder { get; init; }

    public bool IsEncrypted { get; init; }
}
