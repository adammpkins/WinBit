// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Net;
using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a NAT-PMP or UPnP port-mapping request fails. Session-level
/// (no torrent association). <see cref="LocalAddress"/> identifies the
/// interface whose mapping attempt failed; callers can pair this alert
/// with <see cref="PortmapAlert"/> to drive a live port-forwarding
/// diagnostics view.
/// </summary>
public class PortmapErrorAlert : Alert
{
    internal PortmapErrorAlert(NativeEvents.PortmapErrorAlert alert)
        : base(alert.info)
    {
        Mapping = alert.mapping;
        Transport = (PortMappingTransport)alert.map_transport;

        var v6 = new IPAddress(alert.local_address);
        LocalAddress = v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6;

        ErrorCode = alert.error_code;
        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>libtorrent''s stable per-mapping identifier.</summary>
    public int Mapping { get; }

    /// <summary>Router protocol used (NAT-PMP or UPnP).</summary>
    public PortMappingTransport Transport { get; }

    /// <summary>The local interface the mapping attempt was bound to.</summary>
    public IPAddress LocalAddress { get; }

    /// <summary>The numeric system / libtorrent error code.</summary>
    public int ErrorCode { get; }

    /// <summary>Human-readable error text.</summary>
    public string ErrorMessage { get; }
}