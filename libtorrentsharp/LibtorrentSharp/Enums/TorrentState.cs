// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
// csdl - a cross-platform libtorrent wrapper for .NET
// Licensed under Apache-2.0 - see the license file for more information

namespace LibtorrentSharp.Enums;

/// <summary>
/// Lifecycle state of a torrent, surfaced via <see cref="LibtorrentSharp.TorrentStatus.State"/>
/// and <see cref="LibtorrentSharp.Alerts.TorrentStatusAlert"/>'s OldState/NewState.
/// Mostly mirrors libtorrent's <c>torrent_status::state_t</c>, with two
/// LibtorrentSharp-side additions: <see cref="Unknown"/> catches any future
/// libtorrent state not yet mapped (forward compatibility), and <see cref="Errored"/>
/// is synthesized at the native marshal when <c>torrent_status::errc</c> is non-OK
/// (libtorrent itself doesn't have an "error" state — it carries the error
/// alongside whatever lifecycle state the torrent was in when the error fired).
/// </summary>
public enum TorrentState
{
    /// <summary>Sentinel — libtorrent reported a state value the marshal doesn't yet recognize. Typically appears only after a libtorrent version bump that adds a new state_t value; consumers should treat it as "no UI badge".</summary>
    Unknown = 0,

    /// <summary>Hashing existing on-disk files against the torrent's piece hashes — runs after attaching a torrent that already has data on disk. Progress is reflected in <see cref="LibtorrentSharp.TorrentStatus.Progress"/>.</summary>
    CheckingFiles = 1,

    /// <summary>Validating the resume data passed in via <c>add_torrent_params</c> against the on-disk file footprint — fast path that avoids a full re-hash when resume data matches reality. Falls back to <see cref="CheckingFiles"/> if resume data is rejected.</summary>
    CheckingResume = 2,

    /// <summary>Magnet-link torrent fetching its <c>.torrent</c> metadata from peers via the BEP-9 ut_metadata extension. Until this completes the torrent has no piece map and can't download content.</summary>
    DownloadingMetadata = 3,

    /// <summary>Actively fetching pieces from peers — the normal "in-flight download" state. Progress is reflected in <see cref="LibtorrentSharp.TorrentStatus.Progress"/>.</summary>
    Downloading = 4,

    /// <summary>Has all pieces and is actively distributing them to other peers. Distinct from <see cref="Finished"/>, which is complete-but-not-currently-uploading (auto-managed pause, share-limit hit, paused by user).</summary>
    Seeding = 5,

    /// <summary>Has all pieces but is not currently uploading — typically because the torrent was paused, hit its share-ratio/seed-time limit, or auto-managed pressure put it to sleep. Transitions to <see cref="Seeding"/> when libtorrent decides to start uploading again.</summary>
    Finished = 6,

    /// <summary>LibtorrentSharp-side synthetic value: native marshal sets this when <c>torrent_status::errc</c> is non-OK (the underlying libtorrent state value is overridden). Inspect <see cref="LibtorrentSharp.TorrentStatus"/>'s error fields for details.</summary>
    Errored = 7
}