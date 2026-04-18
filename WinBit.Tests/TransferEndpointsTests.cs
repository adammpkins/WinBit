using System.Net;
using System.Text.Json;
using FluentAssertions;
using WinBit.Core.BitTorrent;
using WinBit.Core.Logging;
using WinBit.Core.Settings;
using WinBit.Core.WebUi;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class TransferEndpointsTests : IAsyncLifetime
{
    private readonly WebUiService _service;
    private readonly StubTorrentSession _session = new();
    private readonly CookieContainer _cookies = new();
    private HttpClient _client = null!;
    private HttpClientHandler _handler = null!;

    public InMemorySettings Settings { get; } = new();

    public TransferEndpointsTests()
    {
        Settings.Current.WebUi.Enabled = true;
        Settings.Current.WebUi.Port = 0;
        _service = new WebUiService(Settings, new WebUiAuthService(Settings), _session, new NoopLog(), new PeerLogService(), new Helpers.StubCategoryService(), new Helpers.StubTagService(), new Helpers.StubRssService(), new Helpers.StubAutoDownloaderService(), new Helpers.StubRssArticleCache(), new Helpers.StubRssRefresher(), new WinBit.Core.BitTorrent.TorrentCreatorQueue(new WinBit.Core.BitTorrent.TorrentCreatorService()), Helpers.TestPaths.Ambient);
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
    public async Task Info_requires_auth()
    {
        (await _client.GetAsync("/api/v2/transfer/info")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Info_returns_session_stats_with_rate_limits_and_status()
    {
        _session.CurrentStats = new SessionStats(
            GlobalDownloadBps: 1024,
            GlobalUploadBps: 512,
            OpenConnections: 10,
            DhtNodes: 37,
            SessionDownloadedBytes: 1_000_000,
            SessionUploadedBytes: 500_000);

        Settings.Current.Speed.GlobalDownBps = 10_000_000;
        Settings.Current.Speed.GlobalUpBps = 2_000_000;

        await Login();

        var json = JsonDocument.Parse(await _client.GetStringAsync("/api/v2/transfer/info")).RootElement;
        json.GetProperty("dl_info_speed").GetInt64().Should().Be(1024);
        json.GetProperty("up_info_speed").GetInt64().Should().Be(512);
        json.GetProperty("dl_info_data").GetInt64().Should().Be(1_000_000);
        json.GetProperty("up_info_data").GetInt64().Should().Be(500_000);
        json.GetProperty("dht_nodes").GetInt32().Should().Be(37);
        json.GetProperty("dl_rate_limit").GetInt64().Should().Be(10_000_000);
        json.GetProperty("up_rate_limit").GetInt64().Should().Be(2_000_000);
        json.GetProperty("connection_status").GetString().Should().Be("connected");
    }

    [Fact]
    public async Task Info_rate_limits_switch_to_alt_profile_when_AltEnabled()
    {
        Settings.Current.Speed.GlobalDownBps = 10_000_000;
        Settings.Current.Speed.GlobalUpBps = 2_000_000;
        Settings.Current.Speed.AltDownBps = 1_000_000;
        Settings.Current.Speed.AltUpBps = 200_000;
        Settings.Current.Speed.AltEnabled = true;

        await Login();

        var json = JsonDocument.Parse(await _client.GetStringAsync("/api/v2/transfer/info")).RootElement;
        json.GetProperty("dl_rate_limit").GetInt64().Should().Be(1_000_000);
        json.GetProperty("up_rate_limit").GetInt64().Should().Be(200_000);
    }

    [Fact]
    public async Task SpeedLimitsMode_returns_0_or_1_matching_AltEnabled()
    {
        await Login();

        Settings.Current.Speed.AltEnabled = false;
        (await _client.GetStringAsync("/api/v2/transfer/speedLimitsMode")).Should().Be("0");

        Settings.Current.Speed.AltEnabled = true;
        (await _client.GetStringAsync("/api/v2/transfer/speedLimitsMode")).Should().Be("1");
    }

    [Fact]
    public async Task ToggleSpeedLimitsMode_flips_AltEnabled_through_settings()
    {
        await Login();
        Settings.Current.Speed.AltEnabled = false;

        (await _client.PostAsync("/api/v2/transfer/toggleSpeedLimitsMode",
            new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>())))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        Settings.Current.Speed.AltEnabled.Should().BeTrue();

        await _client.PostAsync("/api/v2/transfer/toggleSpeedLimitsMode",
            new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));
        Settings.Current.Speed.AltEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task SetSpeedLimitsMode_writes_mode_explicitly()
    {
        await Login();

        await _client.PostAsync("/api/v2/transfer/setSpeedLimitsMode",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("mode", "1") }));
        Settings.Current.Speed.AltEnabled.Should().BeTrue();

        await _client.PostAsync("/api/v2/transfer/setSpeedLimitsMode",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("mode", "0") }));
        Settings.Current.Speed.AltEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task SetSpeedLimitsMode_rejects_non_integer_mode()
    {
        await Login();
        var response = await _client.PostAsync("/api/v2/transfer/setSpeedLimitsMode",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("mode", "garbage") }));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Mutating_routes_require_auth()
    {
        var unauthenticatedHandler = new HttpClientHandler { UseCookies = false };
        using var anon = new HttpClient(unauthenticatedHandler) { BaseAddress = _client.BaseAddress };
        (await anon.PostAsync("/api/v2/transfer/toggleSpeedLimitsMode",
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
