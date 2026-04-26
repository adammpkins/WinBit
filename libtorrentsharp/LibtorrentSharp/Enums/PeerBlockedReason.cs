// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

namespace LibtorrentSharp.Enums;

/// <summary>
/// Why an incoming peer connection was filtered out, surfaced via
/// <see cref="Alerts.PeerBlockedAlert.Reason"/>. Mirrors libtorrent's
/// <c>peer_blocked_alert::reason_t</c> from <c>libtorrent/alert_types.hpp</c>
/// in declaration order, so a numeric round-trip across the C ABI maps 1:1.
/// </summary>
public enum PeerBlockedReason
{
    /// <summary>The peer's IP is blocked by the session's <c>ip_filter</c>.</summary>
    IpFilter = 0,

    /// <summary>The peer's port is blocked by the session's <c>port_filter</c>.</summary>
    PortFilter = 1,

    /// <summary>An I2P peer attempted to connect via a non-I2P transport (or vice versa).</summary>
    I2pMixed = 2,

    /// <summary>The peer's port is below 1024 and <c>no_connect_privileged_ports</c> is enabled.</summary>
    PrivilegedPorts = 3,

    /// <summary>The peer requested uTP but uTP is disabled on this session.</summary>
    UtpDisabled = 4,

    /// <summary>The peer requested TCP but TCP is disabled on this session.</summary>
    TcpDisabled = 5,

    /// <summary>The peer's connection didn't satisfy the bind-interface restriction.</summary>
    InvalidLocalInterface = 6,

    /// <summary>The peer's address triggered Server-Side Request Forgery mitigation (e.g. loopback or RFC1918 from a public-facing tracker).</summary>
    SsrfMitigation = 7,
}
