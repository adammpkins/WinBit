namespace WinBit.Core.Rss;

/// <summary>
/// Minimal article shape the rule matcher needs. Full article model (GUID, description,
/// read state) lands with the RSS service deliverable.
/// </summary>
public sealed record RssArticle
{
    public required string FeedUrl { get; init; }

    public required string Title { get; init; }

    public string? TorrentUrl { get; init; }

    public DateTime PublishedUtc { get; init; }

    /// <summary>
    /// Stable article identifier. Pulled from RSS <c>&lt;guid&gt;</c> or Atom <c>&lt;id&gt;</c>
    /// when the feed supplies it; otherwise derived from <see cref="Title"/> +
    /// <see cref="TorrentUrl"/>. Used by markAsRead and dedup logic in the article cache.
    /// </summary>
    public string Id { get; init; } = "";
}
