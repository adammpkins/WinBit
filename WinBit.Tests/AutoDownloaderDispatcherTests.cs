using FluentAssertions;
using Microsoft.Extensions.Options;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Hosting;
using WinBit.Core.Logging;
using WinBit.Core.Persistence;
using WinBit.Core.Rss;
using WinBit.Core.Settings;
using WinBit.Core.Sharing;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class AutoDownloaderDispatcherTests
{
    private const string DefaultSavePath = "/var/winbit/downloads";

    [Fact]
    public async Task Match_adds_torrent_with_configured_save_path()
    {
        var ctx = new TestContext();
        ctx.Settings.Current.Rss.AutoDownloader = true;
        ctx.Settings.Current.Downloads.DefaultSavePath = DefaultSavePath;

        await ctx.Rules.UpsertAsync(new AutoDownloadRule
        {
            Name = "r",
            MustContain = "1080p",
        });

        await ctx.Dispatcher.ProcessArticlesAsync("http://f", new[]
        {
            Article("Show.S01E05.1080p", "https://x/s01e05.torrent"),
            Article("Trailer.720p", "https://x/trailer.torrent"),
        }, CancellationToken.None);

        ctx.Session.AddCalls.Should().ContainSingle()
            .Which.Source.Should().Be("https://x/s01e05.torrent");
        ctx.Session.AddCalls[0].SavePath.Should().Be(DefaultSavePath);
    }

    [Fact]
    public async Task AutoDownloader_toggle_off_disables_adds()
    {
        var ctx = new TestContext();
        ctx.Settings.Current.Rss.AutoDownloader = false;
        ctx.Settings.Current.Downloads.DefaultSavePath = DefaultSavePath;
        await ctx.Rules.UpsertAsync(new AutoDownloadRule { Name = "r", MustContain = "anything" });

        await ctx.Dispatcher.ProcessArticlesAsync("http://f", new[]
        {
            Article("anything goes", "https://x/t.torrent"),
        }, CancellationToken.None);

        ctx.Session.AddCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_default_save_path_skips_adds_with_warning()
    {
        var ctx = new TestContext();
        ctx.Settings.Current.Rss.AutoDownloader = true;
        ctx.Settings.Current.Downloads.DefaultSavePath = null;
        await ctx.Rules.UpsertAsync(new AutoDownloadRule { Name = "r" });

        await ctx.Dispatcher.ProcessArticlesAsync("http://f", new[] { Article("t", "https://x/t.torrent") },
            CancellationToken.None);

        ctx.Session.AddCalls.Should().BeEmpty();
        ctx.Log.Messages.Should().Contain(m => m.Contains("no default save path"));
    }

    [Fact]
    public async Task Disabled_rule_is_skipped()
    {
        var ctx = new TestContext();
        ctx.Settings.Current.Rss.AutoDownloader = true;
        ctx.Settings.Current.Downloads.DefaultSavePath = DefaultSavePath;
        await ctx.Rules.UpsertAsync(new AutoDownloadRule { Name = "r", Enabled = false });

        await ctx.Dispatcher.ProcessArticlesAsync("http://f", new[] { Article("t", "https://x/t.torrent") },
            CancellationToken.None);

        ctx.Session.AddCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Article_without_torrent_url_logs_and_skips()
    {
        var ctx = new TestContext();
        ctx.Settings.Current.Rss.AutoDownloader = true;
        ctx.Settings.Current.Downloads.DefaultSavePath = DefaultSavePath;
        await ctx.Rules.UpsertAsync(new AutoDownloadRule { Name = "r" });

        await ctx.Dispatcher.ProcessArticlesAsync("http://f", new[] { Article("t", torrentUrl: null) },
            CancellationToken.None);

        ctx.Session.AddCalls.Should().BeEmpty();
        ctx.Log.Messages.Should().Contain(m => m.Contains("no torrent URL"));
    }

    [Fact]
    public async Task Smart_filter_match_persists_episode_tag_back_to_rule()
    {
        var ctx = new TestContext();
        ctx.Settings.Current.Rss.AutoDownloader = true;
        ctx.Settings.Current.Downloads.DefaultSavePath = DefaultSavePath;
        await ctx.Rules.UpsertAsync(new AutoDownloadRule
        {
            Name = "smart",
            SmartFilter = true,
        });

        await ctx.Dispatcher.ProcessArticlesAsync("http://f", new[]
        {
            Article("Show.S01E05.1080p", "https://x/s01e05.torrent"),
        }, CancellationToken.None);

        var reloaded = (await ctx.Rules.GetAsync("smart"))!;
        reloaded.PreviouslyMatchedEpisodes.Should().Contain("01x05");
        reloaded.LastMatchUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Smart_filter_suppresses_second_same_episode_article_in_same_batch()
    {
        var ctx = new TestContext();
        ctx.Settings.Current.Rss.AutoDownloader = true;
        ctx.Settings.Current.Downloads.DefaultSavePath = DefaultSavePath;
        await ctx.Rules.UpsertAsync(new AutoDownloadRule { Name = "smart", SmartFilter = true });

        await ctx.Dispatcher.ProcessArticlesAsync("http://f", new[]
        {
            Article("Show.S01E05.720p", "https://x/s01e05-720.torrent"),
            Article("Show.S01E05.1080p", "https://x/s01e05-1080.torrent"),
        }, CancellationToken.None);

        ctx.Session.AddCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Multiple_rules_can_match_the_same_article()
    {
        var ctx = new TestContext();
        ctx.Settings.Current.Rss.AutoDownloader = true;
        ctx.Settings.Current.Downloads.DefaultSavePath = DefaultSavePath;
        await ctx.Rules.UpsertAsync(new AutoDownloadRule { Name = "a", MustContain = "1080p" });
        await ctx.Rules.UpsertAsync(new AutoDownloadRule { Name = "b", MustContain = "Show" });

        await ctx.Dispatcher.ProcessArticlesAsync("http://f", new[]
        {
            Article("Show.S01E05.1080p", "https://x/t.torrent"),
        }, CancellationToken.None);

        ctx.Session.AddCalls.Should().HaveCount(2);
    }

    [Fact]
    public async Task Empty_article_batch_is_a_noop()
    {
        var ctx = new TestContext();
        ctx.Settings.Current.Rss.AutoDownloader = true;
        ctx.Settings.Current.Downloads.DefaultSavePath = DefaultSavePath;
        await ctx.Rules.UpsertAsync(new AutoDownloadRule { Name = "r" });

        await ctx.Dispatcher.ProcessArticlesAsync("http://f", Array.Empty<RssArticle>(), CancellationToken.None);

        ctx.Session.AddCalls.Should().BeEmpty();
    }

    private static RssArticle Article(string title, string? torrentUrl, DateTime? date = null) =>
        new()
        {
            FeedUrl = "http://f",
            Title = title,
            TorrentUrl = torrentUrl,
            PublishedUtc = date ?? new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Utc),
        };

    private sealed class TestContext : IDisposable
    {
        public TempDirectory Temp { get; }
        public AutoDownloaderService Rules { get; }
        public InMemorySettings Settings { get; }
        public RecordingSession Session { get; }
        public CapturingLog Log { get; }
        public AutoDownloaderDispatcher Dispatcher { get; }
        public RssRefreshLoop Loop { get; }

        public TestContext()
        {
            Temp = new TempDirectory();
            var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = Temp.Path }));
            Rules = new AutoDownloaderService(paths);
            Settings = new InMemorySettings();
            Session = new RecordingSession();
            Log = new CapturingLog();
            var rss = new RssService(paths);
            Loop = new RssRefreshLoop(rss, Settings, (_, _) => Task.FromResult<string?>(null), Log);
            Dispatcher = new AutoDownloaderDispatcher(Loop, Rules, Settings, Session, Log);
        }

        public void Dispose() => Temp.Dispose();
    }

    private sealed class InMemorySettings : ISettingsService
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

    private sealed class CapturingLog : ILogService
    {
        public List<string> Messages { get; } = new();
        public IReadOnlyList<LogEntry> GetMessages(long afterId = -1, LogSeverity filter = LogSeverity.All) => Array.Empty<LogEntry>();
        public void Write(string message, LogSeverity severity = LogSeverity.Normal) => Messages.Add(message);
        public event EventHandler<LogEntry>? MessageLogged { add { } remove { } }
    }

    private sealed class RecordingSession : ITorrentSessionService
    {
        public List<AddTorrentParams> AddCalls { get; } = new();

        public Task<Result<TorrentId>> AddAsync(AddTorrentParams parameters, CancellationToken ct = default)
        {
            AddCalls.Add(parameters);
            return Task.FromResult(Result<TorrentId>.Success(TorrentId.FromInfoHash(new string('a', 40))));
        }

        public bool IsRunning => true;
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<TorrentId> Torrents => Array.Empty<TorrentId>();
        public event EventHandler<IReadOnlyList<TorrentSnapshot>>? TorrentUpdated { add { } remove { } }
        public void CaptureAndPublishSnapshots() { }
        public IReadOnlyList<TorrentSnapshot> GetSnapshots() => Array.Empty<TorrentSnapshot>();
        public Task PersistFastResumeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<Result> SetNameAsync(TorrentId id, string name, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> RemoveAsync(TorrentId id, bool deleteContent = false, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> PauseAsync(TorrentId id, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> ResumeAsync(TorrentId id, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> ForceRecheckAsync(TorrentId id, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> ForceReannounceAsync(TorrentId id, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public string? GetMagnetUri(TorrentId id) => null;
        public string? GetSavePath(TorrentId id) => null;
        public string? GetName(TorrentId id) => null;
        public IReadOnlyList<string> GetTrackerHosts(TorrentId id) => Array.Empty<string>();
        public (long DownloadBps, long UploadBps)? GetSpeedLimits(TorrentId id) => null;
        public Task<Result> SetSpeedLimitsAsync(TorrentId id, long? downloadBps, long? uploadBps, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> SetSuperSeedingAsync(TorrentId id, bool enabled, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> SetSequentialDownloadAsync(TorrentId id, bool enabled, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task RenameFileAsync(TorrentId id, int fileIndex, string newRelativePath, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetFilePriorityAsync(TorrentId id, int fileIndex, FileDownloadPriority priority, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Result> SetGlobalSpeedLimitsAsync(long downloadBps, long uploadBps, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> SetPortForwardingAsync(bool enabled, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> SetEncryptionModeAsync(EncryptionMode mode, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> SetPeerDiscoveryAsync(bool dht, bool pex, bool lsd, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public ShareLimitSnapshot? GetShareLimitSnapshot(TorrentId id) => null;
        public Task<IReadOnlyList<PeerInfo>> GetPeersAsync(TorrentId id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PeerInfo>>(Array.Empty<PeerInfo>());
        public Task<IReadOnlyList<TrackerInfo>> GetTrackersAsync(TorrentId id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TrackerInfo>>(Array.Empty<TrackerInfo>());
        public Task<IReadOnlyList<TorrentFileEntry>> GetTorrentFilesAsync(TorrentId id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TorrentFileEntry>>(Array.Empty<TorrentFileEntry>());
        public Task<IReadOnlyList<bool>> GetPiecesAsync(TorrentId id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<bool>>(Array.Empty<bool>());
        public SessionStats GetSessionStats() => default;
        public Task<TorrentDetailInfo?> GetTorrentDetailAsync(TorrentId id, CancellationToken ct = default) =>
            Task.FromResult<TorrentDetailInfo?>(null);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
