using System.Net;
using System.Text.Json;
using FluentAssertions;
using WinBit.Core.Logging;
using WinBit.Core.Settings;
using WinBit.Core.WebUi;
using WinBit.Core.WebUi.Endpoints;
using Xunit;

namespace WinBit.Tests;

public sealed class AppEndpointsTests : IAsyncLifetime
{
    private readonly WebUiService _service;
    private HttpClient _client = null!;
    private readonly CookieContainer _cookies = new();
    private HttpClientHandler _handler = null!;

    public AppEndpointsTests()
    {
        var settings = new InMemorySettings();
        settings.Current.WebUi.Enabled = true;
        settings.Current.WebUi.Port = 0;
        settings.Current.Downloads.DefaultSavePath = @"C:\winbit\downloads";
        _service = new WebUiService(settings, new WebUiAuthService(settings), new Helpers.StubTorrentSession(), new NoopLog(), new PeerLogService(), new Helpers.StubCategoryService(), new Helpers.StubTagService(), new Helpers.StubRssService(), new Helpers.StubAutoDownloaderService(), new Helpers.StubRssArticleCache(), new Helpers.StubRssRefresher(), new WinBit.Core.BitTorrent.TorrentCreatorQueue(new WinBit.Core.BitTorrent.TorrentCreatorService()), new Helpers.StubTorrentStateStore(), Helpers.TestPaths.Ambient, new Helpers.StubSearchPluginHost());
        Settings = settings;
    }

    public InMemorySettings Settings { get; }

    public async Task InitializeAsync()
    {
        await _service.StartAsync(CancellationToken.None);
        _handler = new HttpClientHandler { CookieContainer = _cookies, UseCookies = true };
        _client = new HttpClient(_handler) { BaseAddress = new Uri($"http://localhost:{_service.BoundPort}") };
        await Login();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _handler.Dispose();
        await _service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Version_returns_plaintext_body()
    {
        var response = await _client.GetAsync("/api/v2/app/version");
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        (await response.Content.ReadAsStringAsync()).Should().Be(AppEndpoints.Version);
    }

    [Fact]
    public async Task WebApiVersion_returns_plaintext_body()
    {
        var body = await _client.GetStringAsync("/api/v2/app/webapiVersion");
        body.Should().Be(AppEndpoints.WebApiVersion);
    }

    [Fact]
    public async Task BuildInfo_returns_json_object_with_expected_keys()
    {
        var response = await _client.GetAsync("/api/v2/app/buildInfo");
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        foreach (var key in new[] { "qt", "libtorrent", "boost", "openssl", "zlib", "bitness", "platform" })
        {
            json.TryGetProperty(key, out _).Should().BeTrue($"buildInfo should expose '{key}'");
        }
        json.GetProperty("libtorrent").GetString().Should().Contain("libtorrent-rasterbar");
        json.GetProperty("platform").GetString().Should().Be("windows");
        json.GetProperty("bitness").GetInt32().Should().BeOneOf(32, 64);
    }

    [Fact]
    public async Task DefaultSavePath_reflects_settings()
    {
        var body = await _client.GetStringAsync("/api/v2/app/defaultSavePath");
        body.Should().Be(@"C:\winbit\downloads");
    }

    [Fact]
    public async Task DefaultSavePath_returns_empty_when_unset()
    {
        Settings.Current.Downloads.DefaultSavePath = null;
        var body = await _client.GetStringAsync("/api/v2/app/defaultSavePath");
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task TransfersGridHiddenColumns_roundtrips_through_setPreferences_and_getPreferences()
    {
        // POST hidden columns via form-urlencoded json= param (qBittorrent compat path).
        var payload = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("json",
                """{"transfers_hidden_columns":["size","ratio"]}"""),
        });
        var postResponse = await _client.PostAsync("/api/v2/app/setPreferences", payload);
        postResponse.EnsureSuccessStatusCode();

        // GET preferences and assert the persisted list is exactly what was sent.
        var getResponse = await _client.GetAsync("/api/v2/app/preferences");
        getResponse.EnsureSuccessStatusCode();

        var root = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync()).RootElement;
        root.TryGetProperty("transfers_hidden_columns", out var hiddenProp).Should().BeTrue();
        var hidden = hiddenProp.EnumerateArray().Select(e => e.GetString()).ToList();
        hidden.Should().BeEquivalentTo(new[] { "size", "ratio" }, options => options.WithStrictOrdering());
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
