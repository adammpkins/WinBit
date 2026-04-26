#nullable enable
using System;

namespace LibtorrentSharp.Enums;

/// <summary>
/// libtorrent's <c>torrent_flags_t</c> bitset. Every flag below is a single bit whose
/// index matches libtorrent's definition in <c>&lt;libtorrent/torrent_flags.hpp&gt;</c> —
/// keep the two in sync if libtorrent ever extends the set.
/// </summary>
/// <remarks>
/// Deprecated bits from libtorrent 1.x (pinned, override_resume_data,
/// merge_resume_trackers, use_resume_save_path, merge_resume_http_seeds —
/// bits 14–18) are intentionally omitted; they're no-ops in 2.x and we don't
/// want to encourage new callers to set them.
/// </remarks>
[Flags]
public enum TorrentFlags : ulong
{
    /// <summary>No flags set.</summary>
    None = 0,

    /// <summary>Skip the initial piece check on add; assume all pieces present on disk.</summary>
    SeedMode = 1UL << 0,

    /// <summary>Don't upload anything; participate only as a downloader / metadata requester.</summary>
    UploadMode = 1UL << 1,

    /// <summary>Upload-only mode with per-peer request tuning. Experimental — see libtorrent docs.</summary>
    ShareMode = 1UL << 2,

    /// <summary>When set, the session IP filter (if any) applies to this torrent's peer list.</summary>
    ApplyIpFilter = 1UL << 3,

    /// <summary>Pause the torrent. Pairs with <see cref="AutoManaged"/> for queue-aware vs forced pause.</summary>
    Paused = 1UL << 4,

    /// <summary>Queue-aware: libtorrent's queue manager may start/pause this torrent automatically.</summary>
    AutoManaged = 1UL << 5,

    /// <summary>When set on add, a duplicate info-hash raises an error instead of returning the existing handle.</summary>
    DuplicateIsError = 1UL << 6,

    /// <summary>Subscribe to state-updates for this torrent via <c>post_torrent_updates</c>.</summary>
    UpdateSubscribe = 1UL << 7,

    /// <summary>Super-seeding mode for seeder-side piece rarity. Takes effect only once seeding.</summary>
    SuperSeeding = 1UL << 8,

    /// <summary>Request pieces in order instead of rarest-first. Streaming / progressive UX.</summary>
    SequentialDownload = 1UL << 9,

    /// <summary>Pause the torrent automatically once all-files-available transitions to true.</summary>
    StopWhenReady = 1UL << 10,

    /// <summary>Use the trackers from <c>add_torrent_params</c> instead of merging them with any existing set.</summary>
    OverrideTrackers = 1UL << 11,

    /// <summary>Use the web seeds from <c>add_torrent_params</c> instead of merging.</summary>
    OverrideWebSeeds = 1UL << 12,

    /// <summary>Signals libtorrent should generate resume data at the next tick (rarely set by clients).</summary>
    NeedSaveResume = 1UL << 13,

    /// <summary>Disable DHT for just this torrent.</summary>
    DisableDht = 1UL << 19,

    /// <summary>Disable local service discovery for just this torrent.</summary>
    DisableLsd = 1UL << 20,

    /// <summary>Disable peer exchange for just this torrent.</summary>
    DisablePex = 1UL << 21,

    /// <summary>Skip piece verification even when the save path already contains data.</summary>
    NoVerifyFiles = 1UL << 22,

    /// <summary>When set, newly-added files default to <c>dont_download</c> priority.</summary>
    DefaultDontDownload = 1UL << 23,

    /// <summary>Marks this torrent as routed through an I2P SAM gateway.</summary>
    I2pTorrent = 1UL << 24,
}
