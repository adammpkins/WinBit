// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Net;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when our DHT node sends an outgoing <c>get_peers</c> query to
/// another node — the active counterpart to <see cref="DhtGetPeersAlert"/>
/// (incoming queries) and the request side of <see cref="DhtReplyAlert"/>
/// (responses). Session-level.
/// </summary>
public class DhtOutgoingGetPeersAlert : Alert
{
    internal DhtOutgoingGetPeersAlert(NativeEvents.DhtOutgoingGetPeersAlert alert)
        : base(alert.info)
    {
        InfoHash = new Sha1Hash(alert.info_hash);
        ObfuscatedInfoHash = new Sha1Hash(alert.obfuscated_info_hash);

        var v6 = new IPAddress(alert.endpoint_address);
        var address = v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6;
        Endpoint = new IPEndPoint(address, alert.endpoint_port);
    }

    /// <summary>The info-hash we're querying for peers.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>
    /// The bit-masked target actually sent on the wire when obfuscated
    /// lookups are enabled; equal to <see cref="InfoHash"/> when obfuscation
    /// is off.
    /// </summary>
    public Sha1Hash ObfuscatedInfoHash { get; }

    /// <summary>The DHT node we sent the query to.</summary>
    public IPEndPoint Endpoint { get; }
}
