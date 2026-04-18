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
}
