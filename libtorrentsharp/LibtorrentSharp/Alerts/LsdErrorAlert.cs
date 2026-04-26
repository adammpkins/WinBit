// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Net;
using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when Local Service Discovery (LSD) fails on a specific local
/// interface — usually indicates the multicast socket couldn't be bound
/// or the LAN discovery query failed. <see cref="LocalAddress"/> identifies
/// which interface the failure occurred on. Session-level (no torrent
/// association).
/// </summary>
public class LsdErrorAlert : Alert
{
    internal LsdErrorAlert(NativeEvents.LsdErrorAlert alert)
        : base(alert.info)
    {
        var v6 = new IPAddress(alert.local_address);
        LocalAddress = v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6;
        ErrorCode = alert.error_code;

        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>The local interface the LSD failure occurred on.</summary>
    public IPAddress LocalAddress { get; }

    /// <summary>The numeric system / libtorrent error code.</summary>
    public int ErrorCode { get; }

    /// <summary>Human-readable error text.</summary>
    public string ErrorMessage { get; }
}
