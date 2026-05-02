using System.Net;
using System.Text.Json;
using FluentAssertions;
using WinBit.Core.Logging;
using WinBit.Core.Settings;
using WinBit.Core.WebUi;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class LogEndpointsTests : IAsyncLifetime
{
    private readonly WebUiService _service;
    private readonly LogService _log = new();
    private readonly PeerLogService _peerLog = new();
    private readonly CookieContainer _cookies = new();
    private HttpClient _client = null!;
    private HttpClientHandler _handler = null!;

    public InMemorySettings Settings { get; } = new();

    public LogEndpointsTests()
    {
        Settings.Current.WebUi.Enabled = true;
        Settings.Current.WebUi.Port = 0;
        _service = new WebUiService(Settings, new WebUiAuthService(Settings),
            new StubTorrentSession(), _log, _peerLog,
            new StubCategoryService(), new StubTagService(),
            new StubRssService(), new StubAutoDownloaderService(), new StubRssArticleCache(), new StubRssRefresher(), new WinBit.Core.BitTorrent.TorrentCreatorQueue(new WinBit.Core.BitTorrent.TorrentCreatorService()), new StubTorrentStateStore(), TestPaths.Ambient);
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
    public async Task Main_requires_auth()
    {
        (await _client.GetAsync("/api/v2/log/main")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Main_returns_every_entry_with_default_filters()
    {
        _log.Write("normal message", LogSeverity.Normal);
        _log.Write("info message", LogSeverity.Info);
        _log.Write("warn message", LogSeverity.Warning);
        _log.Write("crit message", LogSeverity.Critical);

        await Login();

        var arr = JsonDocument.Parse(await _client.GetStringAsync("/api/v2/log/main")).RootElement;
        var byMessage = arr.EnumerateArray()
            .ToDictionary(e => e.GetProperty("message").GetString() ?? "", e => e.GetProperty("type").GetInt32());

        // Flag bit values 1 / 2 / 4 / 8 match qBittorrent's MsgType.
        byMessage["normal message"].Should().Be((int)LogSeverity.Normal);
        byMessage["info message"].Should().Be((int)LogSeverity.Info);
        byMessage["warn message"].Should().Be((int)LogSeverity.Warning);
        byMessage["crit message"].Should().Be((int)LogSeverity.Critical);

        // timestamp is serialized as milliseconds since epoch.
        arr.EnumerateArray().Should().OnlyContain(e => e.GetProperty("timestamp").GetInt64() > 0);
    }

    [Fact]
    public async Task Main_filters_by_severity_flag_query_params()
    {
        _log.Write("n", LogSeverity.Normal);
        _log.Write("w", LogSeverity.Warning);

        await Login();

        var arr = JsonDocument.Parse(await _client.GetStringAsync(
            "/api/v2/log/main?normal=false&warning=true&info=false&critical=false")).RootElement;
        arr.GetArrayLength().Should().Be(1);
        arr[0].GetProperty("message").GetString().Should().Be("w");
    }

    [Fact]
    public async Task Main_last_known_id_excludes_older_entries()
    {
        // Read the current tail first so we're resilient to WebUiService.Start's
        // own log line (it writes "Web UI listening on port …" before tests hit the endpoint).
        await Login();
        var baseline = JsonDocument.Parse(await _client.GetStringAsync("/api/v2/log/main")).RootElement;
        var baselineMaxId = baseline.GetArrayLength() == 0
            ? -1L
            : baseline.EnumerateArray().Max(e => e.GetProperty("id").GetInt64());

        _log.Write("first");
        var firstId = _log.GetMessages(baselineMaxId).Single().Id;
        _log.Write("second");
        _log.Write("third");

        var tail = JsonDocument.Parse(await _client.GetStringAsync(
            $"/api/v2/log/main?last_known_id={firstId}")).RootElement;

        tail.EnumerateArray().Select(e => e.GetProperty("message").GetString()).Should().Equal("second", "third");
    }

    [Fact]
    public async Task Peers_requires_auth()
    {
        (await _client.GetAsync("/api/v2/log/peers")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Peers_returns_every_entry_with_blocked_true()
    {
        _peerLog.Record("1.2.3.4:6881", "IP filter");
        _peerLog.Record("5.6.7.8:6882", "manual ban");
        await Login();

        var arr = JsonDocument.Parse(await _client.GetStringAsync("/api/v2/log/peers")).RootElement;
        arr.GetArrayLength().Should().Be(2);
        arr.EnumerateArray().Should().OnlyContain(e => e.GetProperty("blocked").GetBoolean());
        arr[0].GetProperty("ip").GetString().Should().Be("1.2.3.4:6881");
        arr[0].GetProperty("reason").GetString().Should().Be("IP filter");
    }

    [Fact]
    public async Task Peers_last_known_id_excludes_older_entries()
    {
        _peerLog.Record("1.1.1.1:1", "a");
        _peerLog.Record("2.2.2.2:2", "b");
        _peerLog.Record("3.3.3.3:3", "c");

        await Login();

        var all = JsonDocument.Parse(await _client.GetStringAsync("/api/v2/log/peers")).RootElement;
        var firstId = all[0].GetProperty("id").GetInt64();

        var tail = JsonDocument.Parse(await _client.GetStringAsync(
            $"/api/v2/log/peers?last_known_id={firstId}")).RootElement;

        tail.GetArrayLength().Should().Be(2);
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
}
