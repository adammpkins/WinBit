using WinBit.Core.Common;

namespace WinBit.Core.Sharing;

/// <summary>
/// Reads and writes per-torrent share-limit overrides to <c>Paths.ShareLimitOverridesFile</c>.
/// Atomic writes (temp + rename). The enforcement loop (separate deliverable) merges these
/// with <c>AppSettings.GlobalShareLimits</c> before applying an action.
/// </summary>
public interface IShareLimitOverrideService
{
    Task<IReadOnlyList<PerTorrentShareLimitOverride>> GetAllAsync(CancellationToken ct = default);

    Task<PerTorrentShareLimitOverride?> GetAsync(TorrentId id, CancellationToken ct = default);

    /// <summary>Adds a new override or replaces the existing entry for the same torrent id.</summary>
    Task UpsertAsync(PerTorrentShareLimitOverride entry, CancellationToken ct = default);

    Task RemoveAsync(TorrentId id, CancellationToken ct = default);

    /// <summary>
    /// Merges an override (if present) with the global configuration. Null/Default fields on
    /// the override fall back to the global value; non-null/non-default fields win.
    /// </summary>
    ShareLimits Effective(TorrentId id, ShareLimits global);
}
