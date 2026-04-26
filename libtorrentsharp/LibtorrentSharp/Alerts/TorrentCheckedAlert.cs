// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a torrent completes its hash check — either the initial check
/// on attach or a force_recheck request. <see cref="Subject"/> is the handle
/// that was checked and <see cref="InfoHash"/> is its v1 info-hash, the same
/// identifier the native dispatcher used to route the alert.
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source
/// torrent (MagnetHandle) — magnet handles aren't tracked in the
/// session's TorrentHandle map, so the dispatcher can't resolve a
/// managed TorrentHandle to attribute the check to. Magnet torrents
/// fire torrent_checked_alert when their initial hash check completes
/// (after metadata arrival) — e.g. checking a previously-saved
/// partially-downloaded payload. Callers should use <see cref="InfoHash"/>
/// as the routing key for magnet-source completions.
/// </para>
/// </summary>
public class TorrentCheckedAlert : Alert
{
    internal TorrentCheckedAlert(NativeEvents.TorrentCheckedAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        InfoHash = new Sha1Hash(alert.info_hash);
    }

    /// <summary>The torrent that was checked. May be null for magnet-source completions — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>The v1 info-hash of the checked torrent — surfaces the same identifier the native dispatcher used to route the alert, useful for correlating multiple check completions across handles.</summary>
    public Sha1Hash InfoHash { get; }
}
