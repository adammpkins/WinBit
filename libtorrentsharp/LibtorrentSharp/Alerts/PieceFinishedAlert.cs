// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a piece finishes downloading and passes the hash check.
/// Note that "finished" here means the bytes are received and verified —
/// the piece may not yet be flushed to disk, so
/// <c>torrent_handle::have_piece()</c> can still return false briefly
/// after this alert. Useful for streaming UIs that want per-piece
/// availability tracking.
/// <para>
/// <b>Requires opt-in:</b> consumers must include
/// <see cref="LibtorrentSharp.Enums.AlertCategories.PieceProgress"/> in
/// <see cref="LibtorrentSessionConfig.AlertCategories"/>. The default
/// <c>RequiredAlertCategories</c> mask intentionally omits PieceProgress
/// because the alert fires once per piece during active downloads —
/// chatty for big torrents and only needed when callers actually want
/// piece-level granularity.
/// </para>
/// </summary>
public class PieceFinishedAlert : Alert
{
    internal PieceFinishedAlert(NativeEvents.PieceFinishedAlert alert, TorrentHandle subject)
        : base(alert.info)
    {
        Subject = subject;
        PieceIndex = alert.piece_index;
        InfoHash = new Sha1Hash(alert.info_hash);
    }

    /// <summary>The torrent whose piece completed.</summary>
    public TorrentHandle Subject { get; }

    /// <summary>Zero-based index of the piece that finished.</summary>
    public int PieceIndex { get; }

    /// <summary>The v1 info-hash of the torrent the completed piece belongs to — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }
}
