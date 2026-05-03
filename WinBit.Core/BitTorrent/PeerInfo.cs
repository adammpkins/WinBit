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

    /// <summary>True when the peer has all pieces (seeder). Shown in the Progress/State column, not as a flag character.</summary>
    public bool IsSeeder { get; init; }

    /// <summary>True when full RC4 stream encryption is active.</summary>
    public bool IsEncrypted { get; init; }

    /// <summary>True when only the MSE handshake is encrypted (plaintext body); shows lowercase 'e' in flags.</summary>
    public bool IsHandshakeEncrypted { get; init; }

    /// <summary>We are interested in at least one piece this peer has.</summary>
    public bool IsInteresting { get; init; }

    /// <summary>We have choked this peer; it cannot request pieces from us.</summary>
    public bool IsChoked { get; init; }

    /// <summary>This peer is interested in at least one piece we have.</summary>
    public bool IsRemoteInteresting { get; init; }

    /// <summary>This peer has choked us; we cannot request pieces from it.</summary>
    public bool IsRemoteChoked { get; init; }

    /// <summary>This peer was selected for an optimistic unchoke slot.</summary>
    public bool IsOptimisticUnchoke { get; init; }

    /// <summary>This peer has been snubbed due to insufficient upload rate.</summary>
    public bool IsSnubbed { get; init; }

    /// <summary>True when the peer connected to us (incoming); false when we initiated.</summary>
    public bool IsIncomingConnection { get; init; }

    /// <summary>Peer was discovered via DHT.</summary>
    public bool IsFromDht { get; init; }

    /// <summary>Peer was discovered via Peer Exchange.</summary>
    public bool IsFromPex { get; init; }

    /// <summary>Peer was discovered via Local Service Discovery.</summary>
    public bool IsFromLsd { get; init; }

    /// <summary>Connection is over uTP.</summary>
    public bool IsUtp { get; init; }

    /// <summary>Connection was established via NAT hole punching.</summary>
    public bool IsHolepunched { get; init; }
}
