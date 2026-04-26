// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Net;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a peer sent us a 16 KiB block we never requested —
/// typically a duplicate (a different peer already delivered the same
/// block before this one arrived) or the sign of a buggy / hostile
/// peer. <see cref="PieceIndex"/> + <see cref="BlockIndex"/> identify
/// the block; <see cref="PeerAddress"/> identifies the peer that sent
/// it. Same field shape as the rest of the Block* alert family.
/// <para>
/// Useful for "noisy peer" detection in a Peers tab — repeated
/// <c>UnwantedBlockAlert</c> from the same peer is a strong signal
/// that peer is misbehaving.
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
public class UnwantedBlockAlert : Alert
{
    internal UnwantedBlockAlert(NativeEvents.UnwantedBlockAlert alert, TorrentHandle subject)
        : base(alert.info)
    {
        Subject = subject;
        BlockIndex = alert.block_index;
        PieceIndex = alert.piece_index;

        var v6 = new IPAddress(alert.v6_address);
        PeerAddress = v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6;
    }

    /// <summary>The torrent the unwanted block belongs to.</summary>
    public TorrentHandle Subject { get; }

    /// <summary>Zero-based block index within <see cref="PieceIndex"/>.</summary>
    public int BlockIndex { get; }

    /// <summary>Zero-based piece index the block belongs to.</summary>
    public int PieceIndex { get; }

    /// <summary>The peer that sent the unwanted block. v4-mapped v6 is demapped to v4.</summary>
    public IPAddress PeerAddress { get; }
}
