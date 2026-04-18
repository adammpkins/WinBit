using System.Text.Json;
using WinBit.Core.Persistence;

namespace WinBit.Core.Rss;

/// <summary>
/// File-backed implementation of <see cref="IRssService"/>. Persists the entire tree to
/// <c>rss/feeds.json</c> on every mutation using the atomic tmp-write-then-rename pattern used
/// by <c>CategoryService</c>, <c>TagService</c>, and <c>WatchedFolderService</c>.
/// </summary>
public sealed class RssService : IRssService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Paths _paths;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private RssFolder? _root;

    public event EventHandler? Changed;

    public RssService(Paths paths) => _paths = paths;

    public async Task<RssFolder> GetTreeAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _root!;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpsertFolderAsync(string path, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _root = WithFolderAtPath(_root!, SplitPath(path));
            await PersistAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpsertFeedAsync(string parentPath, RssFeedConfig feed, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(feed.Url))
        {
            throw new ArgumentException("Feed URL must not be empty.", nameof(feed));
        }

        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _root = WithFolderAtPath(_root!, SplitPath(parentPath));
            _root = WithFeedAt(_root!, SplitPath(parentPath), feed);
            await PersistAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveFolderAsync(string path, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var segments = SplitPath(path);
            if (segments.Length == 0)
            {
                return; // cannot remove root
            }
            _root = WithFolderRemovedAt(_root!, segments);
            await PersistAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveFeedAsync(string parentPath, string feedUrl, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _root = WithFeedRemovedAt(_root!, SplitPath(parentPath), feedUrl);
            await PersistAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task MarkRefreshedAsync(string feedUrl, DateTime utc, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var updated = WithFeedMutated(_root!, feedUrl, f => f with { LastRefreshUtc = utc });
            if (ReferenceEquals(updated, _root))
            {
                return; // feed not found — no-op
            }
            _root = updated;
            await PersistAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // --- Tree transforms (pure) ------------------------------------------

    internal static string[] SplitPath(string path) =>
        string.IsNullOrWhiteSpace(path)
            ? Array.Empty<string>()
            : path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static RssFolder WithFolderAtPath(RssFolder root, string[] segments)
    {
        if (segments.Length == 0)
        {
            return root;
        }

        var existing = root.Folders.FirstOrDefault(f =>
            string.Equals(f.Name, segments[0], StringComparison.OrdinalIgnoreCase));
        var child = existing ?? new RssFolder { Name = segments[0] };
        var rest = segments[1..];
        child = WithFolderAtPath(child, rest);

        var folders = existing is null
            ? root.Folders.Append(child).ToArray()
            : root.Folders.Select(f =>
                string.Equals(f.Name, segments[0], StringComparison.OrdinalIgnoreCase) ? child : f).ToArray();

        return root with { Folders = folders };
    }

    private static RssFolder WithFeedAt(RssFolder root, string[] segments, RssFeedConfig feed)
    {
        if (segments.Length == 0)
        {
            var feeds = root.Feeds.Any(f => string.Equals(f.Url, feed.Url, StringComparison.OrdinalIgnoreCase))
                ? root.Feeds.Select(f => string.Equals(f.Url, feed.Url, StringComparison.OrdinalIgnoreCase) ? feed : f).ToArray()
                : root.Feeds.Append(feed).ToArray();
            return root with { Feeds = feeds };
        }

        var child = root.Folders.First(f => string.Equals(f.Name, segments[0], StringComparison.OrdinalIgnoreCase));
        var updated = WithFeedAt(child, segments[1..], feed);
        return root with
        {
            Folders = root.Folders.Select(f =>
                string.Equals(f.Name, segments[0], StringComparison.OrdinalIgnoreCase) ? updated : f).ToArray(),
        };
    }

    private static RssFolder WithFolderRemovedAt(RssFolder root, string[] segments)
    {
        if (segments.Length == 1)
        {
            return root with
            {
                Folders = root.Folders
                    .Where(f => !string.Equals(f.Name, segments[0], StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
            };
        }

        var child = root.Folders.FirstOrDefault(f =>
            string.Equals(f.Name, segments[0], StringComparison.OrdinalIgnoreCase));
        if (child is null)
        {
            return root;
        }

        var updated = WithFolderRemovedAt(child, segments[1..]);
        return root with
        {
            Folders = root.Folders.Select(f =>
                string.Equals(f.Name, segments[0], StringComparison.OrdinalIgnoreCase) ? updated : f).ToArray(),
        };
    }

    private static RssFolder WithFeedRemovedAt(RssFolder root, string[] segments, string feedUrl)
    {
        if (segments.Length == 0)
        {
            return root with
            {
                Feeds = root.Feeds.Where(f => !string.Equals(f.Url, feedUrl, StringComparison.OrdinalIgnoreCase)).ToArray(),
            };
        }

        var child = root.Folders.FirstOrDefault(f =>
            string.Equals(f.Name, segments[0], StringComparison.OrdinalIgnoreCase));
        if (child is null)
        {
            return root;
        }

        var updated = WithFeedRemovedAt(child, segments[1..], feedUrl);
        return root with
        {
            Folders = root.Folders.Select(f =>
                string.Equals(f.Name, segments[0], StringComparison.OrdinalIgnoreCase) ? updated : f).ToArray(),
        };
    }

    private static RssFolder WithFeedMutated(RssFolder node, string feedUrl, Func<RssFeedConfig, RssFeedConfig> mutate)
    {
        var matched = false;

        var feeds = node.Feeds.Select(f =>
        {
            if (!matched && string.Equals(f.Url, feedUrl, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
                return mutate(f);
            }
            return f;
        }).ToArray();

        if (matched)
        {
            return node with { Feeds = feeds };
        }

        var folders = new RssFolder[node.Folders.Count];
        var changed = false;
        for (var i = 0; i < node.Folders.Count; i++)
        {
            var updatedChild = WithFeedMutated(node.Folders[i], feedUrl, mutate);
            folders[i] = updatedChild;
            if (!ReferenceEquals(updatedChild, node.Folders[i]))
            {
                changed = true;
            }
        }

        return changed ? node with { Folders = folders } : node;
    }

    // --- Persistence ------------------------------------------------------

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_root is not null)
        {
            return;
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_root is not null)
            {
                return;
            }

            Directory.CreateDirectory(_paths.RssDir);
            var file = FeedsFile;
            if (File.Exists(file))
            {
                try
                {
                    await using var stream = File.OpenRead(file);
                    var loaded = await JsonSerializer.DeserializeAsync<RssFolder>(stream, JsonOptions, ct).ConfigureAwait(false);
                    _root = loaded ?? new RssFolder { Name = "" };
                    return;
                }
                catch (JsonException)
                {
                    // Corrupt file — fall through to a fresh tree; next write replaces it.
                }
            }
            _root = new RssFolder { Name = "" };
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_paths.RssDir);
        var tmp = FeedsFile + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, _root, JsonOptions, ct).ConfigureAwait(false);
        }
        File.Move(tmp, FeedsFile, overwrite: true);
    }

    private string FeedsFile => Path.Combine(_paths.RssDir, "feeds.json");
}
