using System.Net;
using FluentAssertions;
using WinBit.Core.Logging;
using WinBit.Core.Settings;
using WinBit.Core.WebUi;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class QBittorrentAssetsTests : IAsyncLifetime
{
    private readonly WebUiService _service;
    private readonly CookieContainer _cookies = new();
    private HttpClient _client = null!;
    private HttpClientHandler _handler = null!;

    public InMemorySettings Settings { get; } = new();

    public QBittorrentAssetsTests()
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
        _handler = new HttpClientHandler { CookieContainer = _cookies, UseCookies = true };
        _client = new HttpClient(_handler) { BaseAddress = new Uri($"http://localhost:{_service.BoundPort}") };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _handler.Dispose();
        await _service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Rewrite_strips_translation_markers_and_substitutes_tokens()
    {
        const string input = "<title>QBT_TR(qBittorrent WebUI)QBT_TR[CONTEXT=Login]</title>" +
            "<script src=\"scripts/login.js?locale=${LANG}&v=${CACHEID}\"></script>";
        var output = QBittorrentAssets.Rewrite(input);

        output.Should().NotContain("QBT_TR(");
        output.Should().NotContain("QBT_TR[");
        output.Should().Contain("<title>qBittorrent WebUI</title>");
        output.Should().Contain($"locale={QBittorrentAssets.Language}");
        output.Should().Contain($"v={QBittorrentAssets.CacheId}");
    }

    [Fact]
    public async Task Anonymous_root_serves_the_public_login_page()
    {
        var response = await _client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("<title>qBittorrent WebUI</title>");
        body.Should().NotContain("QBT_TR(");
        body.Should().NotContain("${CACHEID}");
    }

    [Fact]
    public async Task Authenticated_root_serves_the_private_admin_page()
    {
        await Login();
        var body = await _client.GetStringAsync("/");
        // The private index contains the main admin toolbar; the public login page does not.
        body.Should().Contain("mochaToolbar");
        body.Should().NotContain("<form id=\"loginform\">");
    }

    [Fact]
    public async Task Asset_paths_serve_with_correct_content_type()
    {
        var response = await _client.GetAsync("/images/qbittorrent-tray.svg");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/svg+xml");
    }

    [Fact]
    public async Task Api_routes_still_dispatch_to_endpoints_not_the_asset_middleware()
    {
        var body = await _client.GetStringAsync("/api/v2/app/version");
        body.Should().Be(WebUiService.VersionString);
    }

    [Fact]
    public async Task Unknown_path_returns_404()
    {
        var response = await _client.GetAsync("/this/does/not/exist.txt");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
