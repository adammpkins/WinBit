using System.Net;
using System.Text.Json;
using FluentAssertions;
using WinBit.Core.Logging;
using WinBit.Core.Settings;
using WinBit.Core.WebUi;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class SearchEndpointsTests : IAsyncLifetime
{
    private readonly WebUiService _service;
    private readonly CookieContainer _cookies = new();
    private HttpClient _client = null!;
    private HttpClientHandler _handler = null!;

    public InMemorySettings Settings { get; } = new();

    public SearchEndpointsTests()
    {
        Settings.Current.WebUi.Enabled = true;
        Settings.Current.WebUi.Port = 0;
        _service = new WebUiService(Settings, new WebUiAuthService(Settings),
            new StubTorrentSession(), new NoopLog(), new PeerLogService(),
            new StubCategoryService(), new StubTagService(),
            new StubRssService(), new StubAutoDownloaderService(),
            new StubRssArticleCache(), new StubRssRefresher(),
            new WinBit.Core.BitTorrent.TorrentCreatorQueue(new WinBit.Core.BitTorrent.TorrentCreatorService()), TestPaths.Ambient);
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
    public async Task Plugins_returns_empty_array_when_authenticated()
    {
        await Login();
        var body = await _client.GetStringAsync("/api/v2/search/plugins");
        body.Should().Be("[]");
    }

    [Fact]
    public async Task Status_returns_empty_array()
    {
        await Login();
        (await _client.GetStringAsync("/api/v2/search/status")).Should().Be("[]");
    }

    [Fact]
    public async Task Results_returns_stopped_envelope_with_zero_rows()
    {
        await Login();
        var json = JsonDocument.Parse(await _client.GetStringAsync("/api/v2/search/results")).RootElement;
        json.GetProperty("status").GetString().Should().Be("Stopped");
        json.GetProperty("total").GetInt32().Should().Be(0);
        json.GetProperty("results").GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("delete")]
    [InlineData("installPlugin")]
    [InlineData("uninstallPlugin")]
    [InlineData("enablePlugin")]
    [InlineData("updatePlugins")]
    public async Task Mutating_routes_return_409_with_unavailable_body(string verb)
    {
        await Login();
        var response = await _client.PostAsync($"/api/v2/search/{verb}",
            new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Be("Search service unavailable");
    }

    [Fact]
    public async Task Read_routes_require_auth()
    {
        using var anon = new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = _client.BaseAddress,
        };
        (await anon.GetAsync("/api/v2/search/plugins")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await anon.GetAsync("/api/v2/search/status")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await anon.GetAsync("/api/v2/search/results")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mutating_routes_require_auth()
    {
        using var anon = new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = _client.BaseAddress,
        };
        (await anon.PostAsync("/api/v2/search/start",
            new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>())))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
