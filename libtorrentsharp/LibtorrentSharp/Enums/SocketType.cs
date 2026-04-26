namespace LibtorrentSharp.Enums;

/// <summary>
/// Mirrors libtorrent's <c>socket_type_t</c>. Identifies the kind of socket
/// involved in a listen operation or peer connection. Surfaced through
/// <see cref="Alerts.ListenSucceededAlert"/>, <see cref="Alerts.ListenFailedAlert"/>,
/// and <see cref="Alerts.IncomingConnectionAlert"/>. The four base transports
/// (TCP, uTP, SOCKS5, HTTP) each have a TLS-wrapped sibling
/// (<see cref="TcpSsl"/>, <see cref="UtpSsl"/>, <see cref="Socks5Ssl"/>, <see cref="HttpSsl"/>);
/// I2P is anonymous-overlay-only and has no SSL variant.
/// </summary>
public enum SocketType : byte
{
    /// <summary>Plain TCP — the original BitTorrent peer transport (BEP 3). Listened on whichever port the session was configured to bind.</summary>
    Tcp = 0,

    /// <summary>SOCKS5 proxy tunnel — outbound peer connections are dialed through a configured SOCKS5 proxy. Surfaced when the session is configured with <c>proxy_type::socks5</c>.</summary>
    Socks5 = 1,

    /// <summary>HTTP CONNECT proxy tunnel — outbound peer connections are tunneled through a configured HTTP proxy. Surfaced when the session is configured with <c>proxy_type::http</c>.</summary>
    Http = 2,

    /// <summary>uTorrent Transport Protocol (BEP 29): a UDP-based, LEDBAT-congestion-controlled peer transport. Co-listens on the same UDP port as DHT/LSD; preferred over TCP when both peers support it.</summary>
    Utp = 3,

    /// <summary>I2P anonymous-overlay transport — peer connections routed through an I2P SAM/BOB bridge. No TLS variant (the I2P tunnel itself supplies the encryption layer).</summary>
    I2p = 4,

    /// <summary>TLS-wrapped TCP — used for peers that negotiate the <c>uTP/TCP-encrypted</c> protocol extension. Distinct listen socket from plain <see cref="Tcp"/>.</summary>
    TcpSsl = 5,

    /// <summary>TLS-wrapped SOCKS5 tunnel — outbound peer connections via a SOCKS5 proxy with the proxy hop itself TLS-protected.</summary>
    Socks5Ssl = 6,

    /// <summary>TLS-wrapped HTTP CONNECT tunnel — outbound peer connections via an HTTP proxy with the proxy hop itself TLS-protected (HTTPS proxy).</summary>
    HttpSsl = 7,

    /// <summary>TLS-wrapped uTP — TLS over the UDP-based uTP transport. Distinct listen socket from plain <see cref="Utp"/>.</summary>
    UtpSsl = 8
}
