// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a piece fails SHA hash verification during download. libtorrent
/// discards the bad data and re-requests the piece from a different peer.
/// <see cref="PieceIndex"/> identifies which piece failed — useful for
/// diagnostics and reputation tracking on peers that repeatedly send bad data.
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source
/// torrent (MagnetHandle) that received corrupted piece data after metadata
/// arrival — magnet handles aren't tracked in the session's TorrentHandle
/// map, so the dispatcher can't resolve a managed TorrentHandle to attribute
/// the hash failure to. Callers tracking magnet hash failures should use
/// <see cref="InfoHash"/> as the routing key.
/// </para>
/// </summary>
public class HashFailedAlert : Alert
{
    internal HashFailedAlert(NativeEvents.HashFailedAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        PieceIndex = alert.piece_index;
        InfoHash = new Sha1Hash(alert.info_hash);
    }

    /// <summary>The torrent the failed piece belongs to. May be null for magnet-source hash failures — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>Index of the piece that failed hash verification.</summary>
    public int PieceIndex { get; }

    /// <summary>The v1 info-hash of the torrent the failed piece belongs to — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }
}
