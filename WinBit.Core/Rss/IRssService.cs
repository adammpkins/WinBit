namespace WinBit.Core.Rss;

/// <summary>
/// Persistence-facing API for the RSS feed tree. Tree paths are forward-slash-separated, e.g.
/// <c>"TV/Shows"</c> — the root folder has an empty path (<c>""</c>). The refresh loop (shipped
/// with the next M9 deliverable) reads the tree via <see cref="GetTreeAsync"/> and writes back
/// refresh timestamps through <see cref="MarkRefreshedAsync"/>.
/// </summary>
public interface IRssService
{
    Task<RssFolder> GetTreeAsync(CancellationToken ct = default);

    Task UpsertFolderAsync(string path, CancellationToken ct = default);

    Task UpsertFeedAsync(string parentPath, RssFeedConfig feed, CancellationToken ct = default);

    Task RemoveFolderAsync(string path, CancellationToken ct = default);

    Task RemoveFeedAsync(string parentPath, string feedUrl, CancellationToken ct = default);

    Task MarkRefreshedAsync(string feedUrl, DateTime utc, CancellationToken ct = default);

    event EventHandler? Changed;
}
