using System.Net;
using FluentAssertions;
using WinBit.Core.Logging;
using WinBit.Core.Settings;
using WinBit.Core.WebUi;
using WinBit.Core.WebUi.Endpoints;
using Xunit;

namespace WinBit.Tests;

public sealed class AuthEndpointsTests : IAsyncLifetime
{
    private readonly WebUiService _service;
    private HttpClient _client = null!;
    private HttpClientHandler _handler = null!;
    private readonly CookieContainer _cookies = new();

    public InMemorySettings Settings { get; } = new();

    public AuthEndpointsTests()
    {
        Settings.Current.WebUi.Enabled = true;
        Settings.Current.WebUi.Port = 0;
        _service = new WebUiService(Settings, new WebUiAuthService(Settings), new Helpers.StubTorrentSession(), new NoopLog(), new PeerLogService(), new Helpers.StubCategoryService(), new Helpers.StubTagService(), new Helpers.StubRssService(), new Helpers.StubAutoDownloaderService(), new Helpers.StubRssArticleCache(), new Helpers.StubRssRefresher(), new WinBit.Core.BitTorrent.TorrentCreatorQueue(new WinBit.Core.BitTorrent.TorrentCreatorService()), Helpers.TestPaths.Ambient);
    }

    public async Task InitializeAsync()
    {
        await _service.StartAsync(CancellationToken.None);
        _handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
        };
        _client = new HttpClient(_handler)
        {
            BaseAddress = new Uri($"http://localhost:{_service.BoundPort}"),
        };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _handler.Dispose();
        await _service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Login_with_default_credentials_returns_Ok_and_sets_SID_cookie()
    {
        var response = await _client.PostAsync("/api/v2/auth/login", FormOf(("username", "admin"), ("password", "adminadmin")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Ok.");
        _cookies.GetCookies(_client.BaseAddress!).Should().Contain(c => c.Name == "SID");
    }

    [Fact]
    public async Task Login_with_bad_credentials_returns_403_Fails()
    {
        var response = await _client.PostAsync("/api/v2/auth/login", FormOf(("username", "admin"), ("password", "wrong")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Be("Fails.");
        _cookies.GetCookies(_client.BaseAddress!).Should().NotContain(c => c.Name == "SID");
    }

    [Fact]
    public async Task Login_respects_configured_PBKDF2_password_hash()
    {
        Settings.Current.WebUi.Username = "alice";
        Settings.Current.WebUi.PasswordHash = PasswordHasher.Hash("s3cret");

        (await _client.PostAsync("/api/v2/auth/login", FormOf(("username", "alice"), ("password", "s3cret"))))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PostAsync("/api/v2/auth/login", FormOf(("username", "alice"), ("password", "adminadmin"))))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Configured_hash_means_default_password_no_longer_works()
    {
        Settings.Current.WebUi.PasswordHash = PasswordHasher.Hash("rotated");

        var response = await _client.PostAsync("/api/v2/auth/login", FormOf(("username", "admin"), ("password", "adminadmin")));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Logout_clears_the_session_cookie()
    {
        await _client.PostAsync("/api/v2/auth/login", FormOf(("username", "admin"), ("password", "adminadmin")));
        _cookies.GetCookies(_client.BaseAddress!).Should().Contain(c => c.Name == "SID");

        var response = await _client.PostAsync("/api/v2/auth/logout", new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sidCookie = _cookies.GetCookies(_client.BaseAddress!)["SID"];
        (sidCookie is null || sidCookie.Expired || string.IsNullOrEmpty(sidCookie.Value)).Should().BeTrue();
    }

    private static FormUrlEncodedContent FormOf(params (string key, string value)[] fields) =>
        new(fields.Select(f => new KeyValuePair<string, string>(f.key, f.value)));

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
