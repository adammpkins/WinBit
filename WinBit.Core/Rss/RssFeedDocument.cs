namespace WinBit.Core.Rss;

public sealed record RssFeedDocument
{
    public required string FeedUrl { get; init; }

    public string? Title { get; init; }

    public string? Link { get; init; }

    public required IReadOnlyList<RssArticle> Articles { get; init; }
}
