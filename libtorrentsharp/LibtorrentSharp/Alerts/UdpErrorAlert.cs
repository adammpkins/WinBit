// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Net;
using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a UDP socket operation fails — DHT, UPnP discovery, UDP
/// trackers, uTP. Session-level (no torrent association). <see cref="Endpoint"/>
/// is the peer/remote address that triggered the failure (may be
/// <c>0.0.0.0:0</c> if not peer-specific); <see cref="Operation"/> identifies
/// which UDP step failed (typed <see cref="OperationType"/>).
/// </summary>
public class UdpErrorAlert : Alert
{
    internal UdpErrorAlert(NativeEvents.UdpErrorAlert alert)
        : base(alert.info)
    {
        var v6 = new IPAddress(alert.endpoint_address);
        Endpoint = new IPEndPoint(
            v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6,
            alert.endpoint_port);
        Operation = (OperationType)alert.operation;
        ErrorCode = alert.error_code;

        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>Remote endpoint that triggered the UDP error.</summary>
    public IPEndPoint Endpoint { get; }

    /// <summary>The UDP operation that failed.</summary>
    public OperationType Operation { get; }

    /// <summary>The numeric system / libtorrent error code.</summary>
    public int ErrorCode { get; }

    /// <summary>Human-readable error text.</summary>
    public string ErrorMessage { get; }
}
