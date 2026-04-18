namespace WinBit.Core.Search;

/// <summary>What to search for. <c>Category</c> is plugin-specific; null means "any".</summary>
public sealed record SearchRequest(string Query, string? Category = null);

/// <summary>
/// One hit from a plugin. At least one of <see cref="MagnetUri"/> / <see cref="TorrentUrl"/>
/// must be non-null for the Search page's one-click download to work; <see cref="DetailsUrl"/>
/// is optional and drives the "Open page" link.
/// </summary>
public sealed record SearchResult(
    string PluginName,
    string Name,
    long? SizeBytes = null,
    int? Seeders = null,
    int? Leechers = null,
    string? MagnetUri = null,
    string? TorrentUrl = null,
    string? DetailsUrl = null,
    DateTime? PublishedUtc = null);
