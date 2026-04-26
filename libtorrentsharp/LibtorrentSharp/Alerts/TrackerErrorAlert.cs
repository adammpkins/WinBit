// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a tracker announce or scrape fails. <see cref="TrackerUrl"/>
/// identifies the failing tracker; <see cref="ErrorMessage"/> carries the
/// system error text; <see cref="TimesInRow"/> is the consecutive-failure
/// count so callers can surface persistent failures distinctly from
/// transient ones.
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source
/// torrent (MagnetHandle) — magnet handles aren't tracked in the
/// session's TorrentHandle map, so the dispatcher can't resolve a
/// managed TorrentHandle. Callers correlating tracker errors for magnets
/// should use <see cref="InfoHash"/> as the routing key.
/// </para>
/// </summary>
public class TrackerErrorAlert : Alert
{
    internal TrackerErrorAlert(NativeEvents.TrackerErrorAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        ErrorCode = alert.error_code;
        TimesInRow = alert.times_in_row;
        InfoHash = new Sha1Hash(alert.info_hash);

        TrackerUrl = alert.tracker_url == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.tracker_url) ?? string.Empty;
        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>The torrent handle this alert fired on. May be null for magnet-source torrents — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>The numeric system / libtorrent error code.</summary>
    public int ErrorCode { get; }

    /// <summary>Consecutive-failure count for this tracker.</summary>
    public int TimesInRow { get; }

    /// <summary>The v1 info-hash of the torrent the failing tracker belongs to — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>Tracker URL whose announce / scrape failed.</summary>
    public string TrackerUrl { get; }

    /// <summary>Human-readable error text.</summary>
    public string ErrorMessage { get; }
}
