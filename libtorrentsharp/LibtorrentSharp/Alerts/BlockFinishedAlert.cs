// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Net;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a single 16 KiB block (a sub-piece request unit) finishes
/// downloading from a specific peer. <see cref="PieceIndex"/> +
/// <see cref="BlockIndex"/> together pinpoint the block within the
/// torrent; <see cref="PeerAddress"/> identifies the peer the block
/// came from.
/// <para>
/// <b>Requires opt-in:</b> consumers must include
/// <see cref="LibtorrentSharp.Enums.AlertCategories.BlockProgress"/> in
/// <see cref="LibtorrentSessionConfig.AlertCategories"/>. The default
/// <c>RequiredAlertCategories</c> mask intentionally omits BlockProgress
/// because the alert fires once per block during active downloads —
/// dozens or hundreds per second on real swarms — and would flood the
/// alert pipeline. Useful for fine-grained progress UIs and per-peer
/// throughput accounting.
/// </para>
/// </summary>
public class BlockFinishedAlert : Alert
{
    internal BlockFinishedAlert(NativeEvents.BlockFinishedAlert alert, TorrentHandle subject)
        : base(alert.info)
    {
        Subject = subject;
        BlockIndex = alert.block_index;
        PieceIndex = alert.piece_index;

        var v6 = new IPAddress(alert.v6_address);
        PeerAddress = v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6;
    }

    /// <summary>The torrent the completed block belongs to.</summary>
    public TorrentHandle Subject { get; }

    /// <summary>Zero-based block index within <see cref="PieceIndex"/>.</summary>
    public int BlockIndex { get; }

    /// <summary>Zero-based piece index the block belongs to.</summary>
    public int PieceIndex { get; }

    /// <summary>The peer address the block was received from. v4-mapped v6 is demapped to v4.</summary>
    public IPAddress PeerAddress { get; }
}
