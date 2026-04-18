using FluentAssertions;
using Microsoft.Extensions.Options;
using WinBit.Core.Hosting;
using WinBit.Core.Logging;
using WinBit.Core.Persistence;
using WinBit.Core.Rss;
using WinBit.Core.Settings;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class RssArticleCacheTests
{
    [Fact]
    public void Absorb_stores_new_articles_newest_first()
    {
        var loop = MakeLoop(out var settings);
        settings.Current.Rss.MaxArticlesPerFeed = 50;
        var cache = new RssArticleCache(loop, settings);

        cache.Absorb("http://f", new[]
        {
            new RssArticle { FeedUrl = "http://f", Title = "A", TorrentUrl = "t/a" },
            new RssArticle { FeedUrl = "http://f", Title = "B", TorrentUrl = "t/b" },
        });

        // Articles are inserted at the head in input order → last one absorbed ends up first.
        cache.Get("http://f").Select(a => a.Title).Should().Equal("B", "A");
    }

    [Fact]
    public void Duplicate_articles_are_suppressed_across_batches()
    {
        var loop = MakeLoop(out var settings);
        var cache = new RssArticleCache(loop, settings);

        cache.Absorb("http://f", new[] { new RssArticle { FeedUrl = "http://f", Title = "A", TorrentUrl = "t/a" } });
        cache.Absorb("http://f", new[]
        {
            new RssArticle { FeedUrl = "http://f", Title = "A", TorrentUrl = "t/a" }, // dup
            new RssArticle { FeedUrl = "http://f", Title = "C", TorrentUrl = "t/c" },
        });

        cache.Get("http://f").Select(a => a.Title).Should().Equal("C", "A");
    }

    [Fact]
    public void Absorb_trims_to_MaxArticlesPerFeed()
    {
        var loop = MakeLoop(out var settings);
        settings.Current.Rss.MaxArticlesPerFeed = 2;
        var cache = new RssArticleCache(loop, settings);

        cache.Absorb("http://f", new[]
        {
            new RssArticle { FeedUrl = "http://f", Title = "A", TorrentUrl = "t/a" },
            new RssArticle { FeedUrl = "http://f", Title = "B", TorrentUrl = "t/b" },
            new RssArticle { FeedUrl = "http://f", Title = "C", TorrentUrl = "t/c" },
        });

        cache.Get("http://f").Should().HaveCount(2);
    }

    [Fact]
    public void Unknown_feed_returns_empty()
    {
        var loop = MakeLoop(out var settings);
        var cache = new RssArticleCache(loop, settings);
        cache.Get("http://does-not-exist").Should().BeEmpty();
    }

    [Fact]
    public async Task Cache_subscribes_to_loop_FeedRefreshed_event()
    {
        using var temp = new TempDirectory();
        var rss = new RssService(NewPaths(temp));
        await rss.UpsertFeedAsync("", new RssFeedConfig { Url = "http://f" });

        var settings = new InMemorySettings();
        settings.Current.Rss.MaxArticlesPerFeed = 10;

        var loop = new RssRefreshLoop(rss, settings,
            (_, _) => Task.FromResult<string?>("""
                <?xml version="1.0"?>
                <rss version="2.0">
                  <channel>
                    <item><title>Hello</title><enclosure url="https://x/1.torrent" /></item>
                  </channel>
                </rss>
                """),
            new NoopLog());

        var cache = new RssArticleCache(loop, settings);
        var events = 0;
        cache.Updated += (_, _) => events++;

        await loop.TickAsync(CancellationToken.None);

        events.Should().Be(1);
        cache.Get("http://f").Should().ContainSingle().Which.Title.Should().Be("Hello");
    }

    private static RssRefreshLoop MakeLoop(out InMemorySettings settings)
    {
        using var temp = new TempDirectory();
        settings = new InMemorySettings();
        // RefreshLoop needs an IRssService + fetcher even if we never tick it here.
        var rss = new RssService(NewPaths(temp));
        return new RssRefreshLoop(rss, settings, (_, _) => Task.FromResult<string?>(null), new NoopLog());
    }

    private static Paths NewPaths(TempDirectory temp)
    {
        var opts = Options.Create(new WinBitCoreOptions { DataRoot = temp.Path });
        return new Paths(opts);
    }

    private sealed class InMemorySettings : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(Current);
        public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Action<AppSettings> mutate, CancellationToken ct = default)
        {
            mutate(Current);
            Changed?.Invoke(this, Current);
            return Task.CompletedTask;
        }
        public event EventHandler<AppSettings>? Changed;
    }

    private sealed class NoopLog : ILogService
    {
        public IReadOnlyList<LogEntry> GetMessages(long afterId = -1, LogSeverity filter = LogSeverity.All) => Array.Empty<LogEntry>();
        public void Write(string message, LogSeverity severity = LogSeverity.Normal) { }
        public event EventHandler<LogEntry>? MessageLogged { add { } remove { } }
    }
}
