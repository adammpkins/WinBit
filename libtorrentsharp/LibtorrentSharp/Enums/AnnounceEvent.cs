// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

namespace LibtorrentSharp.Enums;

/// <summary>
/// The lifecycle event a tracker announce is reporting — mirrors libtorrent's
/// <c>lt::event_t</c>. Reported on <see cref="Alerts.TrackerAnnounceAlert.Event"/>
/// when the announce is sent; also embedded in the announce query itself
/// (BitTorrent's <c>event</c> HTTP parameter).
/// </summary>
public enum AnnounceEvent
{
    /// <summary>A periodic re-announce; the default for routine keepalive announces.</summary>
    None = 0,

    /// <summary>The torrent just finished downloading (all pieces verified).</summary>
    Completed = 1,

    /// <summary>The first announce after the torrent is added / started.</summary>
    Started = 2,

    /// <summary>The torrent is being stopped (gracefully leaving the swarm).</summary>
    Stopped = 3,

    /// <summary>The torrent is being paused (soft-stop, not fully leaving).</summary>
    Paused = 4
}
