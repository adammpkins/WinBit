// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a tracker response includes a <c>trackerid</c> (BEP 3).
/// libtorrent stores the id internally and repeats it in subsequent
/// announces for the tracker — this alert surfaces the exchange for
/// observability.
/// </summary>
public class TrackeridAlert : Alert
{
    internal TrackeridAlert(NativeEvents.TrackeridAlert alert, TorrentHandle subject)
        : base(alert.info)
    {
        Subject = subject;
        InfoHash = new Sha1Hash(alert.info_hash);

        TrackerUrl = alert.tracker_url == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.tracker_url) ?? string.Empty;

        TrackerId = alert.tracker_id == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.tracker_id) ?? string.Empty;
    }

    /// <summary>The torrent whose tracker issued the id.</summary>
    public TorrentHandle Subject { get; }

    /// <summary>The v1 info-hash of the torrent the tracker id belongs to — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>The tracker URL that issued the id.</summary>
    public string TrackerUrl { get; }

    /// <summary>The tracker id returned in the announce response.</summary>
    public string TrackerId { get; }
}
