// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a tracker attaches an advisory warning message to its reply.
/// Distinct from <see cref="TrackerErrorAlert"/>: the announce still
/// succeeded, the warning is informational. Surfacing it lets UX flag
/// configuration issues (e.g. &quot;unregistered torrent&quot;) that wouldn't
/// otherwise be visible to the user.
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source
/// torrent (MagnetHandle) — magnet handles aren't tracked in the
/// session's TorrentHandle map, so the dispatcher can't resolve a
/// managed TorrentHandle. Callers correlating tracker warnings for magnets
/// should use <see cref="InfoHash"/> as the routing key.
/// </para>
/// </summary>
public class TrackerWarningAlert : Alert
{
    internal TrackerWarningAlert(NativeEvents.TrackerWarningAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        InfoHash = new Sha1Hash(alert.info_hash);

        TrackerUrl = alert.tracker_url == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.tracker_url) ?? string.Empty;
        WarningMessage = alert.warning_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.warning_message) ?? string.Empty;
    }

    /// <summary>The torrent handle this alert fired on. May be null for magnet-source torrents — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>The v1 info-hash of the torrent the warning belongs to — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>Tracker URL whose reply carried the warning.</summary>
    public string TrackerUrl { get; }

    /// <summary>Human-readable advisory text from the tracker.</summary>
    public string WarningMessage { get; }
}
