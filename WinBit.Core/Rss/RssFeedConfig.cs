namespace WinBit.Core.Rss;

public sealed record RssFeedConfig
{
    public required string Url { get; init; }

    /// <summary>Human-readable title. Populated from the feed on first successful fetch; the
    /// user can override via the UI.</summary>
    public string? Title { get; init; }

    /// <summary>Per-feed override in minutes. Null = use the global
    /// <c>AppSettings.Rss.RefreshIntervalMinutes</c>.</summary>
    public int? RefreshIntervalMinutesOverride { get; init; }

    public DateTime? LastRefreshUtc { get; init; }
}

public sealed record RssFolder
{
    public required string Name { get; init; }

    public IReadOnlyList<RssFolder> Folders { get; init; } = Array.Empty<RssFolder>();

    public IReadOnlyList<RssFeedConfig> Feeds { get; init; } = Array.Empty<RssFeedConfig>();
}
