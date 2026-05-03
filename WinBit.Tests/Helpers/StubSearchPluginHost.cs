using WinBit.Core.Search;

namespace WinBit.Tests.Helpers;

/// <summary>No-op <see cref="ISearchPluginHost"/> for endpoint tests that don't exercise search.</summary>
public sealed class StubSearchPluginHost : ISearchPluginHost
{
    public IReadOnlyList<ISearchPlugin> Plugins => [];

    public void Register(ISearchPlugin plugin) { }

    public bool Unregister(string pluginName) => false;

    public async IAsyncEnumerable<SearchResult> SearchAsync(
        SearchRequest request,
        IReadOnlyCollection<string>? pluginNames = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
