// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Net;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when another peer announces itself to our DHT node for an
/// info-hash. Session-level (the info-hash does not have to match one
/// of our own torrents — this surfaces incoming DHT traffic where our
/// node is acting as a lookup peer for the swarm).
/// </summary>
public class DhtAnnounceAlert : Alert
{
    internal DhtAnnounceAlert(NativeEvents.DhtAnnounceAlert alert)
        : base(alert.info)
    {
        var v6 = new IPAddress(alert.ip_address);
        IpAddress = v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6;
        Port = alert.port;
        InfoHash = new Sha1Hash(alert.info_hash);
    }

    /// <summary>The remote peer's IP address.</summary>
    public IPAddress IpAddress { get; }

    /// <summary>The port the remote peer is listening on.</summary>
    public int Port { get; }

    /// <summary>The info-hash the remote peer announced interest in.</summary>
    public Sha1Hash InfoHash { get; }
}
