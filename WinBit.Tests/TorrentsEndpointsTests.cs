using System.Net;
using System.Text.Json;
using FluentAssertions;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Logging;
using WinBit.Core.Settings;
using WinBit.Core.WebUi;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class TorrentsEndpointsTests : IAsyncLifetime
{
    private readonly WebUiService _service;
    private readonly StubTorrentSession _session = new();
    private readonly CookieContainer _cookies = new();
    private HttpClient _client = null!;
    private HttpClientHandler _handler = null!;

    public InMemorySettings Settings { get; } = new();

    public TorrentsEndpointsTests()
    {
        Settings.Current.WebUi.Enabled = true;
        Settings.Current.WebUi.Port = 0;
        _service = new WebUiService(Settings, new WebUiAuthService(Settings), _session, new NoopLog(), new PeerLogService(), new Helpers.StubCategoryService(), new Helpers.StubTagService(), new Helpers.StubRssService(), new Helpers.StubAutoDownloaderService(), new Helpers.StubRssArticleCache(), new Helpers.StubRssRefresher(), new WinBit.Core.BitTorrent.TorrentCreatorQueue(new WinBit.Core.BitTorrent.TorrentCreatorService()), new Helpers.StubTorrentStateStore(), Helpers.TestPaths.Ambient);
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

    // --- /torrents/info ------------------------------------------------------

    [Fact]
    public async Task Info_requires_auth()
    {
        (await _client.GetAsync("/api/v2/torrents/info")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Info_returns_serialized_snapshots_when_authenticated()
    {
        var hash = new string('a', 40);
        _session.SnapshotsByHash[hash] = new TorrentSnapshot
        {
            Id = TorrentId.FromInfoHash(hash),
            State = TorrentState.Downloading,
            Progress = 0.25,
            BytesDownloaded = 1024,
            BytesUploaded = 2048,
            DownloadSpeedBps = 512,
            UploadSpeedBps = 128,
            Ratio = 2.0,
            Eta = TimeSpan.FromSeconds(60),
            Seeds = 3,
            Peers = 5,
        };
        _session.Names[hash] = "my torrent";
        _session.SavePaths[hash] = @"C:\dl";

        await Login();

        var body = await _client.GetStringAsync("/api/v2/torrents/info");
        var arr = JsonDocument.Parse(body).RootElement;
        arr.GetArrayLength().Should().Be(1);

        var row = arr[0];
        row.GetProperty("hash").GetString().Should().Be(hash);
        row.GetProperty("name").GetString().Should().Be("my torrent");
        row.GetProperty("state").GetString().Should().Be("downloading");
        row.GetProperty("progress").GetDouble().Should().BeApproximately(0.25, 1e-6);
        row.GetProperty("dlspeed").GetInt64().Should().Be(512);
        row.GetProperty("upspeed").GetInt64().Should().Be(128);
        row.GetProperty("save_path").GetString().Should().Be(@"C:\dl");
        row.GetProperty("eta").GetInt64().Should().Be(60);
    }

    [Fact]
    public async Task Info_maps_state_to_qBittorrent_vocabulary()
    {
        _session.SnapshotsByHash["h1"] = new TorrentSnapshot
        {
            Id = TorrentId.FromInfoHash("h1"),
            State = TorrentState.Seeding,
            Eta = null,
        };
        await Login();

        var arr = JsonDocument.Parse(await _client.GetStringAsync("/api/v2/torrents/info")).RootElement;
        arr[0].GetProperty("state").GetString().Should().Be("uploading");
        arr[0].GetProperty("eta").GetInt64().Should().Be(8_640_000L);
    }

    // --- /torrents/add -------------------------------------------------------

    [Fact]
    public async Task Add_requires_auth()
    {
        var response = await _client.PostAsync("/api/v2/torrents/add",
            new MultipartFormDataContent { { new StringContent("http://x/y"), "urls" } });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Add_urls_field_dispatches_one_AddAsync_per_newline_separated_entry()
    {
        Settings.Current.Downloads.DefaultSavePath = @"C:\dl";
        await Login();

        var content = new MultipartFormDataContent
        {
            { new StringContent("http://x/a.torrent\nmagnet:?xt=urn:btih:abc\n"), "urls" },
        };
        (await _client.PostAsync("/api/v2/torrents/add", content)).StatusCode.Should().Be(HttpStatusCode.OK);

        _session.AddCalls.Should().HaveCount(2);
        _session.AddCalls.Select(p => p.Source).Should().Equal(
            "http://x/a.torrent", "magnet:?xt=urn:btih:abc");
        _session.AddCalls.Should().OnlyContain(p => p.SavePath == @"C:\dl");
    }

    [Fact]
    public async Task Add_uses_savepath_form_field_over_default()
    {
        Settings.Current.Downloads.DefaultSavePath = @"C:\default";
        await Login();

        var content = new MultipartFormDataContent
        {
            { new StringContent("http://x/a.torrent"), "urls" },
            { new StringContent(@"D:\override"), "savepath" },
        };
        await _client.PostAsync("/api/v2/torrents/add", content);

        _session.AddCalls.Should().ContainSingle()
            .Which.SavePath.Should().Be(@"D:\override");
    }

    [Fact]
    public async Task Add_returns_400_when_no_save_path_is_resolvable()
    {
        Settings.Current.Downloads.DefaultSavePath = null;
        await Login();

        var content = new MultipartFormDataContent
        {
            { new StringContent("http://x/a.torrent"), "urls" },
        };
        var response = await _client.PostAsync("/api/v2/torrents/add", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _session.AddCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_paused_and_skip_checking_flow_into_AddTorrentParams()
    {
        Settings.Current.Downloads.DefaultSavePath = @"C:\dl";
        await Login();

        var content = new MultipartFormDataContent
        {
            { new StringContent("http://x/a.torrent"), "urls" },
            { new StringContent("true"), "paused" },
            { new StringContent("true"), "skip_checking" },
            { new StringContent("true"), "sequentialDownload" },
            { new StringContent("true"), "firstLastPiecePrio" },
        };
        await _client.PostAsync("/api/v2/torrents/add", content);

        var call = _session.AddCalls.Should().ContainSingle().Subject;
        call.StartImmediately.Should().BeFalse();
        call.SkipHashCheck.Should().BeTrue();
        call.Sequential.Should().BeTrue();
        call.FirstAndLastPiecePriority.Should().BeTrue();
    }

    [Fact]
    public async Task Add_category_and_tags_flow_through()
    {
        Settings.Current.Downloads.DefaultSavePath = @"C:\dl";
        await Login();

        var content = new MultipartFormDataContent
        {
            { new StringContent("http://x/a.torrent"), "urls" },
            { new StringContent("movies"), "category" },
            { new StringContent("hd, 1080p,  archival"), "tags" },
        };
        await _client.PostAsync("/api/v2/torrents/add", content);

        var call = _session.AddCalls.Should().ContainSingle().Subject;
        call.Category.Should().Be("movies");
        call.Tags.Should().Equal("hd", "1080p", "archival");
    }

    [Fact]
    public async Task Add_file_upload_is_spooled_to_temp_path_and_passed_to_AddAsync()
    {
        Settings.Current.Downloads.DefaultSavePath = @"C:\dl";
        await Login();

        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var fileContent = new ByteArrayContent(payload);
        var content = new MultipartFormDataContent
        {
            { fileContent, "torrents", "example.torrent" },
        };
        var response = await _client.PostAsync("/api/v2/torrents/add", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var call = _session.AddCalls.Should().ContainSingle().Subject;
        call.Source.Should().EndWith(".torrent");
        // After the endpoint returns, the temp file should have been cleaned up.
        File.Exists(call.Source).Should().BeFalse();
    }

    // --- control ops ---------------------------------------------------------

    [Theory]
    [InlineData("pause", "pause")]
    [InlineData("resume", "resume")]
    [InlineData("recheck", "recheck")]
    public async Task Control_routes_dispatch_to_session_for_each_hash(string route, string op)
    {
        await Login();
        var hashes = $"{new string('a', 40)}|{new string('b', 40)}";
        var response = await _client.PostAsync($"/api/v2/torrents/{route}",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("hashes", hashes) }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _session.ControlCalls.Where(c => c.Op == op).Should().HaveCount(2);
    }

    [Fact]
    public async Task Control_supports_hashes_all_pseudo_target()
    {
        _session.SnapshotsByHash[new string('a', 40)] = new TorrentSnapshot { Id = TorrentId.FromInfoHash(new string('a', 40)) };
        _session.SnapshotsByHash[new string('b', 40)] = new TorrentSnapshot { Id = TorrentId.FromInfoHash(new string('b', 40)) };

        await Login();
        await _client.PostAsync("/api/v2/torrents/pause",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("hashes", "all") }));

        _session.ControlCalls.Where(c => c.Op == "pause").Should().HaveCount(2);
    }

    [Fact]
    public async Task Delete_honours_deleteFiles_flag()
    {
        await Login();
        var hash = new string('a', 40);
        await _client.PostAsync("/api/v2/torrents/delete",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", hash),
                new KeyValuePair<string, string>("deleteFiles", "true"),
            }));

        _session.RemoveCalls.Should().ContainSingle()
            .Which.Should().Be((TorrentId.FromInfoHash(hash), true));
    }

    [Fact]
    public async Task Delete_defaults_deleteFiles_to_false()
    {
        await Login();
        var hash = new string('a', 40);
        await _client.PostAsync("/api/v2/torrents/delete",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("hashes", hash) }));

        _session.RemoveCalls.Should().ContainSingle().Which.DeleteContent.Should().BeFalse();
    }

    [Fact]
    public async Task Control_routes_require_auth()
    {
        // No login — POST without SID cookie.
        var unauthenticatedHandler = new HttpClientHandler { UseCookies = false };
        using var anon = new HttpClient(unauthenticatedHandler) { BaseAddress = _client.BaseAddress };
        var response = await anon.PostAsync("/api/v2/torrents/pause",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("hashes", "x") }));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task Login()
    {
        var response = await _client.PostAsync("/api/v2/auth/login",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", "admin"),
                new KeyValuePair<string, string>("password", "adminadmin"),
            }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
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
