// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Net;
using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a SOCKS5 proxy operation fails — handshake rejection,
/// authentication failure, network unreachable through the proxy, etc.
/// Session-level (no torrent association — the proxy itself is the
/// failing resource). <see cref="Endpoint"/> is the SOCKS5 proxy we
/// tried to talk to; <see cref="Operation"/> identifies which step in
/// the SOCKS5 conversation failed (typically <see cref="OperationType.Connect"/>
/// or one of the socket-level operations).
/// <para>
/// Useful for proxy-using consumers diagnosing why outbound peer or
/// tracker connections aren't getting through — the proxy is silently
/// rejecting them.
/// </para>
/// </summary>
public class Socks5Alert : Alert
{
    internal Socks5Alert(NativeEvents.Socks5Alert alert)
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

    /// <summary>The SOCKS5 proxy endpoint we tried to talk to.</summary>
    public IPEndPoint Endpoint { get; }

    /// <summary>The SOCKS5 operation that failed.</summary>
    public OperationType Operation { get; }

    /// <summary>The numeric system / libtorrent error code.</summary>
    public int ErrorCode { get; }

    /// <summary>Human-readable error text.</summary>
    public string ErrorMessage { get; }
}
