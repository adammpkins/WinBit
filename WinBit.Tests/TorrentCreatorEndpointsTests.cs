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

public sealed class TorrentCreatorEndpointsTests : IAsyncLifetime
{
    private readonly TempDirectory _temp = new();
    private readonly TorrentCreatorQueue _queue = new(new TorrentCreatorService());
    private readonly WebUiService _service;
    private readonly CookieContainer _cookies = new();
    private HttpClient _client = null!;
    private HttpClientHandler _handler = null!;

    public InMemorySettings Settings { get; } = new();
    private string _sourceDir = "";

    public TorrentCreatorEndpointsTests()
    {
        Settings.Current.WebUi.Enabled = true;
        Settings.Current.WebUi.Port = 0;
        _service = new WebUiService(Settings, new WebUiAuthService(Settings),
            new StubTorrentSession(), new NoopLog(), new PeerLogService(),
            new StubCategoryService(), new StubTagService(),
            new StubRssService(), new StubAutoDownloaderService(),
            new StubRssArticleCache(), new StubRssRefresher(), _queue, TestPaths.Ambient);
    }

    public async Task InitializeAsync()
    {
        _sourceDir = Path.Combine(_temp.Path, "payload");
        Directory.CreateDirectory(_sourceDir);
        File.WriteAllBytes(Path.Combine(_sourceDir, "a.bin"), new byte[512]);
        File.WriteAllBytes(Path.Combine(_sourceDir, "b.bin"), new byte[1024]);

        await _service.StartAsync(CancellationToken.None);
        _handler = new HttpClientHandler { CookieContainer = _cookies, UseCookies = true };
        _client = new HttpClient(_handler) { BaseAddress = new Uri($"http://localhost:{_service.BoundPort}") };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _handler.Dispose();
        await _service.StopAsync(CancellationToken.None);
        await _queue.DisposeAsync();
        _temp.Dispose();
    }

    [Fact]
    public async Task AddTask_queues_a_request_and_returns_task_id_even_when_creation_is_unsupported()
    {
        // The libtorrent binding doesn't yet wrap create_torrent (Phase G of
        // LIBTORRENT_TASKS.md), so TorrentCreatorService's stub throws and the queue
        // settles the task into Failed. The endpoint contract — accept the form,
        // assign a task id, return 200 — must hold so qbittorrent-api clients keep
        // working; the failure surfaces through GET /status.
        await Login();

        var response = await _client.PostAsync("/api/v2/torrentcreator/addTask",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("sourcePath", _sourceDir),
                new KeyValuePair<string, string>("comment", "unit test"),
                new KeyValuePair<string, string>("private", "true"),
            }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var taskId = json.GetProperty("taskID").GetString();

        taskId.Should().NotBeNullOrEmpty();
        await _queue.WaitForTaskAsync(taskId!);

        var status = _queue.GetStatus(taskId!);
        status!.State.Should().Be(TorrentCreatorTaskState.Failed);
        status.Error.Should().Contain("libtorrent");
    }

    [Fact]
    public async Task AddTask_requires_sourcePath()
    {
        await Login();
        var response = await _client.PostAsync("/api/v2/torrentcreator/addTask",
            new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Status_returns_all_tasks_when_taskID_omitted()
    {
        await Login();
        var id1 = _queue.AddTask(new TorrentCreateRequest { SourcePath = _sourceDir, OutputPath = Path.Combine(_temp.Path, "a.torrent") });
        var id2 = _queue.AddTask(new TorrentCreateRequest { SourcePath = _sourceDir, OutputPath = Path.Combine(_temp.Path, "b.torrent") });
        await _queue.WaitForTaskAsync(id1);
        await _queue.WaitForTaskAsync(id2);

        var arr = JsonDocument.Parse(await _client.GetStringAsync("/api/v2/torrentcreator/status")).RootElement;
        arr.GetArrayLength().Should().Be(2);
        arr.EnumerateArray().Select(e => e.GetProperty("taskID").GetString())
            .Should().BeEquivalentTo(new[] { id1, id2 });
    }

    [Fact]
    public async Task Status_single_task_returns_404_when_unknown()
    {
        await Login();
        var response = await _client.GetAsync("/api/v2/torrentcreator/status?taskID=nope");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // DownloadTorrent / DeleteTask happy-path tests previously asserted byte-for-byte
    // equality with the produced .torrent file. With creation stubbed (Phase G of
    // LIBTORRENT_TASKS.md), no file is ever written; the queue settles into Failed and
    // the download endpoint has nothing to serve. The auth coverage and 404-on-unknown
    // tests below exercise the rest of the endpoint contract; full happy-path coverage
    // returns when the libtorrent creator lands.

    [Fact]
    public async Task DownloadTorrent_returns_409_when_task_did_not_finish()
    {
        // The endpoint returns 409 Conflict for any non-Finished task (per qBittorrent's
        // contract). With creation stubbed (Phase G of LIBTORRENT_TASKS.md), tasks settle
        // into Failed and the contract surfaces as 409 — clients can poll /status to see
        // the failure detail.
        await Login();
        var output = Path.Combine(_temp.Path, "out.torrent");
        var taskId = _queue.AddTask(new TorrentCreateRequest { SourcePath = _sourceDir, OutputPath = output });
        await _queue.WaitForTaskAsync(taskId);

        var response = await _client.GetAsync($"/api/v2/torrentcreator/downloadTorrent?taskID={taskId}");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteTask_removes_task_record_even_when_no_output_was_produced()
    {
        await Login();
        var output = Path.Combine(_temp.Path, "delete.torrent");
        var taskId = _queue.AddTask(new TorrentCreateRequest { SourcePath = _sourceDir, OutputPath = output });
        await _queue.WaitForTaskAsync(taskId);
        File.Exists(output).Should().BeFalse(); // nothing was created

        var response = await _client.PostAsync("/api/v2/torrentcreator/deleteTask",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("taskID", taskId) }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        _queue.GetStatus(taskId).Should().BeNull();
    }

    [Fact]
    public async Task DeleteTask_returns_404_when_unknown()
    {
        await Login();
        var response = await _client.PostAsync("/api/v2/torrentcreator/deleteTask",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("taskID", "nope") }));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task All_routes_require_auth()
    {
        using var anon = new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = _client.BaseAddress,
        };

        (await anon.PostAsync("/api/v2/torrentcreator/addTask",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("sourcePath", "x") })))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.GetAsync("/api/v2/torrentcreator/status")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await anon.GetAsync("/api/v2/torrentcreator/downloadTorrent?taskID=x")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsync("/api/v2/torrentcreator/deleteTask",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("taskID", "x") })))
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
