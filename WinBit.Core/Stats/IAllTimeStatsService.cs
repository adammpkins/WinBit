namespace WinBit.Core.Stats;

/// <summary>
/// Tracks all-time downloaded / uploaded bytes, persisted to <c>Paths.AllTimeStatsFile</c>.
/// The hosted service loads once at startup, feeds per-tick session totals in via
/// <see cref="Tick"/>, and flushes with <see cref="SaveAsync"/> on the 60 s autosave cadence
/// and on shutdown.
/// </summary>
public interface IAllTimeStatsService
{
    /// <summary>Current snapshot — persisted baseline plus deltas recorded since startup.</summary>
    AllTimeStats Current { get; }

    Task LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Feeds a fresh session-cumulative-bytes snapshot. The delta from the previous tick is
    /// added to the all-time counters; negative deltas (e.g. a torrent removed from the
    /// session) are clamped to 0 to avoid rewriting history.
    /// </summary>
    void Tick(long sessionDownloadedBytes, long sessionUploadedBytes);

    Task SaveAsync(CancellationToken ct = default);
}
