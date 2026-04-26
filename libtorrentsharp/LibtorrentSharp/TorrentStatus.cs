using System;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp;

/// <summary>
/// Snapshot of an attached torrent's runtime state. Returned by
/// <see cref="TorrentHandle.GetCurrentStatus"/> / <see cref="MagnetHandle.GetCurrentStatus"/>.
/// </summary>
public sealed class TorrentStatus
{
    internal TorrentStatus(NativeStructs.TorrentStatus native)
    {
        State = native.state;
        Progress = native.progress;

        PeerCount = native.count_peers;
        SeedCount = native.count_seeds;

        BytesUploaded = native.bytes_uploaded;
        BytesDownloaded = native.bytes_downloaded;

        UploadRate = native.upload_rate;
        DownloadRate = native.download_rate;

        AllTimeUploaded = native.all_time_upload;
        AllTimeDownloaded = native.all_time_download;

        ActiveDuration = TimeSpan.FromSeconds(native.active_duration_seconds);
        FinishedDuration = TimeSpan.FromSeconds(native.finished_duration_seconds);
        SeedingDuration = TimeSpan.FromSeconds(native.seeding_duration_seconds);

        Eta = native.eta_seconds < 0 ? (TimeSpan?)null : TimeSpan.FromSeconds(native.eta_seconds);
        Ratio = native.ratio < 0 ? (float?)null : native.ratio;

        Flags = (TorrentFlags)native.flags;

        SavePath = native.save_path ?? string.Empty;
        ErrorMessage = native.error_string ?? string.Empty;
    }

    /// <summary>Lifecycle phase the torrent is currently in. See <see cref="TorrentState"/> for the full enum (CheckingFiles → CheckingResume → DownloadingMetadata → Downloading → Finished/Seeding, plus the Errored synthetic value when <see cref="ErrorMessage"/> is non-empty).</summary>
    public TorrentState State { get; }

    /// <summary>Fraction of the torrent that has been downloaded and verified, in <c>[0.0, 1.0]</c>. Same value libtorrent uses to compute the visible progress bar; covers both <see cref="TorrentState.CheckingFiles"/> hashing progress and <see cref="TorrentState.Downloading"/> piece progress.</summary>
    public float Progress { get; }

    /// <summary>Total connected peers (seeds + leechers, including those just-handshaked but not yet exchanging blocks). Equivalent to libtorrent's <c>torrent_status::num_peers</c>.</summary>
    public int PeerCount { get; }

    /// <summary>Subset of <see cref="PeerCount"/> that are seeds (have all pieces). Equivalent to libtorrent's <c>torrent_status::num_seeds</c>; useful for swarm-health UI badges.</summary>
    public int SeedCount { get; }

    /// <summary>Bytes uploaded this session.</summary>
    public long BytesUploaded { get; }

    /// <summary>Bytes downloaded this session.</summary>
    public long BytesDownloaded { get; }

    /// <summary>Instantaneous upload throughput in bytes/sec, smoothed by libtorrent's payload-rate accumulator. Drives the per-row upload speed column.</summary>
    public long UploadRate { get; }

    /// <summary>Instantaneous download throughput in bytes/sec, smoothed by libtorrent's payload-rate accumulator. Drives the per-row download speed column and feeds the <see cref="Eta"/> calculation.</summary>
    public long DownloadRate { get; }

    /// <summary>Cumulative upload across all sessions (from resume data).</summary>
    public long AllTimeUploaded { get; }

    /// <summary>Cumulative download across all sessions (from resume data).</summary>
    public long AllTimeDownloaded { get; }

    /// <summary>Wall-clock time the torrent has been active since first add.</summary>
    public TimeSpan ActiveDuration { get; }

    /// <summary>Wall-clock time the torrent has been in the finished state.</summary>
    public TimeSpan FinishedDuration { get; }

    /// <summary>Wall-clock time the torrent has been seeding.</summary>
    public TimeSpan SeedingDuration { get; }

    /// <summary>Estimated time to completion. <c>null</c> when unknown (no throughput or already done).</summary>
    public TimeSpan? Eta { get; }

    /// <summary>Seeding ratio (all-time upload ÷ all-time download). <c>null</c> when nothing has been downloaded yet.</summary>
    public float? Ratio { get; }

    /// <summary>libtorrent's <c>torrent_flags_t</c> bitset for this torrent — surfaces seed-mode / pause / auto-managed / share-mode / sequential-download / super-seeding / per-torrent DHT-LSD-PEX disables / etc. Typed mirror of the underlying ulong; see <see cref="TorrentFlags"/> for the full bit list.</summary>
    public TorrentFlags Flags { get; }

    /// <summary>Filesystem directory libtorrent is reading from / writing to for this torrent's data. Reflects any in-flight or completed <c>move_storage</c> operation; safe to display verbatim. Empty string only when the native marshal couldn't resolve the path (rare).</summary>
    public string SavePath { get; }

    /// <summary>Human-readable error message, or empty when no error.</summary>
    public string ErrorMessage { get; }
}
