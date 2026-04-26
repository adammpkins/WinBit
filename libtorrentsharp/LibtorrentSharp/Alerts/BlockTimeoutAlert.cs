// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Net;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a previously-issued block request did not get filled
/// before its deadline — the remote peer was supposed to send a 16 KiB
/// sub-piece and didn't. <see cref="PieceIndex"/> + <see cref="BlockIndex"/>
/// pinpoint the request; <see cref="PeerAddress"/> identifies the peer
/// the request was sent to. Same field shape as <see cref="BlockFinishedAlert"/>
/// and <see cref="BlockUploadedAlert"/>.
/// <para>
/// Repeated <c>BlockTimeoutAlert</c> from the same peer typically means
/// that peer is dead or congested and libtorrent will eventually drop
/// them — useful for "stalled download" diagnostics in a Peers tab.
/// </para>
/// <para>
/// <b>Requires opt-in:</b> consumers must include
/// <see cref="LibtorrentSharp.Enums.AlertCategories.BlockProgress"/> in
/// <see cref="LibtorrentSessionConfig.AlertCategories"/>. The default
/// <c>RequiredAlertCategories</c> mask intentionally omits BlockProgress
/// because the alerts in that category can be high-volume on busy
/// swarms.
/// </para>
/// </summary>
public class BlockTimeoutAlert : Alert
{
    internal BlockTimeoutAlert(NativeEvents.BlockTimeoutAlert alert, TorrentHandle subject)
        : base(alert.info)
    {
        Subject = subject;
        BlockIndex = alert.block_index;
        PieceIndex = alert.piece_index;

        var v6 = new IPAddress(alert.v6_address);
        PeerAddress = v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6;
    }

    /// <summary>The torrent the timed-out block request belongs to.</summary>
    public TorrentHandle Subject { get; }

    /// <summary>Zero-based block index within <see cref="PieceIndex"/>.</summary>
    public int BlockIndex { get; }

    /// <summary>Zero-based piece index the block belongs to.</summary>
    public int PieceIndex { get; }

    /// <summary>The peer the request was sent to. v4-mapped v6 is demapped to v4.</summary>
    public IPAddress PeerAddress { get; }
}
