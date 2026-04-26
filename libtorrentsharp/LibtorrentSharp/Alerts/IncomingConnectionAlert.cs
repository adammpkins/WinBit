// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Net;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when libtorrent's listen socket accepts an incoming peer connection.
/// Distinct from <see cref="PeerAlert"/> with <see cref="PeerAlertType.ConnectedIncoming"/>:
/// PeerAlert fires per-torrent <i>after</i> the connection has been associated
/// with a specific torrent, while this alert fires at the session level the
/// moment the listen socket accepts the connection (before any handshake).
/// Useful for surfacing inbound connection attempts in session-level
/// diagnostics independent of which torrent ultimately receives them.
/// <para>
/// <b>Requires opt-in:</b> consumers must include
/// <see cref="LibtorrentSharp.Enums.AlertCategories.Peer"/> in
/// <see cref="LibtorrentSessionConfig.AlertCategories"/>. The default
/// <c>RequiredAlertCategories</c> mask intentionally omits Peer because
/// the alerts in that category can be high-volume on busy seeds.
/// </para>
/// </summary>
public class IncomingConnectionAlert : Alert
{
    internal IncomingConnectionAlert(NativeEvents.IncomingConnectionAlert alert)
        : base(alert.info)
    {
        var v6 = new IPAddress(alert.endpoint_address);
        Endpoint = new IPEndPoint(
            v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6,
            alert.endpoint_port);
        SocketType = (SocketType)alert.socket_type;
    }

    /// <summary>The remote endpoint the connection came from.</summary>
    public IPEndPoint Endpoint { get; }

    /// <summary>The kind of socket the connection was accepted on (TCP, uTP, etc.).</summary>
    public SocketType SocketType { get; }
}
