// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Net;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when libtorrent learns the machine's external IP address — discovered
/// via tracker responses, peer extension handshakes (BEP 10), or other
/// reporting channels. Session-level (no torrent association). Callers can
/// surface this as a diagnostic signal or use it for NAT / UPnP troubleshooting.
/// </summary>
public class ExternalIpAlert : Alert
{
    internal ExternalIpAlert(NativeEvents.ExternalIpAlert alert)
        : base(alert.info)
    {
        var v6 = new IPAddress(alert.external_address);
        ExternalAddress = v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6;
    }

    /// <summary>The external IP libtorrent observed for this machine.</summary>
    public IPAddress ExternalAddress { get; }
}
