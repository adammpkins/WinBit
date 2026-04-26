// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a tracker successfully replies to an announce.
/// <see cref="NumPeers"/> is the number of peers the tracker returned;
/// <see cref="TrackerUrl"/> identifies which tracker the reply came from
/// (a torrent may announce to several).
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source
/// torrent (MagnetHandle) — magnet handles aren't tracked in the
/// session's TorrentHandle map, so the dispatcher can't resolve a
/// managed TorrentHandle. Callers correlating tracker replies for magnets
/// should use <see cref="InfoHash"/> as the routing key.
/// </para>
/// </summary>
public class TrackerReplyAlert : Alert
{
    internal TrackerReplyAlert(NativeEvents.TrackerReplyAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        NumPeers = alert.num_peers;
        InfoHash = new Sha1Hash(alert.info_hash);

        TrackerUrl = alert.tracker_url == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.tracker_url) ?? string.Empty;
    }

    /// <summary>The torrent handle this alert fired on. May be null for magnet-source torrents — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>Peers the tracker returned in its reply.</summary>
    public int NumPeers { get; }

    /// <summary>The v1 info-hash of the torrent the tracker replied for — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>Tracker URL this announce targeted.</summary>
    public string TrackerUrl { get; }
}
