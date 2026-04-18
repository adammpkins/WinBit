using System.Net;
using FluentAssertions;
using WinBit.Core.Logging;
using WinBit.Core.Settings;
using WinBit.Core.WebUi;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

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
            TestPaths.Ambient);
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
    public async Task Native_client_is_always_reachable_at_winbit_prefix()
    {
        var body = await _client.GetStringAsync("/winbit/");
        body.Should().Contain("<title>WinBit</title>");
        body.Should().Contain("app.js");
    }

    [Fact]
    public async Task Native_client_serves_css_and_js_assets()
    {
        (await _client.GetAsync("/winbit/style.css")).StatusCode.Should().Be(HttpStatusCode.OK);
        var js = await _client.GetAsync("/winbit/app.js");
        js.StatusCode.Should().Be(HttpStatusCode.OK);
        js.Content.Headers.ContentType!.MediaType.Should().BeOneOf("text/javascript", "application/javascript");
    }

    [Fact]
    public async Task UseNativeClient_flag_controls_root_url()
    {
        Settings.Current.WebUi.UseNativeClient = false;
        (await _client.GetStringAsync("/"))
            .Should().Contain("qBittorrent WebUI");

        Settings.Current.WebUi.UseNativeClient = true;
        (await _client.GetStringAsync("/"))
            .Should().Contain("<title>WinBit</title>");
    }

    [Fact]
    public async Task QBittorrent_UI_still_reachable_under_explicit_prefix_even_when_native_is_default()
    {
        Settings.Current.WebUi.UseNativeClient = true;
        (await _client.GetStringAsync("/qbittorrent/"))
            .Should().Contain("qBittorrent WebUI");
    }

    [Fact]
    public async Task Api_routes_still_take_precedence_over_the_SPA()
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
