using System.Net;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when libtorrent successfully opens a listen socket on an interface.
/// Pair with <see cref="LibtorrentSession.IsListening"/> /
/// <see cref="LibtorrentSession.ListenPort"/> for the steady-state view; this
/// alert is the event-driven notification that a bind just succeeded.
/// </summary>
public class ListenSucceededAlert : Alert
{
    internal ListenSucceededAlert(NativeEvents.ListenSucceededAlert alert)
        : base(alert.info)
    {
        // Native side delivers v4-mapped v6; demap to v4 when applicable so
        // callers see the interface address in its natural family.
        var v6 = new IPAddress(alert.address);
        Address = v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6;
        Port = alert.port;
        SocketType = (SocketType)alert.socket_type;
    }

    /// <summary>The local IP libtorrent successfully bound to.</summary>
    public IPAddress Address { get; }

    /// <summary>The port libtorrent ended up listening on.</summary>
    public int Port { get; }

    /// <summary>The kind of listen socket this bind opened.</summary>
    public SocketType SocketType { get; }
}
