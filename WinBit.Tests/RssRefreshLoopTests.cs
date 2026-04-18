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

public sealed class RssRefreshLoopTests
{
    private const string SampleFeed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0">
          <channel>
            <title>t</title>
            <item>
              <title>Article 1</title>
              <enclosure url="https://x/1.torrent" />
            </item>
          </channel>
        </rss>
        """;

    [Fact]
    public async Task Tick_fetches_due_feeds_and_emits_articles()
    {
        using var temp = new TempDirectory();
        var rss = new RssService(NewPaths(temp));
        await rss.UpsertFeedAsync("", new RssFeedConfig { Url = "http://feed/a" });

        var settings = new InMemorySettingsService();
        var calls = new List<Uri>();
        var time = new FakeTimeProvider(new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Utc));

        var fetcher = new Func<Uri, CancellationToken, Task<string?>>((uri, _) =>
        {
            calls.Add(uri);
            return Task.FromResult<string?>(SampleFeed);
        });

        var loop = new RssRefreshLoop(rss, settings, fetcher, new NoopLog(), time);

        var received = new List<RssFeedRefreshedEventArgs>();
        loop.FeedRefreshed += (_, e) => received.Add(e);

        await loop.TickAsync(CancellationToken.None);

        calls.Should().ContainSingle().Which.Should().Be(new Uri("http://feed/a"));
        received.Should().ContainSingle()
            .Which.Articles.Should().ContainSingle()
            .Which.Title.Should().Be("Article 1");

        var feed = (await rss.GetTreeAsync()).Feeds.Single();
        feed.LastRefreshUtc.Should().Be(new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Tick_skips_feeds_inside_interval_window()
    {
        using var temp = new TempDirectory();
        var rss = new RssService(NewPaths(temp));
        var start = new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Utc);
        await rss.UpsertFeedAsync("", new RssFeedConfig
        {
            Url = "http://feed/a",
            LastRefreshUtc = start,
        });

        var settings = new InMemorySettingsService();
        settings.Current.Rss.RefreshIntervalMinutes = 30;

        var time = new FakeTimeProvider(start.AddMinutes(5));
        var calls = 0;
        var loop = new RssRefreshLoop(rss, settings,
            (_, _) => { calls++; return Task.FromResult<string?>(SampleFeed); },
            new NoopLog(), time);

        await loop.TickAsync(CancellationToken.None);
        calls.Should().Be(0);

        // Jump past the interval.
        time.Advance(TimeSpan.FromMinutes(30));
        await loop.TickAsync(CancellationToken.None);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Per_feed_override_shortens_interval()
    {
        using var temp = new TempDirectory();
        var rss = new RssService(NewPaths(temp));
        var start = new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Utc);

        // global = 60, override = 5 — 10 minutes after a refresh, override feed is due, global
        // feed is not.
        await rss.UpsertFeedAsync("", new RssFeedConfig
        {
            Url = "http://feed/global",
            LastRefreshUtc = start,
        });
        await rss.UpsertFeedAsync("", new RssFeedConfig
        {
            Url = "http://feed/override",
            LastRefreshUtc = start,
            RefreshIntervalMinutesOverride = 5,
        });

        var settings = new InMemorySettingsService();
        settings.Current.Rss.RefreshIntervalMinutes = 60;

        var time = new FakeTimeProvider(start.AddMinutes(10));
        var fetched = new List<Uri>();
        var loop = new RssRefreshLoop(rss, settings,
            (uri, _) => { fetched.Add(uri); return Task.FromResult<string?>(SampleFeed); },
            new NoopLog(), time);

        await loop.TickAsync(CancellationToken.None);

        fetched.Should().ContainSingle().Which.Host.Should().Be("feed");
        fetched.Single().AbsoluteUri.Should().Be("http://feed/override");
    }

    [Fact]
    public async Task Tick_is_a_noop_when_Rss_Enabled_is_false()
    {
        using var temp = new TempDirectory();
        var rss = new RssService(NewPaths(temp));
        await rss.UpsertFeedAsync("", new RssFeedConfig { Url = "http://feed" });

        var settings = new InMemorySettingsService();
        settings.Current.Rss.Enabled = false;

        var calls = 0;
        var loop = new RssRefreshLoop(rss, settings,
            (_, _) => { calls++; return Task.FromResult<string?>(SampleFeed); },
            new NoopLog());

        await loop.TickAsync(CancellationToken.None);
        calls.Should().Be(0);
    }

    [Fact]
    public async Task Fetch_failure_still_marks_refreshed_when_empty_response()
    {
        using var temp = new TempDirectory();
        var rss = new RssService(NewPaths(temp));
        await rss.UpsertFeedAsync("", new RssFeedConfig { Url = "http://feed" });

        var settings = new InMemorySettingsService();
        var loop = new RssRefreshLoop(rss, settings, (_, _) => Task.FromResult<string?>(""),
            new NoopLog());

        await loop.TickAsync(CancellationToken.None);

        (await rss.GetTreeAsync()).Feeds.Single().LastRefreshUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Fetch_exception_is_logged_but_does_not_crash_loop()
    {
        using var temp = new TempDirectory();
        var rss = new RssService(NewPaths(temp));
        await rss.UpsertFeedAsync("", new RssFeedConfig { Url = "http://feed/a" });
        await rss.UpsertFeedAsync("", new RssFeedConfig { Url = "http://feed/b" });

        var settings = new InMemorySettingsService();
        var log = new CapturingLog();

        var loop = new RssRefreshLoop(rss, settings, (uri, _) =>
        {
            if (uri.AbsoluteUri == "http://feed/a")
            {
                throw new HttpRequestException("boom");
            }
            return Task.FromResult<string?>(SampleFeed);
        }, log);

        var count = 0;
        loop.FeedRefreshed += (_, _) => count++;
        await loop.TickAsync(CancellationToken.None);

        count.Should().Be(1); // feed/b succeeded
        log.Messages.Should().Contain(m => m.Contains("boom"));
    }

    [Fact]
    public async Task Invalid_feed_url_is_skipped_with_log()
    {
        using var temp = new TempDirectory();
        var rss = new RssService(NewPaths(temp));
        await rss.UpsertFeedAsync("", new RssFeedConfig { Url = "not-a-url" });

        var settings = new InMemorySettingsService();
        var log = new CapturingLog();
        var loop = new RssRefreshLoop(rss, settings, (_, _) => Task.FromResult<string?>(SampleFeed),
            log);

        await loop.TickAsync(CancellationToken.None);

        log.Messages.Should().Contain(m => m.Contains("invalid URL"));
    }

    [Fact]
    public async Task RefreshFeedAsync_fetches_and_emits_bypassing_the_interval_gate()
    {
        using var temp = new TempDirectory();
        var rss = new RssService(NewPaths(temp));
        var justNow = new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Utc);
        await rss.UpsertFeedAsync("", new RssFeedConfig
        {
            Url = "http://feed/a",
            LastRefreshUtc = justNow,
        });

        var settings = new InMemorySettingsService();
        settings.Current.Rss.RefreshIntervalMinutes = 60;
        var time = new FakeTimeProvider(justNow.AddMinutes(1));

        var calls = 0;
        var loop = new RssRefreshLoop(rss, settings,
            (_, _) => { calls++; return Task.FromResult<string?>(SampleFeed); },
            new NoopLog(), time);

        RssFeedRefreshedEventArgs? received = null;
        loop.FeedRefreshed += (_, e) => received = e;

        await loop.RefreshFeedAsync("http://feed/a");

        calls.Should().Be(1);
        received.Should().NotBeNull();
        received!.FeedUrl.Should().Be("http://feed/a");
    }

    [Fact]
    public async Task RefreshFeedAsync_no_ops_on_invalid_url()
    {
        using var temp = new TempDirectory();
        var rss = new RssService(NewPaths(temp));
        var settings = new InMemorySettingsService();
        var log = new CapturingLog();

        var calls = 0;
        var loop = new RssRefreshLoop(rss, settings,
            (_, _) => { calls++; return Task.FromResult<string?>(SampleFeed); },
            log);

        await loop.RefreshFeedAsync("not-a-url");

        calls.Should().Be(0);
        log.Messages.Should().Contain(m => m.Contains("not a valid URL"));
    }

    [Fact]
    public async Task Nested_feeds_are_all_visited()
    {
        using var temp = new TempDirectory();
        var rss = new RssService(NewPaths(temp));
        await rss.UpsertFeedAsync("TV", new RssFeedConfig { Url = "http://feed/tv" });
        await rss.UpsertFeedAsync("TV/Shows", new RssFeedConfig { Url = "http://feed/shows" });

        var settings = new InMemorySettingsService();
        var fetched = new List<Uri>();
        var loop = new RssRefreshLoop(rss, settings,
            (uri, _) => { fetched.Add(uri); return Task.FromResult<string?>(SampleFeed); },
            new NoopLog());

        await loop.TickAsync(CancellationToken.None);

        fetched.Should().HaveCount(2);
    }

    private static Paths NewPaths(TempDirectory temp)
    {
        var opts = Options.Create(new WinBitCoreOptions { DataRoot = temp.Path });
        return new Paths(opts);
    }

    private sealed class InMemorySettingsService : ISettingsService
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

    private sealed class CapturingLog : ILogService
    {
        public List<string> Messages { get; } = new();
        public IReadOnlyList<LogEntry> GetMessages(long afterId = -1, LogSeverity filter = LogSeverity.All) => Array.Empty<LogEntry>();
        public void Write(string message, LogSeverity severity = LogSeverity.Normal) => Messages.Add(message);
        public event EventHandler<LogEntry>? MessageLogged { add { } remove { } }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTime utc) => _now = new DateTimeOffset(utc, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan span) => _now = _now.Add(span);
    }
}
