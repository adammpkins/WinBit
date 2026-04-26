// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a tracker scrape request fails. Distinct from
/// <see cref="TrackerErrorAlert"/>, which covers announce failures — scrape
/// failures go through their own alert type. <see cref="TrackerUrl"/>
/// identifies the failing tracker; <see cref="ErrorMessage"/> carries the
/// system error text.
/// </summary>
public class ScrapeFailedAlert : Alert
{
    internal ScrapeFailedAlert(NativeEvents.ScrapeFailedAlert alert, TorrentHandle subject)
        : base(alert.info)
    {
        Subject = subject;
        ErrorCode = alert.error_code;
        InfoHash = new Sha1Hash(alert.info_hash);

        TrackerUrl = alert.tracker_url == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.tracker_url) ?? string.Empty;
        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>The torrent handle this alert fired on. Resolved by the native dispatcher from the alert's torrent association before the typed alert lands on the consumer side.</summary>
    public TorrentHandle Subject { get; }

    /// <summary>The numeric system / libtorrent error code.</summary>
    public int ErrorCode { get; }

    /// <summary>The v1 info-hash of the torrent whose scrape failed — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>Tracker URL whose scrape failed.</summary>
    public string TrackerUrl { get; }

    /// <summary>Human-readable error text.</summary>
    public string ErrorMessage { get; }
}
