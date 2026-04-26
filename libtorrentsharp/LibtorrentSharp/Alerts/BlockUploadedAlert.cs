// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using System.Net;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Symmetric to <see cref="BlockFinishedAlert"/> but for the upload
/// direction — fires when a single 16 KiB block has been written out
/// to a specific peer. <see cref="PieceIndex"/> + <see cref="BlockIndex"/>
/// pinpoint the block within the torrent; <see cref="PeerAddress"/>
/// identifies the peer that received it.
/// <para>
/// <b>Requires opt-in:</b> consumers must include
/// <see cref="LibtorrentSharp.Enums.AlertCategories.Upload"/> in
/// <see cref="LibtorrentSessionConfig.AlertCategories"/>. The default
/// <c>RequiredAlertCategories</c> mask intentionally omits Upload
/// because the alert fires once per block uploaded — chatty for active
/// seeds (dozens or hundreds per second on a popular torrent). Useful
/// for per-peer upload accounting and seeding-throughput UIs.
/// </para>
/// </summary>
public class BlockUploadedAlert : Alert
{
    internal BlockUploadedAlert(NativeEvents.BlockUploadedAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        BlockIndex = alert.block_index;
        PieceIndex = alert.piece_index;

        var v6 = new IPAddress(alert.v6_address);
        PeerAddress = v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6;
    }

    /// <summary>The torrent the uploaded block belongs to. May be null for magnet-source torrents — magnet handles aren't tracked in the session's TorrentHandle map.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>Zero-based block index within <see cref="PieceIndex"/>.</summary>
    public int BlockIndex { get; }

    /// <summary>Zero-based piece index the block belongs to.</summary>
    public int PieceIndex { get; }

    /// <summary>The peer address the block was uploaded to. v4-mapped v6 is demapped to v4.</summary>
    public IPAddress PeerAddress { get; }
}
