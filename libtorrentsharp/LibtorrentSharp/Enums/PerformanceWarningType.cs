// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
// csdl - a cross-platform libtorrent wrapper for .NET
// Licensed under Apache-2.0 - see the license file for more information

namespace LibtorrentSharp.Enums;

/// <summary>
/// Discriminator for <see cref="LibtorrentSharp.Alerts.PerformanceWarningAlert.WarningCode"/>,
/// mirroring libtorrent's <c>performance_alert::performance_warning_t</c>.
/// Each value identifies a specific session/disk/IO subsystem libtorrent
/// flagged as a likely throughput bottleneck — most map to a tunable in
/// <c>settings_pack</c> that would relieve the pressure if raised.
/// </summary>
public enum PerformanceWarningType : byte
{
    /// <summary>Pending disk-write buffers have hit <c>settings_pack::max_queued_disk_bytes</c>; downloaded blocks are stalling waiting for the disk thread. Raise <c>max_queued_disk_bytes</c> or speed up the storage.</summary>
    OutstandingDiskBufferLimitReached = 0,

    /// <summary>Outstanding peer requests have hit <c>settings_pack::max_out_request_queue</c>; the picker can't keep peers fed. Raise the limit if download throughput is limited.</summary>
    OutstandingRequestLimitReached = 1,

    /// <summary>The configured upload rate limit is so low that protocol overhead (peer messages, BEP-3 keep-alives, BT-protocol) consumes most of the budget — peers may be choked. Raise <c>settings_pack::upload_rate_limit</c> or set it to 0 (unlimited).</summary>
    UploadLimitTooLow = 2,

    /// <summary>The configured download rate limit is so low that protocol overhead dominates and effective throughput is far below the cap. Raise <c>settings_pack::download_rate_limit</c> or set it to 0 (unlimited).</summary>
    DownloadLimitTooLow = 3,

    /// <summary>Send buffers are filling slowly enough that peers run out of data to read between fills. Raise <c>settings_pack::send_buffer_watermark</c> (and/or <c>send_buffer_low_watermark</c>) to keep the pipe full.</summary>
    SendBufferWatermarkTooLow = 4,

    /// <summary>Too many slots are reserved for optimistic unchoke relative to the regular-unchoke pool, starving regular peers. Lower <c>settings_pack::num_optimistic_unchoke_slots</c>.</summary>
    TooManyOptimisticUnchokeSlots = 5,

    /// <summary>The disk job queue has grown so large that new jobs are stalling. Lower <c>settings_pack::max_queued_disk_bytes</c> if storage can't keep up, or speed up the storage.</summary>
    TooHighDiskQueueLimit = 6,

    /// <summary>Pending async-IO operations have hit the platform's AIO limit; new disk operations are blocking. Generally an OS-level constraint — increase the kernel AIO limit if available.</summary>
    AioLimitReached = 7,

    /// <summary>The deprecated BitTyrant choking algorithm requires an explicit upload limit (it tunes itself against the cap). Set <c>settings_pack::upload_rate_limit</c> to a non-zero value or switch to <c>fixed_slots_choker</c>/<c>rate_based_choker</c>.</summary>
    DeprecatedBittyrantWithNoUplimit = 8,

    /// <summary>The configured outgoing-port range is too narrow for the number of concurrent connections, forcing reuse churn. Widen <c>settings_pack::outgoing_port</c> + <c>num_outgoing_ports</c> or use the OS's ephemeral range (set both to 0).</summary>
    TooFewOutgoingPorts = 9,

    /// <summary>The process is approaching its file-descriptor ceiling (open sockets + open torrent files). Raise the OS <c>ulimit -n</c> / <c>setrlimit(RLIMIT_NOFILE)</c> or lower <c>settings_pack::connections_limit</c>.</summary>
    TooFewFileDescriptors = 10
}