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

    event EventHandler<RssArticleCacheUpdatedEventArgs>? Updated;
}
