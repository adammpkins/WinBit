using WinBit.Core.Settings;

namespace WinBit.Core.Rss;

public sealed class RssArticleCache : IRssArticleCache, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly RssRefreshLoop _loop;
    private readonly IRssReadStore _readStore;
    private readonly Dictionary<string, List<RssArticle>> _byFeed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _readByFeed = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public event EventHandler<RssArticleCacheUpdatedEventArgs>? Updated;

    public RssArticleCache(RssRefreshLoop loop, ISettingsService settings, IRssReadStore readStore)
    {
        _loop = loop;
        _settings = settings;
        _readStore = readStore;
        _loop.FeedRefreshed += OnFeedRefreshed;
    }

    public void Hydrate(IEnumerable<(string FeedUrl, string ArticleId)> entries)
    {
        lock (_gate)
        {
            foreach (var (feedUrl, articleId) in entries)
            {
                if (!_readByFeed.TryGetValue(feedUrl, out var bucket))
                {
                    bucket = new HashSet<string>(StringComparer.Ordinal);
                    _readByFeed[feedUrl] = bucket;
                }
                bucket.Add(articleId);
            }
        }
    }

    public IReadOnlyList<RssArticle> Get(string feedUrl)
    {
        lock (_gate)
        {
            return _byFeed.TryGetValue(feedUrl, out var list)
                ? list.ToArray()
                : Array.Empty<RssArticle>();
        }
    }

    public async Task MarkAsReadAsync(string feedUrl, string? articleId = null, CancellationToken ct = default)
    {
        var newlyMarkedForPersist = new List<string>();
        var raise = false;

        lock (_gate)
        {
            if (!_readByFeed.TryGetValue(feedUrl, out var bucket))
            {
                bucket = new HashSet<string>(StringComparer.Ordinal);
                _readByFeed[feedUrl] = bucket;
            }

            if (articleId is null)
            {
                if (_byFeed.TryGetValue(feedUrl, out var articles))
                {
                    foreach (var a in articles)
                    {
                        if (!string.IsNullOrEmpty(a.Id) && bucket.Add(a.Id))
                        {
                            raise = true;
                            newlyMarkedForPersist.Add(a.Id);
                        }
                    }
                }
            }
            else
            {
                if (bucket.Add(articleId))
                {
                    raise = true;
                    newlyMarkedForPersist.Add(articleId);
                }
            }
        }

        if (newlyMarkedForPersist.Count > 0)
        {
            await _readStore.MarkManyAsync(feedUrl, newlyMarkedForPersist, ct).ConfigureAwait(false);
        }

        if (raise)
        {
            Updated?.Invoke(this, new RssArticleCacheUpdatedEventArgs { FeedUrl = feedUrl });
        }
    }

    public bool IsRead(string feedUrl, string articleId)
    {
        lock (_gate)
        {
            return _readByFeed.TryGetValue(feedUrl, out var bucket) && bucket.Contains(articleId);
        }
    }

    public void Dispose() => _loop.FeedRefreshed -= OnFeedRefreshed;

    private void OnFeedRefreshed(object? sender, RssFeedRefreshedEventArgs e)
    {
        Absorb(e.FeedUrl, e.Articles);
        Updated?.Invoke(this, new RssArticleCacheUpdatedEventArgs { FeedUrl = e.FeedUrl });
    }

    /// <summary>
    /// Public so tests and future callers (e.g. a manual "refresh now" from the UI) can prime
    /// the cache without waiting for a <see cref="RssRefreshLoop"/> tick.
    /// </summary>
    public void Absorb(string feedUrl, IReadOnlyList<RssArticle> incoming)
    {
        var cap = Math.Max(1, _settings.Current.Rss.MaxArticlesPerFeed);
        lock (_gate)
        {
            if (!_byFeed.TryGetValue(feedUrl, out var bucket))
            {
                bucket = new List<RssArticle>();
                _byFeed[feedUrl] = bucket;
            }

            // Dedupe by (Title, TorrentUrl) — enclosure is the stable identity when feeds don't
            // supply GUIDs, and title keeps magnet-only feeds deduplicable too.
            var known = new HashSet<(string, string?)>(
                bucket.Select(a => (a.Title, a.TorrentUrl)));

            foreach (var a in incoming)
            {
                if (known.Add((a.Title, a.TorrentUrl)))
                {
                    bucket.Insert(0, a);
                }
            }

            if (bucket.Count > cap)
            {
                bucket.RemoveRange(cap, bucket.Count - cap);
            }
        }
    }
}
