using WinBit.Core.Rss;

namespace WinBit.Tests.Helpers;

/// <summary>In-memory mirror of <see cref="IRssService"/> for Web UI endpoint tests.</summary>
public sealed class StubRssService : IRssService
{
    private RssFolder _root = new() { Name = "" };

    public event EventHandler? Changed;

    public Task<RssFolder> GetTreeAsync(CancellationToken ct = default) => Task.FromResult(_root);

    public Task UpsertFolderAsync(string path, CancellationToken ct = default)
    {
        _root = WithFolderAtPath(_root, Split(path));
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task UpsertFeedAsync(string parentPath, RssFeedConfig feed, CancellationToken ct = default)
    {
        _root = WithFolderAtPath(_root, Split(parentPath));
        _root = WithFeedAt(_root, Split(parentPath), feed);
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task RemoveFolderAsync(string path, CancellationToken ct = default)
    {
        var segs = Split(path);
        if (segs.Length == 0)
        {
            return Task.CompletedTask;
        }
        _root = WithFolderRemovedAt(_root, segs);
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task RemoveFeedAsync(string parentPath, string feedUrl, CancellationToken ct = default)
    {
        _root = WithFeedRemovedAt(_root, Split(parentPath), feedUrl);
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task MoveItemAsync(string sourcePath, string destPath, CancellationToken ct = default)
    {
        // The stub delegates to the real service implementation via a roundabout route: we
        // re-use the private Detach / folder insertion primitives by just creating a transient
        // RssService-lite. Easiest path here is to mirror the real code's move semantics with
        // a minimal inline implementation sufficient for endpoint tests.
        var srcSegs = Split(sourcePath);
        var dstSegs = Split(destPath);
        if (srcSegs.Length == 0 || dstSegs.Length == 0)
        {
            throw new ArgumentException("Source and destination paths must be non-empty.");
        }

        var (newRoot, detached) = DetachFromTree(_root, srcSegs);
        if (detached is null)
        {
            throw new InvalidOperationException($"Item not found at '{sourcePath}'.");
        }

        _root = newRoot;
        var dstParent = dstSegs[..^1];
        var dstLeaf = dstSegs[^1];
        _root = WithFolderAtPath(_root, dstParent);

        _root = detached switch
        {
            RssFolder folder => AttachFolder(_root, dstParent, folder with { Name = dstLeaf }),
            RssFeedConfig feed => WithFeedAt(_root, dstParent, feed with
            {
                Title = string.IsNullOrWhiteSpace(dstLeaf) ? feed.Title : dstLeaf,
            }),
            _ => _root,
        };

        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private static (RssFolder Updated, object? Detached) DetachFromTree(RssFolder root, string[] segs)
    {
        if (segs.Length == 1)
        {
            var leaf = segs[0];
            var folder = root.Folders.FirstOrDefault(f => string.Equals(f.Name, leaf, StringComparison.OrdinalIgnoreCase));
            if (folder is not null)
            {
                return (root with { Folders = root.Folders.Where(f => !ReferenceEquals(f, folder)).ToArray() }, folder);
            }
            var feed = root.Feeds.FirstOrDefault(f =>
                string.Equals(f.Title, leaf, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.Url, leaf, StringComparison.OrdinalIgnoreCase));
            if (feed is not null)
            {
                return (root with { Feeds = root.Feeds.Where(f => !ReferenceEquals(f, feed)).ToArray() }, feed);
            }
            return (root, null);
        }

        var next = root.Folders.FirstOrDefault(f => string.Equals(f.Name, segs[0], StringComparison.OrdinalIgnoreCase));
        if (next is null) return (root, null);
        var (updatedChild, detached) = DetachFromTree(next, segs[1..]);
        if (detached is null) return (root, null);
        return (root with
        {
            Folders = root.Folders.Select(f => ReferenceEquals(f, next) ? updatedChild : f).ToArray(),
        }, detached);
    }

    private static RssFolder AttachFolder(RssFolder root, string[] parentSegs, RssFolder folder)
    {
        if (parentSegs.Length == 0)
        {
            var existing = root.Folders.FirstOrDefault(f => string.Equals(f.Name, folder.Name, StringComparison.OrdinalIgnoreCase));
            var folders = existing is null
                ? root.Folders.Append(folder).ToArray()
                : root.Folders.Select(f => ReferenceEquals(f, existing) ? folder : f).ToArray();
            return root with { Folders = folders };
        }

        var child = root.Folders.First(f => string.Equals(f.Name, parentSegs[0], StringComparison.OrdinalIgnoreCase));
        var updated = AttachFolder(child, parentSegs[1..], folder);
        return root with
        {
            Folders = root.Folders.Select(f => ReferenceEquals(f, child) ? updated : f).ToArray(),
        };
    }

    public Task MarkRefreshedAsync(string feedUrl, DateTime utc, CancellationToken ct = default)
    {
        _root = WithFeedMutated(_root, feedUrl, f => f with { LastRefreshUtc = utc });
        return Task.CompletedTask;
    }

    private static string[] Split(string p) =>
        string.IsNullOrWhiteSpace(p) ? Array.Empty<string>()
            : p.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static RssFolder WithFolderAtPath(RssFolder root, string[] segs)
    {
        if (segs.Length == 0) return root;
        var existing = root.Folders.FirstOrDefault(f => string.Equals(f.Name, segs[0], StringComparison.OrdinalIgnoreCase));
        var child = WithFolderAtPath(existing ?? new RssFolder { Name = segs[0] }, segs[1..]);
        var folders = existing is null
            ? root.Folders.Append(child).ToArray()
            : root.Folders.Select(f => string.Equals(f.Name, segs[0], StringComparison.OrdinalIgnoreCase) ? child : f).ToArray();
        return root with { Folders = folders };
    }

    private static RssFolder WithFeedAt(RssFolder root, string[] segs, RssFeedConfig feed)
    {
        if (segs.Length == 0)
        {
            var feeds = root.Feeds.Any(f => string.Equals(f.Url, feed.Url, StringComparison.OrdinalIgnoreCase))
                ? root.Feeds.Select(f => string.Equals(f.Url, feed.Url, StringComparison.OrdinalIgnoreCase) ? feed : f).ToArray()
                : root.Feeds.Append(feed).ToArray();
            return root with { Feeds = feeds };
        }
        var child = root.Folders.First(f => string.Equals(f.Name, segs[0], StringComparison.OrdinalIgnoreCase));
        var updated = WithFeedAt(child, segs[1..], feed);
        return root with
        {
            Folders = root.Folders.Select(f => string.Equals(f.Name, segs[0], StringComparison.OrdinalIgnoreCase) ? updated : f).ToArray(),
        };
    }

    private static RssFolder WithFolderRemovedAt(RssFolder root, string[] segs)
    {
        if (segs.Length == 1)
        {
            return root with { Folders = root.Folders.Where(f => !string.Equals(f.Name, segs[0], StringComparison.OrdinalIgnoreCase)).ToArray() };
        }
        var child = root.Folders.FirstOrDefault(f => string.Equals(f.Name, segs[0], StringComparison.OrdinalIgnoreCase));
        if (child is null) return root;
        var updated = WithFolderRemovedAt(child, segs[1..]);
        return root with
        {
            Folders = root.Folders.Select(f => string.Equals(f.Name, segs[0], StringComparison.OrdinalIgnoreCase) ? updated : f).ToArray(),
        };
    }

    private static RssFolder WithFeedRemovedAt(RssFolder root, string[] segs, string url)
    {
        if (segs.Length == 0)
        {
            return root with { Feeds = root.Feeds.Where(f => !string.Equals(f.Url, url, StringComparison.OrdinalIgnoreCase)).ToArray() };
        }
        var child = root.Folders.FirstOrDefault(f => string.Equals(f.Name, segs[0], StringComparison.OrdinalIgnoreCase));
        if (child is null) return root;
        var updated = WithFeedRemovedAt(child, segs[1..], url);
        return root with
        {
            Folders = root.Folders.Select(f => string.Equals(f.Name, segs[0], StringComparison.OrdinalIgnoreCase) ? updated : f).ToArray(),
        };
    }

    private static RssFolder WithFeedMutated(RssFolder node, string url, Func<RssFeedConfig, RssFeedConfig> mutate)
    {
        var matched = false;
        var feeds = node.Feeds.Select(f =>
        {
            if (!matched && string.Equals(f.Url, url, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
                return mutate(f);
            }
            return f;
        }).ToArray();
        if (matched) return node with { Feeds = feeds };

        var folders = node.Folders.Select(child => WithFeedMutated(child, url, mutate)).ToArray();
        return node with { Folders = folders };
    }
}

public sealed class StubRssArticleCache : IRssArticleCache
{
    private readonly Dictionary<string, List<RssArticle>> _byFeed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _readByFeed = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<RssArticleCacheUpdatedEventArgs>? Updated;

    public IReadOnlyList<RssArticle> Get(string feedUrl) =>
        _byFeed.TryGetValue(feedUrl, out var list) ? list.ToArray() : Array.Empty<RssArticle>();

    public void Seed(string feedUrl, params RssArticle[] articles)
    {
        _byFeed[feedUrl] = new List<RssArticle>(articles);
        Updated?.Invoke(this, new RssArticleCacheUpdatedEventArgs { FeedUrl = feedUrl });
    }

    public Task MarkAsReadAsync(string feedUrl, string? articleId = null, CancellationToken ct = default)
    {
        if (!_readByFeed.TryGetValue(feedUrl, out var bucket))
        {
            bucket = new HashSet<string>(StringComparer.Ordinal);
            _readByFeed[feedUrl] = bucket;
        }
        if (articleId is null)
        {
            if (_byFeed.TryGetValue(feedUrl, out var list))
            {
                foreach (var a in list)
                {
                    if (!string.IsNullOrEmpty(a.Id)) bucket.Add(a.Id);
                }
            }
        }
        else
        {
            bucket.Add(articleId);
        }
        Updated?.Invoke(this, new RssArticleCacheUpdatedEventArgs { FeedUrl = feedUrl });
        return Task.CompletedTask;
    }

    public bool IsRead(string feedUrl, string articleId) =>
        _readByFeed.TryGetValue(feedUrl, out var b) && b.Contains(articleId);

    public void Hydrate(IEnumerable<(string FeedUrl, string ArticleId)> entries)
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

public sealed class InMemoryRssReadStore : IRssReadStore
{
    private readonly List<(string, string)> _rows = new();

    public Task<IReadOnlyList<(string FeedUrl, string ArticleId)>> LoadAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<(string, string)>>(_rows.ToArray());

    public Task MarkAsync(string feedUrl, string articleId, CancellationToken ct = default)
    {
        if (!_rows.Contains((feedUrl, articleId)))
        {
            _rows.Add((feedUrl, articleId));
        }
        return Task.CompletedTask;
    }

    public Task MarkManyAsync(string feedUrl, IReadOnlyCollection<string> articleIds, CancellationToken ct = default)
    {
        foreach (var id in articleIds)
        {
            if (!_rows.Contains((feedUrl, id))) _rows.Add((feedUrl, id));
        }
        return Task.CompletedTask;
    }
}

public sealed class StubRssRefresher : IRssRefresher
{
    public List<string> RefreshCalls { get; } = new();
    public Func<string, CancellationToken, Task>? OnRefresh { get; set; }

    public Task RefreshFeedAsync(string feedUrl, CancellationToken ct = default)
    {
        RefreshCalls.Add(feedUrl);
        return OnRefresh?.Invoke(feedUrl, ct) ?? Task.CompletedTask;
    }
}

public sealed class StubAutoDownloaderService : IAutoDownloaderService
{
    private readonly Dictionary<string, AutoDownloadRule> _byName = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? Changed;

    public Task<IReadOnlyList<AutoDownloadRule>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AutoDownloadRule>>(_byName.Values.OrderBy(r => r.Name).ToArray());

    public Task<AutoDownloadRule?> GetAsync(string name, CancellationToken ct = default) =>
        Task.FromResult(_byName.TryGetValue(name, out var r) ? r : null);

    public Task UpsertAsync(AutoDownloadRule rule, CancellationToken ct = default)
    {
        _byName[rule.Name] = rule;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string name, CancellationToken ct = default)
    {
        if (_byName.Remove(name))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        return Task.CompletedTask;
    }
}
