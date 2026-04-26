// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Net;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a single 16 KiB block has started arriving from a peer —
/// the request was sent and the peer began sending bytes. Pairs with
/// <see cref="BlockFinishedAlert"/> (same block done) and
/// <see cref="BlockTimeoutAlert"/> (block didn't arrive in time) to give
/// consumers an "in-flight blocks per peer" view.
/// <see cref="PieceIndex"/> + <see cref="BlockIndex"/> pinpoint the
/// block; <see cref="PeerAddress"/> identifies the peer the bytes are
/// coming from.
/// <para>
/// <b>Requires opt-in:</b> consumers must include
/// <see cref="LibtorrentSharp.Enums.AlertCategories.BlockProgress"/> in
/// <see cref="LibtorrentSessionConfig.AlertCategories"/>. The default
/// <c>RequiredAlertCategories</c> mask intentionally omits BlockProgress
/// because the alerts in that category can be high-volume on busy
/// swarms (one alert per requested block, possibly hundreds per second
/// during active downloads).
/// </para>
/// </summary>
public class BlockDownloadingAlert : Alert
{
    internal BlockDownloadingAlert(NativeEvents.BlockDownloadingAlert alert, TorrentHandle subject)
        : base(alert.info)
    {
        Subject = subject;
        BlockIndex = alert.block_index;
        PieceIndex = alert.piece_index;

        var v6 = new IPAddress(alert.v6_address);
        PeerAddress = v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6;
    }

    /// <summary>The torrent the in-flight block belongs to.</summary>
    public TorrentHandle Subject { get; }

    /// <summary>Zero-based block index within <see cref="PieceIndex"/>.</summary>
    public int BlockIndex { get; }

    /// <summary>Zero-based piece index the block belongs to.</summary>
    public int PieceIndex { get; }

    /// <summary>The peer the bytes are coming from. v4-mapped v6 is demapped to v4.</summary>
    public IPAddress PeerAddress { get; }
}
