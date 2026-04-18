namespace WinBit.Core.Rss;

/// <summary>
/// Persistence surface for per-article "read" state. Backs
/// <see cref="IRssArticleCache.MarkAsReadAsync"/> so the Web UI's markAsRead survives a
/// WinBit restart.
/// </summary>
public interface IRssReadStore
{
    /// <summary>Loads every stored (feedUrl, articleId) pair — used to hydrate the in-memory cache at startup.</summary>
    Task<IReadOnlyList<(string FeedUrl, string ArticleId)>> LoadAllAsync(CancellationToken ct = default);

    Task MarkAsync(string feedUrl, string articleId, CancellationToken ct = default);

    Task MarkManyAsync(string feedUrl, IReadOnlyCollection<string> articleIds, CancellationToken ct = default);
}
