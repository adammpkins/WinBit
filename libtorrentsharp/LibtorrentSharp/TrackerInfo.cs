using System;
using LibtorrentSharp.Enums;

namespace LibtorrentSharp;

/// <summary>
/// Snapshot of a single tracker attached to a torrent. Aggregate view — one entry
/// per tracker URL, with scrape counts maximized across endpoints × (v1, v2) info
/// hashes.
/// </summary>
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
    DateTimeOffset NextAnnounce,
    string TrackerId,
    string Message,
    bool StartSent,
    bool CompleteSent,
    DateTimeOffset MinAnnounce);
