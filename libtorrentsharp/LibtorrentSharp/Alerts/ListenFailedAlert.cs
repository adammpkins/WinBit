using System.Net;
using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when libtorrent fails to open a listen socket. <see cref="Interface"/>
/// names the interface (or IP) libtorrent attempted to bind to;
/// <see cref="ErrorMessage"/> carries the system error text so callers can
/// surface a meaningful diagnostic without round-tripping
/// <see cref="ErrorCode"/> through their own error registry. <see cref="Operation"/>
/// identifies which step of the bind sequence failed (sock_open / sock_bind /
/// sock_listen / etc.) so consumers can distinguish "couldn't open the socket
/// at all" from "bound but couldn't listen".
/// </summary>
public class ListenFailedAlert : Alert
{
    internal ListenFailedAlert(NativeEvents.ListenFailedAlert alert)
        : base(alert.info)
    {
        var v6 = new IPAddress(alert.address);
        Address = v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6;
        Port = alert.port;
        SocketType = (SocketType)alert.socket_type;
        Operation = (OperationType)alert.op;
        ErrorCode = alert.error_code;

        // Both string fields are dispatcher-owned; the constructor copies into
        // managed memory before the callback returns.
        Interface = alert.listen_interface == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.listen_interface) ?? string.Empty;
        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>The interface (name or IP) libtorrent tried to bind to.</summary>
    public string Interface { get; }

    /// <summary>The local IP libtorrent attempted to bind to.</summary>
    public IPAddress Address { get; }

    /// <summary>The port libtorrent attempted to listen on.</summary>
    public int Port { get; }

    /// <summary>The kind of listen socket whose bind failed.</summary>
    public SocketType SocketType { get; }

    /// <summary>Which step of the bind sequence failed (open, bind, listen, etc.).</summary>
    public OperationType Operation { get; }

    /// <summary>The numeric system error code returned by the OS.</summary>
    public int ErrorCode { get; }

    /// <summary>Human-readable error text from the OS.</summary>
    public string ErrorMessage { get; }
}
