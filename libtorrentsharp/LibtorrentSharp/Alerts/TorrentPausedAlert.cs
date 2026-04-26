// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a torrent transitions from active to paused in response to a
/// pause request. <see cref="Subject"/> is the handle that was paused, and
/// <see cref="InfoHash"/> is its v1 info-hash — the same identifier the
/// native dispatcher used to route the alert.
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source
/// torrent (MagnetHandle) — magnet handles aren't tracked in the
/// session's TorrentHandle map, so the dispatcher can't resolve a
/// managed TorrentHandle to attribute the pause to. Calling
/// <see cref="MagnetHandle.Pause"/> on a magnet still fires this alert
/// even before metadata arrival; callers tracking magnet pause/resume
/// state should use <see cref="InfoHash"/> as the routing key.
/// </para>
/// </summary>
public class TorrentPausedAlert : Alert
{
    internal TorrentPausedAlert(NativeEvents.TorrentPausedAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        InfoHash = new Sha1Hash(alert.info_hash);
    }

    /// <summary>The torrent that was paused. May be null for magnet-source pauses — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>The v1 info-hash of the paused torrent — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }
}
