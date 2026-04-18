using Microsoft.Extensions.Hosting;

namespace WinBit.Core.Rss;

/// <summary>
/// On startup, loads every persisted <c>rss_read</c> row and seeds
/// <see cref="IRssArticleCache"/> so prior markAsRead flags survive an app restart.
/// </summary>
public sealed class RssReadStateHydrator : IHostedService
{
    private readonly IRssReadStore _store;
    private readonly IRssArticleCache _cache;

    public RssReadStateHydrator(IRssReadStore store, IRssArticleCache cache)
    {
        _store = store;
        _cache = cache;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var rows = await _store.LoadAllAsync(ct).ConfigureAwait(false);
        _cache.Hydrate(rows);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
