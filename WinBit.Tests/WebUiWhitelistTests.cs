using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using WinBit.Core.Logging;
using WinBit.Core.Settings;
using WinBit.Core.WebUi;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class WebUiWhitelistTests : IAsyncLifetime
{
    private readonly WebUiService _service;
    public InMemorySettings Settings { get; } = new();
    private HttpClient _anon = null!;

    public WebUiWhitelistTests()
    {
        Settings.Current.WebUi.Enabled = true;
        Settings.Current.WebUi.Port = 0;
        _service = new WebUiService(Settings, new WebUiAuthService(Settings),
            new StubTorrentSession(), new NoopLog(), new PeerLogService(),
            new StubCategoryService(), new StubTagService(),
            new StubRssService(), new StubAutoDownloaderService(),
            new StubRssArticleCache(), new StubRssRefresher(),
            new WinBit.Core.BitTorrent.TorrentCreatorQueue(new WinBit.Core.BitTorrent.TorrentCreatorService()),
            new StubTorrentStateStore(), TestPaths.Ambient);
    }

    public async Task InitializeAsync()
    {
        await _service.StartAsync(CancellationToken.None);
        _anon = new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_service.BoundPort}"),
        };
    }

    public async Task DisposeAsync()
    {
        _anon.Dispose();
        await _service.StopAsync(CancellationToken.None);
    }

    // ---- Service-level unit tests ------------------------------------------

    [Fact]
    public void IsWhitelistedIp_returns_false_when_list_empty()
    {
        var auth = new WebUiAuthService(Settings);
        auth.IsWhitelistedIp(IPAddress.Loopback).Should().BeFalse();
    }

    [Fact]
    public void IsWhitelistedIp_matches_ipv4_subnet()
    {
        Settings.Current.WebUi.WhitelistedSubnets.Add("192.168.1.0/24");
        var auth = new WebUiAuthService(Settings);

        auth.IsWhitelistedIp(IPAddress.Parse("192.168.1.42")).Should().BeTrue();
        auth.IsWhitelistedIp(IPAddress.Parse("10.0.0.1")).Should().BeFalse();
    }

    [Fact]
    public void IsWhitelistedIp_matches_ipv4_mapped_ipv6_via_ipv4_cidr()
    {
        Settings.Current.WebUi.WhitelistedSubnets.Add("127.0.0.0/8");
        var auth = new WebUiAuthService(Settings);

        // Kestrel often delivers localhost as the IPv4-mapped-IPv6 form.
        var mapped = IPAddress.Parse("127.0.0.1").MapToIPv6();
        auth.IsWhitelistedIp(mapped).Should().BeTrue();
    }

    [Fact]
    public void IsWhitelistedIp_tolerates_garbage_cidr_entries()
    {
        Settings.Current.WebUi.WhitelistedSubnets.Add("not a cidr");
        Settings.Current.WebUi.WhitelistedSubnets.Add("192.168.1.0/24");
        var auth = new WebUiAuthService(Settings);

        auth.IsWhitelistedIp(IPAddress.Parse("192.168.1.5")).Should().BeTrue();
    }

    [Fact]
    public void IsWhitelistedIp_null_address_is_never_authorized()
    {
        Settings.Current.WebUi.WhitelistedSubnets.Add("0.0.0.0/0");
        var auth = new WebUiAuthService(Settings);
        auth.IsWhitelistedIp(null).Should().BeFalse();
    }

    // ---- End-to-end through Kestrel ----------------------------------------

    [Fact]
    public async Task Loopback_whitelist_bypasses_SID_cookie()
    {
        Settings.Current.WebUi.WhitelistedSubnets.Add("127.0.0.0/8");

        // No login, no cookie — but request comes from loopback.
        var response = await _anon.GetAsync("/api/v2/torrents/info");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Empty_whitelist_keeps_requiring_cookie()
    {
        Settings.Current.WebUi.WhitelistedSubnets.Clear();

        var response = await _anon.GetAsync("/api/v2/torrents/info");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
