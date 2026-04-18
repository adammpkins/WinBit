using WinBit.Core.Common;

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
    /// Removes a torrent from the session. Cached data is dropped, but downloaded files
    /// remain on disk — delete-on-remove is a UI-layer decision (M4 context menu).
    /// </summary>
    Task<Result> RemoveAsync(TorrentId id, CancellationToken ct = default);
}
