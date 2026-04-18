namespace WinBit.Core.Search;

/// <summary>
/// Contract implemented by every concrete search provider — C# ports and, eventually, the
/// Python.NET-hosted Nova3 plugins from qBittorrent. Plugins are stateless with respect to one
/// another; the <see cref="ISearchPluginHost"/> runs them concurrently and merges their streams
/// into a single result feed. Returning an empty stream is fine; throwing is fine too — the
/// host isolates plugin failures so one bad provider doesn't kill a search.
/// </summary>
public interface ISearchPlugin
{
    /// <summary>Stable identifier used in settings and the plugin-filter UI. Lowercase, no spaces.</summary>
    string Name { get; }

    /// <summary>Human-facing label shown in the Search page. Falls back to <see cref="Name"/>.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Category names this plugin understands (e.g. "movies", "software"). Empty means the plugin
    /// does not differentiate categories. The host passes
    /// <see cref="SearchRequest.Category"/> straight through and lets plugins decide how to
    /// interpret unknown values.
    /// </summary>
    IReadOnlyList<string> SupportedCategories { get; }

    /// <summary>
    /// Streams results for the given query. Implementations must cooperate with cancellation and
    /// should complete quickly when the token trips — users type fast.
    /// </summary>
    IAsyncEnumerable<SearchResult> SearchAsync(SearchRequest request, CancellationToken ct);
}
