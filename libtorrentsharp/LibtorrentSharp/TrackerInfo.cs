using System;
using LibtorrentSharp.Enums;

namespace LibtorrentSharp;

/// <summary>
/// Snapshot of a single tracker attached to a torrent. Aggregate view — one entry
/// per tracker URL, with scrape counts maximized across endpoints × (v1, v2) info
/// hashes. Per-endpoint detail can be added later via a companion API if needed.
/// </summary>
/// <param name="Url">The tracker's announce URL as seen on the wire.</param>
/// <param name="Tier">Tracker tier (0 = primary). Lower tiers are preferred.</param>
/// <param name="Source">Bitmask of how this tracker came to be attached (slice 127 — typed mirror of libtorrent's announce_entry::tracker_source).</param>
/// <param name="Verified">Whether libtorrent has successfully contacted this tracker at least once.</param>
/// <param name="ScrapeComplete">Seeders reported by tracker. -1 when not yet known.</param>
/// <param name="ScrapeIncomplete">Leechers reported by tracker. -1 when not yet known.</param>
/// <param name="ScrapeDownloaded">Completed downloads reported by tracker. -1 when not yet known.</param>
/// <param name="Fails">Maximum consecutive failure count across endpoints.</param>
/// <param name="Updating">True when any endpoint has an in-flight announce.</param>
/// <param name="LastError">First non-empty error message across endpoints, or empty.</param>
/// <param name="NextAnnounce">Earliest next-announce across endpoints, or <see cref="DateTimeOffset.MinValue"/> when none scheduled.</param>
public record TrackerInfo(
    string Url,
    byte Tier,
    TrackerSource Source,
    bool Verified,
    int ScrapeComplete,
    int ScrapeIncomplete,
    int ScrapeDownloaded,
    byte Fails,
    bool Updating,
    string LastError,
    DateTimeOffset NextAnnounce);
