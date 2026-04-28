using WinBit.Core.Common;
using WinBit.Core.Sharing;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Wraps the BitTorrent engine (libtorrent-rasterbar via LibtorrentSharp). M1 defined the
/// contract; subsequent milestones expanded the surface.
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
    /// Synchronous pull counterpart to <see cref="TorrentUpdated"/>. Returns the current state
    /// of every loaded torrent — used by the Web UI's <c>/torrents/info</c> endpoint and any
    /// other caller that needs a point-in-time view without waiting for the next polling tick.
    /// </summary>
    IReadOnlyList<TorrentSnapshot> GetSnapshots();

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

    /// <summary>
    /// Persists a user-assigned display name for the torrent. Empty or whitespace names are
    /// rejected. The new name surfaces on the next polling tick without any explicit refresh.
    /// </summary>
    Task<Result> SetNameAsync(TorrentId id, string name, CancellationToken ct = default);

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

    /// <summary>
    /// Returns a snapshot of all currently connected peers for the identified torrent.
    /// Called at 3 s by <c>TorrentPropertiesViewModel</c> only while the Peers tab is visible.
    /// Returns an empty list when the torrent is unknown or has no open connections.
    /// </summary>
    Task<IReadOnlyList<PeerInfo>> GetPeersAsync(TorrentId id, CancellationToken ct = default);

    /// <summary>
    /// Returns a snapshot of all trackers for the identified torrent.
    /// Called at 3 s by <c>TorrentPropertiesViewModel</c> only while the Trackers tab is visible.
    /// Returns an empty list when the torrent is unknown or has no trackers.
    /// </summary>
    Task<IReadOnlyList<TrackerInfo>> GetTrackersAsync(TorrentId id, CancellationToken ct = default);

    /// <summary>
    /// Returns file entries for all non-pad files in the identified torrent.
    /// Called at 3 s by <c>TorrentPropertiesViewModel</c> only while the Content tab is visible.
    /// Returns an empty list when the torrent is unknown, is a magnet with unresolved metadata, or is a single-file torrent.
    /// </summary>
    Task<IReadOnlyList<TorrentFileEntry>> GetTorrentFilesAsync(TorrentId id, CancellationToken ct = default);

    /// <summary>
    /// Returns the have/missing state of every piece in the identified torrent.
    /// <c>true</c> = piece is fully downloaded and hash-verified; <c>false</c> = missing or in-progress.
    /// Called at 3 s by <c>TorrentPropertiesViewModel</c> only while the Pieces tab is visible.
    /// Returns an empty list when the torrent is unknown or metadata has not yet resolved.
    /// </summary>
    Task<IReadOnlyList<bool>> GetPiecesAsync(TorrentId id, CancellationToken ct = default);

    /// <summary>Reads the torrent's current per-torrent max down/up rate (bytes/sec). Null = torrent unknown.</summary>
    (long DownloadBps, long UploadBps)? GetSpeedLimits(TorrentId id);

    /// <summary>
    /// Sets per-torrent max down/up rate caps in bytes/sec. 0 = unlimited. Each null argument
    /// leaves that axis unchanged.
    /// </summary>
    Task<Result> SetSpeedLimitsAsync(TorrentId id, long? downloadBps, long? uploadBps, CancellationToken ct = default);

    /// <summary>
    /// Applies engine-wide download/upload rate caps (bytes/sec; 0 = unlimited). Driven by the
    /// <c>SpeedProfileApplier</c> hosted service from <c>AppSettings.Speed</c>. Returns success
    /// with no effect when the engine hasn't started yet — the applier re-runs after startup.
    /// </summary>
    Task<Result> SetGlobalSpeedLimitsAsync(long downloadBps, long uploadBps, CancellationToken ct = default);

    /// <summary>
    /// Toggles engine-level UPnP / NAT-PMP port mapping. Driven by <c>IPortForwardingService</c>
    /// from <c>AppSettings.Connection.Upnp</c>. Returns success with no effect when the engine
    /// hasn't started yet.
    /// </summary>
    Task<Result> SetPortForwardingAsync(bool enabled, CancellationToken ct = default);

    /// <summary>
    /// Applies Message Stream Encryption preference to the engine by rewriting
    /// <c>EngineSettings.AllowedEncryption</c>. Returns success with no effect when the engine
    /// hasn't started yet.
    /// </summary>
    Task<Result> SetEncryptionModeAsync(WinBit.Core.Settings.EncryptionMode mode, CancellationToken ct = default);

    /// <summary>
    /// Sets global peer-discovery flags. LSD/DHT/PEX live in libtorrent's <c>settings_pack</c>
    /// at the session level; the call rewrites those keys and the engine applies them
    /// uniformly across loaded torrents.
    /// </summary>
    Task<Result> SetPeerDiscoveryAsync(bool dht, bool pex, bool lsd, CancellationToken ct = default);

    /// <summary>
    /// Toggles BEP 21 initial-seeding (qBittorrent's "super-seeding") on a torrent. Used by the
    /// share-limit enforcement loop for <c>ShareLimitAction.EnableSuperSeeding</c>, and available
    /// to the per-torrent properties surface. Idempotent — flipping to the current state is a
    /// no-op from the peer's perspective.
    /// </summary>
    Task<Result> SetSuperSeedingAsync(TorrentId id, bool enabled, CancellationToken ct = default);

    /// <summary>
    /// Enables or disables sequential piece ordering on a running torrent. Sequential mode
    /// downloads pieces in order rather than rarest-first, which makes partial files usable
    /// during download (e.g. video playback from the start before completion).
    /// </summary>
    Task<Result> SetSequentialDownloadAsync(TorrentId id, bool enabled, CancellationToken ct = default);

    /// <summary>
    /// Renames a single file within a torrent by its zero-based index. Fire-and-forget at
    /// the libtorrent level — the engine emits a <c>FileRenamedAlert</c> asynchronously;
    /// the next Content tab poll will surface the new path automatically.
    /// </summary>
    /// <param name="newRelativePath">Torrent-relative path using forward slashes (e.g. "dir/file.iso").</param>
    Task RenameFileAsync(TorrentId id, int fileIndex, string newRelativePath, CancellationToken ct = default);

    /// <summary>
    /// Sets the download priority of a single file by its zero-based index. Applied immediately
    /// by libtorrent; the next Content tab poll surfaces the updated priority automatically.
    /// No-op when the torrent is not loaded or the file index has no match.
    /// </summary>
    Task SetFilePriorityAsync(TorrentId id, int fileIndex, FileDownloadPriority priority, CancellationToken ct = default);

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

    /// <summary>
    /// Returns static torrent metadata for the General detail tab. Returns null when the
    /// torrent is not loaded or its persistence record is missing.
    /// </summary>
    Task<TorrentDetailInfo?> GetTorrentDetailAsync(TorrentId id, CancellationToken ct = default);
}
