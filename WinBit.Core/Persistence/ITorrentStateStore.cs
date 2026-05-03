using WinBit.Core.Common;

namespace WinBit.Core.Persistence;

/// <summary>
/// Persistence surface for torrent metadata and engine-opaque fast-resume blobs.
/// <see cref="SaveFastResumeAsync"/> writes the blob alongside a version number; on load,
/// a version mismatch returns <c>null</c> so the caller discards the stale blob and re-checks.
/// Autosave every 60 s + on graceful shutdown is wired in a later M3 deliverable.
/// </summary>
public interface ITorrentStateStore
{
    Task UpsertTorrentAsync(TorrentStateRecord record, CancellationToken ct = default);

    Task RemoveTorrentAsync(TorrentId id, CancellationToken ct = default);

    /// <summary>
    /// Writes the fast-resume blob and <paramref name="version"/> for an existing torrent row.
    /// If no row exists for <paramref name="id"/>, the call is a no-op.
    /// </summary>
    Task SaveFastResumeAsync(TorrentId id, byte[] blob, int version, CancellationToken ct = default);

    /// <summary>
    /// Loads the fast-resume blob for <paramref name="id"/>. Returns <c>null</c> when no blob
    /// is stored or when the stored <c>resume_ver</c> does not match
    /// <paramref name="expectedVersion"/> — the caller should trigger a re-check on mismatch.
    /// </summary>
    Task<byte[]?> LoadFastResumeAsync(TorrentId id, int expectedVersion, CancellationToken ct = default);

    /// <summary>
    /// Sets <c>completed_utc</c> for an existing torrent row. If no row exists for
    /// <paramref name="id"/>, the call is a no-op. Used by the alert pump on
    /// <c>TorrentFinishedAlert</c> to record mid-session completions without a full upsert.
    /// </summary>
    Task UpdateCompletedUtcAsync(TorrentId id, DateTime completedUtc, CancellationToken ct = default);

    Task<IReadOnlyList<TorrentStateRecord>> GetAllAsync(CancellationToken ct = default);

    Task<TorrentStateRecord?> GetByIdAsync(TorrentId id, CancellationToken ct = default);
}
