using WinBit.Core.Common;
using WinBit.Core.Sharing;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Wraps the BitTorrent engine (MonoTorrent). Full surface arrives in M3; M1 defines the contract.
/// </summary>
public interface ITorrentSessionService : IAsyncDisposable
{
    bool IsRunning { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);

    IReadOnlyList<TorrentId> Torrents { get; }

    /// <summary>
    /// Raised once per <c>StatusPollingLoop</c> tick with a batch of snapshots for every active
    /// torrent. Subscribers must marshal to the UI thread via <c>IDispatcherQueueProvider</c>.
    /// </summary>
    event EventHandler<IReadOnlyList<TorrentSnapshot>>? TorrentUpdated;

    /// <summary>
    /// Snapshots every <c>TorrentManager</c> the engine currently holds and raises
    /// <see cref="TorrentUpdated"/> once with the batch. Called by <c>StatusPollingLoop</c>.
    /// </summary>
    void CaptureAndPublishSnapshots();

    /// <summary>
    /// Writes each manager's fast-resume blob through <c>ITorrentStateStore</c>. Called on
    /// the 60 s autosave cadence by <c>WinBitHostedService</c> and once more on shutdown.
    /// </summary>
    Task PersistFastResumeAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds a torrent from a magnet URI or local <c>.torrent</c> file path. URL-based adds
    /// go through <c>UrlDownloader</c> before this call lands. Returns a <see cref="Result{T}"/>
    /// — viewmodels render failures as an inline <c>InfoBar</c> on the Add dialog.
    /// </summary>
    Task<Result<TorrentId>> AddAsync(AddTorrentParams parameters, CancellationToken ct = default);

    /// <summary>
    /// Removes a torrent from the session. When <paramref name="deleteContent"/> is
    /// <c>false</c> (default) only engine state is dropped and downloaded files remain on
    /// disk. When <c>true</c>, the engine also deletes the downloaded payload — used by the
    /// share-limit enforcement loop for <c>ShareLimitAction.RemoveWithContent</c>, and
    /// available to the UI's "Remove with data" context-menu entry.
    /// </summary>
    Task<Result> RemoveAsync(TorrentId id, bool deleteContent = false, CancellationToken ct = default);

    /// <summary>Pauses the identified torrent (downloading/seeding → paused).</summary>
    Task<Result> PauseAsync(TorrentId id, CancellationToken ct = default);

    /// <summary>Resumes the identified torrent from paused or stopped.</summary>
    Task<Result> ResumeAsync(TorrentId id, CancellationToken ct = default);

    /// <summary>Forces a full hash check of on-disk data; auto-starts on completion.</summary>
    Task<Result> ForceRecheckAsync(TorrentId id, CancellationToken ct = default);

    /// <summary>Forces an immediate announce to every tracker the torrent knows about.</summary>
    Task<Result> ForceReannounceAsync(TorrentId id, CancellationToken ct = default);

    /// <summary>Returns the torrent's magnet URI (for copy-to-clipboard), or null if unknown.</summary>
    string? GetMagnetUri(TorrentId id);

    /// <summary>Returns the torrent's save-path (for Open folder), or null if unknown.</summary>
    string? GetSavePath(TorrentId id);

    /// <summary>Returns the torrent's display name (from metadata), or null if unknown.</summary>
    string? GetName(TorrentId id);

    /// <summary>
    /// Returns the distinct tracker hosts the torrent announces to (for the sidebar's "by host"
    /// grouping). Empty for magnets before metadata arrives. The host is taken from each
    /// tracker's announce URI; duplicates across tiers collapse into one entry.
    /// </summary>
    IReadOnlyList<string> GetTrackerHosts(TorrentId id);

    /// <summary>Reads the torrent's current per-torrent max down/up rate (bytes/sec). Null = torrent unknown.</summary>
    (long DownloadBps, long UploadBps)? GetSpeedLimits(TorrentId id);

    /// <summary>
    /// Sets per-torrent max down/up rate caps in bytes/sec. 0 = unlimited. Each null argument
    /// leaves that axis unchanged.
    /// </summary>
    Task<Result> SetSpeedLimitsAsync(TorrentId id, long? downloadBps, long? uploadBps, CancellationToken ct = default);

    /// <summary>
    /// Toggles BEP 21 initial-seeding (qBittorrent's "super-seeding") on a torrent. Used by the
    /// share-limit enforcement loop for <c>ShareLimitAction.EnableSuperSeeding</c>, and available
    /// to the per-torrent properties surface. Idempotent — flipping to the current state is a
    /// no-op from the peer's perspective.
    /// </summary>
    Task<Result> SetSuperSeedingAsync(TorrentId id, bool enabled, CancellationToken ct = default);

    /// <summary>
    /// Reads the enforcement-loop inputs for a torrent. Null if the torrent isn't loaded.
    /// <c>SeedingTime</c> and <c>InactiveSeedingTime</c> are NOT on this snapshot — they're
    /// derived from successive ticks by <c>ShareLimitEnforcementLoop</c>.
    /// </summary>
    ShareLimitSnapshot? GetShareLimitSnapshot(TorrentId id);

    /// <summary>
    /// Current session-level stats (global rates, connections, DHT node count). Returns a
    /// zeroed snapshot while the engine isn't running.
    /// </summary>
    SessionStats GetSessionStats();
}
