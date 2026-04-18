using WinBit.Core.Settings;

namespace WinBit.Core.Rss;

public sealed class RssArticleCache : IRssArticleCache, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly RssRefreshLoop _loop;
    private readonly Dictionary<string, List<RssArticle>> _byFeed = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public event EventHandler<RssArticleCacheUpdatedEventArgs>? Updated;

    public RssArticleCache(RssRefreshLoop loop, ISettingsService settings)
    {
        _loop = loop;
        _settings = settings;
        _loop.FeedRefreshed += OnFeedRefreshed;
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
