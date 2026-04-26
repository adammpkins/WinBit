// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a torrent transitions from paused to active in response to a
/// resume request. <see cref="Subject"/> is the handle that was resumed, and
/// <see cref="InfoHash"/> is its v1 info-hash — the same identifier the
/// native dispatcher used to route the alert.
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source
/// torrent (MagnetHandle) — magnet handles aren't tracked in the
/// session's TorrentHandle map, so the dispatcher can't resolve a
/// managed TorrentHandle to attribute the resume to. Calling
/// <see cref="MagnetHandle.Resume"/> on a magnet (which is added in
/// paused state by default) still fires this alert; callers tracking
/// magnet pause/resume state should use <see cref="InfoHash"/> as the
/// routing key.
/// </para>
/// </summary>
public class TorrentResumedAlert : Alert
{
    internal TorrentResumedAlert(NativeEvents.TorrentResumedAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        InfoHash = new Sha1Hash(alert.info_hash);
    }

    /// <summary>The torrent that was resumed. May be null for magnet-source resumes — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>The v1 info-hash of the resumed torrent — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }
}
