using System.Net;
using System.Text.Json;
using FluentAssertions;
using WinBit.Core.Logging;
using WinBit.Core.Rss;
using WinBit.Core.Settings;
using WinBit.Core.WebUi;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class RssEndpointsTests : IAsyncLifetime
{
    private readonly WebUiService _service;
    private readonly StubRssService _rss = new();
    private readonly StubAutoDownloaderService _rules = new();
    private readonly StubRssArticleCache _articles = new();
    private readonly StubRssRefresher _refresher = new();
    private readonly CookieContainer _cookies = new();
    private HttpClient _client = null!;
    private HttpClientHandler _handler = null!;

    public InMemorySettings Settings { get; } = new();

    public RssEndpointsTests()
    {
        Settings.Current.WebUi.Enabled = true;
        Settings.Current.WebUi.Port = 0;
        _service = new WebUiService(Settings, new WebUiAuthService(Settings),
            new StubTorrentSession(), new NoopLog(), new PeerLogService(),
            new StubCategoryService(), new StubTagService(), _rss, _rules, _articles, _refresher,
            new WinBit.Core.BitTorrent.TorrentCreatorQueue(new WinBit.Core.BitTorrent.TorrentCreatorService()),
            new StubTorrentStateStore(), TestPaths.Ambient);
    }

    public async Task InitializeAsync()
    {
        await _service.StartAsync(CancellationToken.None);
        _handler = new HttpClientHandler { CookieContainer = _cookies, UseCookies = true };
        _client = new HttpClient(_handler) { BaseAddress = new Uri($"http://localhost:{_service.BoundPort}") };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _handler.Dispose();
        await _service.StopAsync(CancellationToken.None);
    }

    // ---- Feed tree --------------------------------------------------------

    [Fact]
    public async Task Items_requires_auth()
    {
        (await _client.GetAsync("/api/v2/rss/items")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Items_returns_recursive_folder_feed_tree()
    {
        await _rss.UpsertFeedAsync("TV/Shows",
            new RssFeedConfig { Url = "http://feed/a", Title = "Show A" });
        await _rss.UpsertFeedAsync("", new RssFeedConfig { Url = "http://feed/root" });

        await Login();

        var json = JsonDocument.Parse(await _client.GetStringAsync("/api/v2/rss/items")).RootElement;

        // Root-level feed keyed by URL (no Title).
        json.GetProperty("http://feed/root").GetProperty("url").GetString().Should().Be("http://feed/root");

        // Nested folder + feed keyed by Title.
        var showA = json.GetProperty("TV").GetProperty("Shows").GetProperty("Show A");
        showA.GetProperty("url").GetString().Should().Be("http://feed/a");
    }

    [Fact]
    public async Task AddFolder_creates_nested_path()
    {
        await Login();

        var response = await _client.PostAsync("/api/v2/rss/addFolder",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("path", "TV/Drama") }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _rss.GetTreeAsync()).Folders.Single().Folders.Single().Name.Should().Be("Drama");
    }

    [Fact]
    public async Task AddFeed_with_folder_path_routes_feed_correctly()
    {
        await Login();

        await _client.PostAsync("/api/v2/rss/addFeed",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("url", "http://feed/x"),
                new KeyValuePair<string, string>("path", "TV/MyFeed"),
            }));

        var tv = (await _rss.GetTreeAsync()).Folders.Single();
        tv.Feeds.Single().Url.Should().Be("http://feed/x");
        tv.Feeds.Single().Title.Should().Be("MyFeed");
    }

    [Fact]
    public async Task AddFeed_requires_url()
    {
        await Login();

        var response = await _client.PostAsync("/api/v2/rss/addFeed",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("path", "x") }));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RemoveItem_drops_feed_by_path()
    {
        await _rss.UpsertFeedAsync("TV",
            new RssFeedConfig { Url = "http://feed/x", Title = "MyFeed" });

        await Login();

        await _client.PostAsync("/api/v2/rss/removeItem",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("path", "TV/MyFeed") }));

        (await _rss.GetTreeAsync()).Folders.Single().Feeds.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveItem_drops_folder_when_path_is_not_a_feed()
    {
        await _rss.UpsertFolderAsync("TV/Drama");

        await Login();

        await _client.PostAsync("/api/v2/rss/removeItem",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("path", "TV/Drama") }));

        (await _rss.GetTreeAsync()).Folders.Single().Folders.Should().BeEmpty();
    }

    // ---- Rules ------------------------------------------------------------

    [Fact]
    public async Task Rules_returns_empty_object_when_no_rules_exist()
    {
        await Login();

        var text = await _client.GetStringAsync("/api/v2/rss/rules");
        text.Should().Be("{}");
    }

    [Fact]
    public async Task SetRule_creates_rule_from_ruleDef_json()
    {
        await Login();

        var ruleDef = """
            {"enabled":true,"mustContain":"1080p","useRegex":false,"smartFilter":true,"affectedFeeds":["http://feed/a"]}
            """;
        var response = await _client.PostAsync("/api/v2/rss/setRule",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("ruleName", "r"),
                new KeyValuePair<string, string>("ruleDef", ruleDef),
            }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = (await _rules.GetAsync("r"))!;
        stored.MustContain.Should().Be("1080p");
        stored.SmartFilter.Should().BeTrue();
        stored.AffectedFeeds.Should().ContainSingle().Which.Should().Be("http://feed/a");
    }

    [Fact]
    public async Task SetRule_rejects_invalid_json()
    {
        await Login();

        var response = await _client.PostAsync("/api/v2/rss/setRule",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("ruleName", "r"),
                new KeyValuePair<string, string>("ruleDef", "{not json"),
            }));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Rules_returns_serialized_map()
    {
        await _rules.UpsertAsync(new AutoDownloadRule { Name = "r", MustContain = "hd" });
        await Login();

        var json = JsonDocument.Parse(await _client.GetStringAsync("/api/v2/rss/rules")).RootElement;
        json.GetProperty("r").GetProperty("mustContain").GetString().Should().Be("hd");
    }

    [Fact]
    public async Task RemoveRule_drops_the_named_rule()
    {
        await _rules.UpsertAsync(new AutoDownloadRule { Name = "r" });
        await Login();

        await _client.PostAsync("/api/v2/rss/removeRule",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("ruleName", "r") }));

        (await _rules.GetAsync("r")).Should().BeNull();
    }

    [Fact]
    public async Task RenameRule_moves_rule_to_new_name()
    {
        await _rules.UpsertAsync(new AutoDownloadRule { Name = "old", MustContain = "x" });
        await Login();

        var response = await _client.PostAsync("/api/v2/rss/renameRule",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("ruleName", "old"),
                new KeyValuePair<string, string>("newRuleName", "new"),
            }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await _rules.GetAsync("old")).Should().BeNull();
        (await _rules.GetAsync("new"))!.MustContain.Should().Be("x");
    }

    [Fact]
    public async Task RenameRule_returns_404_when_source_missing()
    {
        await Login();

        var response = await _client.PostAsync("/api/v2/rss/renameRule",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("ruleName", "missing"),
                new KeyValuePair<string, string>("newRuleName", "x"),
            }));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- refreshItem -------------------------------------------------------

    [Fact]
    public async Task RefreshItem_forces_refresh_of_feed_at_given_path()
    {
        await _rss.UpsertFeedAsync("TV", new RssFeedConfig { Url = "http://feed/a", Title = "Feed A" });
        await Login();

        var response = await _client.PostAsync("/api/v2/rss/refreshItem",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("itemPath", "TV/Feed A") }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        _refresher.RefreshCalls.Should().ContainSingle().Which.Should().Be("http://feed/a");
    }

    [Fact]
    public async Task RefreshItem_on_folder_path_refreshes_every_feed_inside()
    {
        await _rss.UpsertFeedAsync("TV", new RssFeedConfig { Url = "http://feed/a", Title = "A" });
        await _rss.UpsertFeedAsync("TV/Shows", new RssFeedConfig { Url = "http://feed/b", Title = "B" });
        await Login();

        await _client.PostAsync("/api/v2/rss/refreshItem",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("itemPath", "TV") }));

        _refresher.RefreshCalls.Should().BeEquivalentTo(new[] { "http://feed/a", "http://feed/b" });
    }

    [Fact]
    public async Task RefreshItem_returns_404_when_path_resolves_to_nothing()
    {
        await Login();
        var response = await _client.PostAsync("/api/v2/rss/refreshItem",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("itemPath", "Missing") }));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RefreshItem_requires_itemPath()
    {
        await Login();
        var response = await _client.PostAsync("/api/v2/rss/refreshItem",
            new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- markAsRead --------------------------------------------------------

    [Fact]
    public async Task MarkAsRead_marks_single_article_when_articleId_given()
    {
        await _rss.UpsertFeedAsync("TV", new RssFeedConfig { Url = "http://feed/a", Title = "Feed A" });
        _articles.Seed("http://feed/a",
            new RssArticle { FeedUrl = "http://feed/a", Title = "T1", Id = "id1" },
            new RssArticle { FeedUrl = "http://feed/a", Title = "T2", Id = "id2" });

        await Login();
        var response = await _client.PostAsync("/api/v2/rss/markAsRead",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("itemPath", "TV/Feed A"),
                new KeyValuePair<string, string>("articleId", "id1"),
            }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        _articles.IsRead("http://feed/a", "id1").Should().BeTrue();
        _articles.IsRead("http://feed/a", "id2").Should().BeFalse();
    }

    [Fact]
    public async Task MarkAsRead_without_articleId_marks_every_article_on_the_feed()
    {
        await _rss.UpsertFeedAsync("", new RssFeedConfig { Url = "http://feed/a", Title = "Feed A" });
        _articles.Seed("http://feed/a",
            new RssArticle { FeedUrl = "http://feed/a", Title = "T1", Id = "id1" },
            new RssArticle { FeedUrl = "http://feed/a", Title = "T2", Id = "id2" });

        await Login();
        await _client.PostAsync("/api/v2/rss/markAsRead",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("itemPath", "Feed A") }));

        _articles.IsRead("http://feed/a", "id1").Should().BeTrue();
        _articles.IsRead("http://feed/a", "id2").Should().BeTrue();
    }

    [Fact]
    public async Task MarkAsRead_applies_to_every_feed_under_a_folder_path()
    {
        await _rss.UpsertFeedAsync("TV", new RssFeedConfig { Url = "http://feed/a", Title = "A" });
        await _rss.UpsertFeedAsync("TV/Shows", new RssFeedConfig { Url = "http://feed/b", Title = "B" });
        _articles.Seed("http://feed/a", new RssArticle { FeedUrl = "http://feed/a", Title = "a", Id = "a1" });
        _articles.Seed("http://feed/b", new RssArticle { FeedUrl = "http://feed/b", Title = "b", Id = "b1" });

        await Login();
        await _client.PostAsync("/api/v2/rss/markAsRead",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("itemPath", "TV") }));

        _articles.IsRead("http://feed/a", "a1").Should().BeTrue();
        _articles.IsRead("http://feed/b", "b1").Should().BeTrue();
    }

    [Fact]
    public async Task MarkAsRead_returns_404_when_path_resolves_to_nothing()
    {
        await Login();
        var response = await _client.PostAsync("/api/v2/rss/markAsRead",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("itemPath", "DoesNotExist") }));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MarkAsRead_requires_itemPath()
    {
        await Login();
        var response = await _client.PostAsync("/api/v2/rss/markAsRead",
            new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- moveItem ---------------------------------------------------------

    [Fact]
    public async Task MoveItem_relocates_feed_to_new_folder()
    {
        await _rss.UpsertFeedAsync("TV",
            new RssFeedConfig { Url = "http://feed/a", Title = "feedA" });
        await Login();

        var response = await _client.PostAsync("/api/v2/rss/moveItem",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("itemPath", "TV/feedA"),
                new KeyValuePair<string, string>("destPath", "Archive/feedA"),
            }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tree = await _rss.GetTreeAsync();
        tree.Folders.Single(f => f.Name == "TV").Feeds.Should().BeEmpty();
        tree.Folders.Single(f => f.Name == "Archive").Feeds.Single().Url.Should().Be("http://feed/a");
    }

    [Fact]
    public async Task MoveItem_requires_both_paths()
    {
        await Login();

        var response = await _client.PostAsync("/api/v2/rss/moveItem",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("itemPath", "TV/x") }));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MoveItem_returns_404_when_source_missing()
    {
        await Login();

        var response = await _client.PostAsync("/api/v2/rss/moveItem",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("itemPath", "Does/NotExist"),
                new KeyValuePair<string, string>("destPath", "Somewhere"),
            }));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- matchingArticles -------------------------------------------------

    [Fact]
    public async Task MatchingArticles_returns_feed_name_to_titles_map_for_affected_feeds()
    {
        await _rss.UpsertFeedAsync("", new RssFeedConfig { Url = "http://feed/a", Title = "Feed A" });
        await _rss.UpsertFeedAsync("", new RssFeedConfig { Url = "http://feed/b", Title = "Feed B" });
        await _rules.UpsertAsync(new AutoDownloadRule
        {
            Name = "r",
            MustContain = "1080p",
            AffectedFeeds = new[] { "http://feed/a" },
        });

        _articles.Seed("http://feed/a",
            new RssArticle { FeedUrl = "http://feed/a", Title = "Show.1080p" },
            new RssArticle { FeedUrl = "http://feed/a", Title = "Show.720p" });
        _articles.Seed("http://feed/b",
            new RssArticle { FeedUrl = "http://feed/b", Title = "Other.1080p" });

        await Login();

        var json = JsonDocument.Parse(await _client.GetStringAsync("/api/v2/rss/matchingArticles?ruleName=r"))
            .RootElement;

        // Feed A is in AffectedFeeds and has a matching title → listed.
        json.GetProperty("Feed A").EnumerateArray().Select(x => x.GetString())
            .Should().Equal("Show.1080p");
        // Feed B is NOT in AffectedFeeds — excluded even though it has a matching title.
        json.TryGetProperty("Feed B", out _).Should().BeFalse();
    }

    [Fact]
    public async Task MatchingArticles_is_empty_when_rule_has_no_affected_feeds()
    {
        await _rules.UpsertAsync(new AutoDownloadRule { Name = "r", MustContain = "anything" });
        await Login();

        (await _client.GetStringAsync("/api/v2/rss/matchingArticles?ruleName=r"))
            .Should().Be("{}");
    }

    [Fact]
    public async Task MatchingArticles_requires_ruleName_param()
    {
        await Login();
        (await _client.GetAsync("/api/v2/rss/matchingArticles")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MatchingArticles_returns_empty_for_unknown_rule()
    {
        await Login();
        (await _client.GetStringAsync("/api/v2/rss/matchingArticles?ruleName=missing"))
            .Should().Be("{}");
    }

    [Fact]
    public async Task All_routes_require_auth()
    {
        var unauthenticated = new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = _client.BaseAddress,
        };
        var endpoints = new[]
        {
            "/api/v2/rss/items",
            "/api/v2/rss/rules",
        };
        foreach (var ep in endpoints)
        {
            (await unauthenticated.GetAsync(ep)).StatusCode
                .Should().Be(HttpStatusCode.Unauthorized, $"GET {ep}");
        }
    }

    private async Task Login()
    {
        await _client.PostAsync("/api/v2/auth/login",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", "admin"),
                new KeyValuePair<string, string>("password", "adminadmin"),
            }));
    }

    public sealed class InMemorySettings : ISettingsService
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
