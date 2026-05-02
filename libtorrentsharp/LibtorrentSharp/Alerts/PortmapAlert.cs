// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Net;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when the router adds or updates a port mapping in response to a
/// NAT-PMP or UPnP request. Session-level (no torrent association) —
/// complements <see cref="LibtorrentSession.GetPortMappings"/> by giving
/// callers an event-driven view of mapping activity.
/// </summary>
public class PortmapAlert : Alert
{
    internal PortmapAlert(NativeEvents.PortmapAlert alert)
        : base(alert.info)
    {
        Mapping = alert.mapping;
        ExternalPort = alert.external_port;
        Protocol = (PortMappingProtocol)alert.map_protocol;
        Transport = (PortMappingTransport)alert.map_transport;

        var v6 = new IPAddress(alert.local_address);
        LocalAddress = v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6;
    }

    /// <summary>libtorrent''s stable per-mapping identifier.</summary>
    public int Mapping { get; }

    /// <summary>External port the router assigned to the mapping.</summary>
    public int ExternalPort { get; }

    /// <summary>Transport-layer protocol (TCP or UDP).</summary>
    public PortMappingProtocol Protocol { get; }

    /// <summary>Router protocol used to establish the mapping (NAT-PMP or UPnP).</summary>
    public PortMappingTransport Transport { get; }

    /// <summary>The local interface the mapping is bound to.</summary>
    public IPAddress LocalAddress { get; }
}