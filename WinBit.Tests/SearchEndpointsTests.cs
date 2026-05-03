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
            new WinBit.Core.BitTorrent.TorrentCreatorQueue(new WinBit.Core.BitTorrent.TorrentCreatorService()),
            new StubTorrentStateStore(), TestPaths.Ambient, new StubSearchPluginHost());
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
    public async Task Plugins_returns_empty_array_when_no_plugins_registered()
    {
        await Login();
        var body = await _client.GetStringAsync("/api/v2/search/plugins");
        body.Should().Be("[]");
    }

    [Fact]
    public async Task Status_returns_empty_array_when_no_jobs()
    {
        await Login();
        (await _client.GetStringAsync("/api/v2/search/status")).Should().Be("[]");
    }

    [Fact]
    public async Task Results_returns_404_for_unknown_job_id()
    {
        await Login();
        var response = await _client.GetAsync("/api/v2/search/results?id=99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Start_creates_job_and_returns_id()
    {
        await Login();
        var response = await _client.PostAsync("/api/v2/search/start",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("pattern", "ubuntu")]));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("id").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Start_requires_pattern_field()
    {
        await Login();
        var response = await _client.PostAsync("/api/v2/search/start",
            new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Stop_and_delete_return_ok_for_unknown_id()
    {
        await Login();
        var stop = await _client.PostAsync("/api/v2/search/stop",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("id", "99999")]));
        stop.StatusCode.Should().Be(HttpStatusCode.OK);

        var delete = await _client.PostAsync("/api/v2/search/delete",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("id", "99999")]));
        delete.StatusCode.Should().Be(HttpStatusCode.OK);
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
