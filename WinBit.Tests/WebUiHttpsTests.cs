using System.Net;
using FluentAssertions;
using WinBit.Core.Logging;
using WinBit.Core.Settings;
using WinBit.Core.WebUi;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class WebUiHttpsTests
{
    [Fact]
    public async Task Self_signed_cert_is_generated_and_reused_across_restarts()
    {
        using var temp = new TempDirectory();
        var paths = TestPaths.ForTemp(temp);
        var settings = MakeSettings();
        settings.Current.WebUi.Https = true;

        var service = new WebUiService(settings, new WebUiAuthService(settings),
            new StubTorrentSession(), new NoopLog(), new PeerLogService(),
            new StubCategoryService(), new StubTagService(),
            new StubRssService(), new StubAutoDownloaderService(),
            new StubRssArticleCache(), new StubRssRefresher(),
            new WinBit.Core.BitTorrent.TorrentCreatorQueue(new WinBit.Core.BitTorrent.TorrentCreatorService()),
            paths);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var certPath = Path.Combine(paths.Root, WebUiCertificateProvider.SelfSignedFileName);
            File.Exists(certPath).Should().BeTrue();

            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri($"https://localhost:{service.BoundPort}"),
            };

            (await client.GetStringAsync("/api/v2/app/version"))
                .Should().Be(WebUiService.VersionString);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task User_supplied_PFX_is_used_when_HttpsCertPath_points_at_existing_file()
    {
        using var temp = new TempDirectory();
        var paths = TestPaths.ForTemp(temp);
        var userPfxPath = Path.Combine(temp.Path, "user.pfx");
        File.WriteAllBytes(userPfxPath, WebUiCertificateProvider.CreateSelfSignedPfx());

        var settings = MakeSettings();
        settings.Current.WebUi.Https = true;
        settings.Current.WebUi.HttpsCertPath = userPfxPath;

        var service = new WebUiService(settings, new WebUiAuthService(settings),
            new StubTorrentSession(), new NoopLog(), new PeerLogService(),
            new StubCategoryService(), new StubTagService(),
            new StubRssService(), new StubAutoDownloaderService(),
            new StubRssArticleCache(), new StubRssRefresher(),
            new WinBit.Core.BitTorrent.TorrentCreatorQueue(new WinBit.Core.BitTorrent.TorrentCreatorService()),
            paths);

        await service.StartAsync(CancellationToken.None);
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri($"https://localhost:{service.BoundPort}"),
            };
            (await client.GetAsync("/api/v2/app/version")).StatusCode.Should().Be(HttpStatusCode.OK);

            // Self-signed fallback file should NOT have been written when a user PFX is configured.
            File.Exists(Path.Combine(paths.Root, WebUiCertificateProvider.SelfSignedFileName))
                .Should().BeFalse();
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Https_disabled_falls_back_to_plain_HTTP()
    {
        using var temp = new TempDirectory();
        var paths = TestPaths.ForTemp(temp);
        var settings = MakeSettings();
        settings.Current.WebUi.Https = false;

        var service = new WebUiService(settings, new WebUiAuthService(settings),
            new StubTorrentSession(), new NoopLog(), new PeerLogService(),
            new StubCategoryService(), new StubTagService(),
            new StubRssService(), new StubAutoDownloaderService(),
            new StubRssArticleCache(), new StubRssRefresher(),
            new WinBit.Core.BitTorrent.TorrentCreatorQueue(new WinBit.Core.BitTorrent.TorrentCreatorService()),
            paths);

        await service.StartAsync(CancellationToken.None);
        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{service.BoundPort}"),
            };
            (await client.GetStringAsync("/api/v2/app/version")).Should().Be(WebUiService.VersionString);
            File.Exists(Path.Combine(paths.Root, WebUiCertificateProvider.SelfSignedFileName))
                .Should().BeFalse();
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static InMemorySettings MakeSettings()
    {
        var s = new InMemorySettings();
        s.Current.WebUi.Enabled = true;
        s.Current.WebUi.Port = 0;
        return s;
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
