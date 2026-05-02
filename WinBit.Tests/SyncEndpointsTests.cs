using System.Net;
using System.Text.Json;
using FluentAssertions;
using WinBit.Core.BitTorrent;
using WinBit.Core.Categories;
using WinBit.Core.Common;
using WinBit.Core.Logging;
using WinBit.Core.Settings;
using WinBit.Core.WebUi;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class SyncEndpointsTests : IAsyncLifetime
{
    private readonly WebUiService _service;
    private readonly StubTorrentSession _session = new();
    private readonly StubCategoryService _categories = new();
    private readonly StubTagService _tags = new();
    private readonly CookieContainer _cookies = new();
    private HttpClient _client = null!;
    private HttpClientHandler _handler = null!;

    public InMemorySettings Settings { get; } = new();

    public SyncEndpointsTests()
    {
        Settings.Current.WebUi.Enabled = true;
        Settings.Current.WebUi.Port = 0;
        _service = new WebUiService(Settings, new WebUiAuthService(Settings), _session,
            new NoopLog(), new PeerLogService(), _categories, _tags,
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
    public async Task Maindata_requires_auth()
    {
        (await _client.GetAsync("/api/v2/sync/maindata")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Maindata_returns_full_update_with_expected_shape()
    {
        var hash = new string('a', 40);
        _session.SnapshotsByHash[hash] = new TorrentSnapshot
        {
            Id = TorrentId.FromInfoHash(hash),
            State = TorrentState.Downloading,
            Progress = 0.5,
            DownloadSpeedBps = 100,
            UploadSpeedBps = 200,
            Eta = TimeSpan.FromSeconds(30),
        };
        _session.Names[hash] = "t";
        _session.CurrentStats = new SessionStats(
            GlobalDownloadBps: 10_000, GlobalUploadBps: 5_000,
            OpenConnections: 4, DhtNodes: 42,
            SessionDownloadedBytes: 1_000_000, SessionUploadedBytes: 500_000);

        await _categories.UpsertAsync(new Category { Name = "movies", SavePath = @"C:\movies" });
        await _tags.AddAsync("hd");

        await Login();

        var json = JsonDocument.Parse(await _client.GetStringAsync("/api/v2/sync/maindata")).RootElement;

        json.GetProperty("rid").GetInt64().Should().BeGreaterThan(0);
        json.GetProperty("full_update").GetBoolean().Should().BeTrue();

        var torrents = json.GetProperty("torrents");
        torrents.GetProperty(hash).GetProperty("name").GetString().Should().Be("t");
        torrents.GetProperty(hash).GetProperty("state").GetString().Should().Be("downloading");
        torrents.GetProperty(hash).GetProperty("progress").GetDouble().Should().BeApproximately(0.5, 1e-6);
        torrents.GetProperty(hash).GetProperty("eta").GetInt64().Should().Be(30);

        var categories = json.GetProperty("categories");
        categories.GetProperty("movies").GetProperty("savePath").GetString().Should().Be(@"C:\movies");

        json.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).Should().Contain("hd");

        var serverState = json.GetProperty("server_state");
        serverState.GetProperty("dl_info_speed").GetInt64().Should().Be(10_000);
        serverState.GetProperty("up_info_speed").GetInt64().Should().Be(5_000);
        serverState.GetProperty("dht_nodes").GetInt32().Should().Be(42);
        serverState.GetProperty("connection_status").GetString().Should().Be("connected");
        serverState.GetProperty("use_alt_speed_limits").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Rid_increments_between_calls()
    {
        await Login();

        var first = JsonDocument.Parse(await _client.GetStringAsync("/api/v2/sync/maindata?rid=0"))
            .RootElement.GetProperty("rid").GetInt64();
        var second = JsonDocument.Parse(await _client.GetStringAsync($"/api/v2/sync/maindata?rid={first}"))
            .RootElement.GetProperty("rid").GetInt64();

        second.Should().BeGreaterThan(first);
    }

    [Fact]
    public async Task Server_state_uses_alt_profile_when_enabled()
    {
        Settings.Current.Speed.AltEnabled = true;
        Settings.Current.Speed.GlobalDownBps = 9;
        Settings.Current.Speed.GlobalUpBps = 9;
        Settings.Current.Speed.AltDownBps = 1;
        Settings.Current.Speed.AltUpBps = 2;

        await Login();

        var state = JsonDocument.Parse(await _client.GetStringAsync("/api/v2/sync/maindata"))
            .RootElement.GetProperty("server_state");
        state.GetProperty("dl_rate_limit").GetInt64().Should().Be(1);
        state.GetProperty("up_rate_limit").GetInt64().Should().Be(2);
        state.GetProperty("use_alt_speed_limits").GetBoolean().Should().BeTrue();
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
