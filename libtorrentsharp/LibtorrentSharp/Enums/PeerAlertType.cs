// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
// csdl - a cross-platform libtorrent wrapper for .NET
// Licensed under Apache-2.0 - see the license file for more information

namespace LibtorrentSharp.Enums;

/// <summary>
/// Discriminator for <see cref="LibtorrentSharp.Alerts.PeerAlert.AlertType"/>,
/// mirroring libtorrent's per-peer alert family. Each value identifies which
/// underlying libtorrent alert type produced the unified <c>PeerAlert</c>.
/// </summary>
public enum PeerAlertType : byte
{
    /// <summary>An incoming peer connection was accepted on this torrent (mirror of <c>peer_connect_alert</c> with the incoming-direction flag).</summary>
    ConnectedIncoming = 0,

    /// <summary>An outgoing peer connection initiated by libtorrent succeeded (mirror of <c>peer_connect_alert</c> with the outgoing-direction flag).</summary>
    ConnectedOutgoing = 1,

    /// <summary>A peer disconnected — clean close, timeout, or transport error (mirror of <c>peer_disconnected_alert</c>).</summary>
    Disconnected = 2,

    /// <summary>A peer was banned for protocol misbehavior (e.g. repeatedly sending bad pieces; mirror of <c>peer_ban_alert</c>).</summary>
    Banned = 3,

    /// <summary>A peer was marked snubbed — libtorrent stopped requesting blocks from it because none arrived in time (mirror of <c>peer_snubbed_alert</c>).</summary>
    Snubbed = 4,

    /// <summary>A previously snubbed peer started delivering blocks again (mirror of <c>peer_unsnubbed_alert</c>).</summary>
    Unsnubbed = 5,

    /// <summary>A peer-side error occurred — generally a protocol error that doesn't warrant a ban but does sever the connection (mirror of <c>peer_error_alert</c>).</summary>
    Errored = 6
}