namespace WinBit.Core.Rss;

/// <summary>
/// Ports the match-relevant surface of
/// <c>qbittorrent/src/base/rss/rss_autodownloadrule.cpp</c>. Routing / scheduling fields
/// (save path, category, content layout, ignore-window) are carried on the full rule model
/// that ships with the AutoDownloader service; this record keeps the subset
/// <see cref="RuleMatcher"/> needs so the matcher stays pure.
/// </summary>
public sealed record AutoDownloadRule
{
    public string Name { get; init; } = "";

    public bool Enabled { get; init; } = true;

    public IReadOnlyList<string> AffectedFeeds { get; init; } = Array.Empty<string>();

    /// <summary>Raw qBittorrent syntax: <c>|</c> separates OR-expressions, whitespace separates
    /// AND-tokens within an expression (or a single regex when <see cref="UseRegex"/>).</summary>
    public string MustContain { get; init; } = "";

    public string MustNotContain { get; init; } = "";

    public bool UseRegex { get; init; }

    /// <summary>e.g. "1x2;5-8;9-". Empty = disabled.</summary>
    public string EpisodeFilter { get; init; } = "";

    public bool SmartFilter { get; init; }

    /// <summary>
    /// When &gt; 0, rejects articles whose <c>PublishedUtc</c> falls within N days of
    /// <see cref="LastMatchUtc"/>. Mirrors qBittorrent's <c>ignoreDays</c>.
    /// </summary>
    public int IgnoreDays { get; init; }

    public DateTime? LastMatchUtc { get; init; }

    public IReadOnlyList<string> PreviouslyMatchedEpisodes { get; init; } = Array.Empty<string>();
}
