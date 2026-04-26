// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a torrent reaches 100% progress. <see cref="Subject"/> is the
/// handle that finished and <see cref="InfoHash"/> is its v1 info-hash.
/// Fires once per download completion; a subsequent re-add of already-
/// complete data also triggers it during the initial hash check.
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source
/// torrent (MagnetHandle) that has finished downloading after metadata
/// arrival. Magnet handles aren't tracked in the session's TorrentHandle
/// map, so the dispatcher can't resolve a managed TorrentHandle to
/// attribute the completion to. Callers tracking magnet downloads
/// should use <see cref="InfoHash"/> as the routing key and treat
/// null Subject as "magnet-source — correlate by InfoHash".
/// </para>
/// </summary>
public class TorrentFinishedAlert : Alert
{
    internal TorrentFinishedAlert(NativeEvents.TorrentFinishedAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        InfoHash = new Sha1Hash(alert.info_hash);
    }

    /// <summary>The torrent that finished. May be null for magnet-source completions — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>The v1 info-hash of the finished torrent — surfaces the same identifier the native dispatcher used to route the alert, useful for callers tracking completion across handles.</summary>
    public Sha1Hash InfoHash { get; }
}
