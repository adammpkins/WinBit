namespace WinBit.Core.Search;

/// <summary>
/// Entry point for the Search page. Runs every registered plugin (or the subset named by
/// <paramref name="pluginNames"/>) against a query, concurrently, and yields their results as a
/// single merged async stream. Failures in individual plugins are absorbed; the caller only
/// sees successful results.
/// </summary>
public interface ISearchPluginHost
{
    /// <summary>All plugins currently registered with this host. Stable, in registration order.</summary>
    IReadOnlyList<ISearchPlugin> Plugins { get; }

    /// <summary>Adds a plugin. Replacing one with the same <see cref="ISearchPlugin.Name"/> is a
    /// no-op so registrars can call this idempotently.</summary>
    void Register(ISearchPlugin plugin);

    /// <summary>Removes a previously registered plugin by name. Returns true if something was removed.</summary>
    bool Unregister(string pluginName);

    /// <summary>Merged stream of hits across the selected plugins.</summary>
    IAsyncEnumerable<SearchResult> SearchAsync(
        SearchRequest request,
        IReadOnlyCollection<string>? pluginNames = null,
        CancellationToken ct = default);
}
