// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a tracker scrape request completes successfully. Only fires in
/// response to an explicit <see cref="TorrentHandle.ScrapeTracker"/> call —
/// scrape counts embedded in announce replies don't trigger this alert.
/// <see cref="Complete"/> is the reported seed count; <see cref="Incomplete"/>
/// is the reported leecher count; <see cref="TrackerUrl"/> identifies which
/// tracker the reply came from (a torrent may scrape several).
/// </summary>
public class ScrapeReplyAlert : Alert
{
    internal ScrapeReplyAlert(NativeEvents.ScrapeReplyAlert alert, TorrentHandle subject)
        : base(alert.info)
    {
        Subject = subject;
        Incomplete = alert.incomplete;
        Complete = alert.complete;
        InfoHash = new Sha1Hash(alert.info_hash);

        TrackerUrl = alert.tracker_url == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.tracker_url) ?? string.Empty;
    }

    /// <summary>The torrent handle this alert fired on. Resolved by the native dispatcher from the alert's torrent association before the typed alert lands on the consumer side.</summary>
    public TorrentHandle Subject { get; }

    /// <summary>Leechers reported by the tracker.</summary>
    public int Incomplete { get; }

    /// <summary>Seeders reported by the tracker.</summary>
    public int Complete { get; }

    /// <summary>The v1 info-hash of the scraped torrent — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>Tracker URL this scrape targeted.</summary>
    public string TrackerUrl { get; }
}
