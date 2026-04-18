namespace WinBit.Core.Rss;

/// <summary>
/// Persistence-facing API for RSS auto-download rules. Rules live in
/// <c>%LOCALAPPDATA%\WinBit\rss\rules.json</c>. The auto-dispatch loop (separate deliverable)
/// reads rules via <see cref="GetAllAsync"/> and writes match state back via
/// <see cref="UpsertAsync"/>.
/// </summary>
public interface IAutoDownloaderService
{
    Task<IReadOnlyList<AutoDownloadRule>> GetAllAsync(CancellationToken ct = default);

    Task<AutoDownloadRule?> GetAsync(string name, CancellationToken ct = default);

    Task UpsertAsync(AutoDownloadRule rule, CancellationToken ct = default);

    Task RemoveAsync(string name, CancellationToken ct = default);

    event EventHandler? Changed;
}
