namespace WinBit.Core.Rss;

public sealed class RssArticleCacheUpdatedEventArgs : EventArgs
{
    public required string FeedUrl { get; init; }
}

/// <summary>
/// In-memory bounded cache of the most recent articles per feed URL, populated by subscribing
/// to <c>RssRefreshLoop.FeedRefreshed</c>. Lives in Core so UI viewmodels and the future
/// AutoDownloader service share the same view of unprocessed articles.
/// </summary>
public interface IRssArticleCache
{
    IReadOnlyList<RssArticle> Get(string feedUrl);

    /// <summary>
    /// Marks one or all articles in a feed as read. When <paramref name="articleId"/> is null,
    /// every article currently cached against <paramref name="feedUrl"/> is flagged; otherwise
    /// only the matching article is. Persists to the backing <see cref="IRssReadStore"/> so
    /// the flag survives a restart.
    /// </summary>
    Task MarkAsReadAsync(string feedUrl, string? articleId = null, CancellationToken ct = default);

    bool IsRead(string feedUrl, string articleId);

    /// <summary>Seeds in-memory read-state from persistent storage at startup.</summary>
    void Hydrate(IEnumerable<(string FeedUrl, string ArticleId)> entries);

    event EventHandler<RssArticleCacheUpdatedEventArgs>? Updated;
}
