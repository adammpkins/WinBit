// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when libtorrent sends an announce request to a tracker — pairs with
/// <see cref="TrackerReplyAlert"/> (success) / <see cref="TrackerErrorAlert"/>
/// (failure) which report the response. <see cref="Event"/> identifies which
/// lifecycle transition triggered the announce.
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source
/// torrent (MagnetHandle) — magnet handles aren't tracked in the
/// session's TorrentHandle map, so the dispatcher can't resolve a
/// managed TorrentHandle. Callers correlating tracker announces for magnets
/// should use <see cref="InfoHash"/> as the routing key.
/// </para>
/// </summary>
public class TrackerAnnounceAlert : Alert
{
    internal TrackerAnnounceAlert(NativeEvents.TrackerAnnounceAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        Event = (AnnounceEvent)alert.@event;
        InfoHash = new Sha1Hash(alert.info_hash);

        TrackerUrl = alert.tracker_url == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.tracker_url) ?? string.Empty;
    }

    /// <summary>The torrent handle this alert fired on. May be null for magnet-source torrents — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>The lifecycle event that triggered this announce.</summary>
    public AnnounceEvent Event { get; }

    /// <summary>The v1 info-hash of the torrent the announce was for — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>Tracker URL the announce was sent to.</summary>
    public string TrackerUrl { get; }
}
