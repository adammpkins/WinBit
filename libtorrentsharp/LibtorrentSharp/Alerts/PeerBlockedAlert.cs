// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using System.Net;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when an incoming peer connection is filtered out before any
/// payload is exchanged — IP filter, port filter, privileged-port
/// restriction, uTP/TCP-disabled mismatch, etc. <see cref="Reason"/>
/// identifies why the peer was rejected; <see cref="PeerAddress"/>
/// identifies which peer (useful for security UIs that want to surface
/// repeat offenders or audit filter behavior).
/// <para>
/// <b>Subject may be null</b> when libtorrent's IP-filter check
/// rejects the connection at accept time — before the BitTorrent
/// handshake has identified which torrent the peer was reaching for —
/// the alert's underlying info_hash is then zero, and the dispatcher
/// can't resolve a managed handle to attribute the rejection to.
/// Callers that surface IP-block telemetry should treat null Subject
/// as "filter-time / pre-handshake rejection" and use
/// <see cref="PeerAddress"/> + <see cref="Reason"/> as the primary
/// signal.
/// </para>
/// <para>
/// <b>Requires opt-in:</b> consumers must include
/// <see cref="LibtorrentSharp.Enums.AlertCategories.IPBlock"/> in
/// <see cref="LibtorrentSessionConfig.AlertCategories"/>. The default
/// <c>RequiredAlertCategories</c> mask intentionally omits IPBlock
/// because the alert can be high-volume on public-facing seeds with
/// strict filters in place.
/// </para>
/// </summary>
public class PeerBlockedAlert : Alert
{
    internal PeerBlockedAlert(NativeEvents.PeerBlockedAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        Reason = (PeerBlockedReason)alert.reason;

        var v6 = new IPAddress(alert.v6_address);
        PeerAddress = v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6;
    }

    /// <summary>The torrent the blocked peer attempted to connect to. May be null for filter-time / pre-handshake rejections — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>Why the peer was filtered out.</summary>
    public PeerBlockedReason Reason { get; }

    /// <summary>The peer's address (v4-mapped v6 demapped to v4).</summary>
    public IPAddress PeerAddress { get; }
}
