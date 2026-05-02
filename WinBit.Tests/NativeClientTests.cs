using System.Net;
using FluentAssertions;
using WinBit.Core.Logging;
using WinBit.Core.Settings;
using WinBit.Core.WebUi;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

/// <summary>
/// Integration tests for WebUI asset serving: Vue SPA at root, qBittorrent UI at
/// /qbittorrent/, and API routes taking precedence over static assets.
/// </summary>
public sealed class NativeClientTests : IAsyncLifetime
{
    private readonly WebUiService _service;
    private HttpClient _client = null!;

    public InMemorySettings Settings { get; } = new();

    public NativeClientTests()
    {
        Settings.Current.WebUi.Enabled = true;
        Settings.Current.WebUi.Port = 0;
        _service = new WebUiService(Settings, new WebUiAuthService(Settings),
            new StubTorrentSession(), new NoopLog(), new PeerLogService(),
            new StubCategoryService(), new StubTagService(),
            new StubRssService(), new StubAutoDownloaderService(),
            new StubRssArticleCache(), new StubRssRefresher(),
            new WinBit.Core.BitTorrent.TorrentCreatorQueue(new WinBit.Core.BitTorrent.TorrentCreatorService()),
            new StubTorrentStateStore(), TestPaths.Ambient);
    }

    public async Task InitializeAsync()
    {
        await _service.StartAsync(CancellationToken.None);
        _client = new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = new Uri($"http://localhost:{_service.BoundPort}"),
        };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Root_serves_vue_spa_index()
    {
        var body = await _client.GetStringAsync("/");
        body.Should().Contain("<title>WinBit</title>");
        body.Should().Contain("<div id=\"app\"></div>");
    }

    [Fact]
    public async Task Unknown_paths_fall_back_to_spa_index_for_client_side_routing()
    {
        var body = await _client.GetStringAsync("/some/deep/route");
        body.Should().Contain("<title>WinBit</title>");
    }

    [Fact]
    public async Task Hashed_assets_are_served_with_immutable_cache_header()
    {
        // index.html lists at least one hashed JS asset — fetch it and confirm cache header
        var indexBody = await _client.GetStringAsync("/");
        var match = System.Text.RegularExpressions.Regex.Match(indexBody, @"src=""\./(assets/[^""]+\.js)""");
        match.Success.Should().BeTrue("index.html must reference a hashed JS asset");

        var assetRes = await _client.GetAsync("/" + match.Groups[1].Value);
        assetRes.StatusCode.Should().Be(HttpStatusCode.OK);
        assetRes.Headers.CacheControl!.MaxAge.Should().BeGreaterThan(TimeSpan.FromDays(300));
    }

    [Fact]
    public async Task QBittorrent_UI_reachable_under_explicit_prefix()
    {
        (await _client.GetStringAsync("/qbittorrent/"))
            .Should().Contain("qBittorrent WebUI");
    }

    [Fact]
    public async Task Api_routes_take_precedence_over_the_SPA()
    {
        (await _client.GetStringAsync("/api/v2/app/version"))
            .Should().Be(WebUiService.VersionString);
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
