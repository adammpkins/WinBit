using FluentAssertions;
using Microsoft.Extensions.Options;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Hosting;
using WinBit.Core.Logging;
using WinBit.Core.Persistence;
using WinBit.Core.Settings;
using WinBit.Core.Sharing;
using WinBit.Core.WatchedFolders;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class WatchedFolderServiceTests
{
    [Fact]
    public async Task Upsert_and_GetAll_round_trip_through_json()
    {
        using var temp = new TempDirectory();
        var paths = NewPaths(temp);

        var service = new WatchedFolderService(paths, new RecordingSession(), new NoopLog());
        await service.UpsertAsync(new WatchedFolder { Path = temp.Path, SavePath = null });
        var list = await service.GetAllAsync();

        list.Should().ContainSingle().Which.Path.Should().Be(temp.Path);

        // Reload with a new service — ensures JSON persistence works.
        var fresh = new WatchedFolderService(paths, new RecordingSession(), new NoopLog());
        (await fresh.GetAllAsync()).Should().ContainSingle().Which.Path.Should().Be(temp.Path);
    }

    [Fact]
    public async Task Remove_drops_folder_and_persists()
    {
        using var temp = new TempDirectory();
        var paths = NewPaths(temp);
        var service = new WatchedFolderService(paths, new RecordingSession(), new NoopLog());
        await service.UpsertAsync(new WatchedFolder { Path = temp.Path });

        await service.RemoveAsync(temp.Path);

        (await service.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Scan_adds_every_torrent_file_with_configured_save_path()
    {
        using var temp = new TempDirectory();
        var watched = Path.Combine(temp.Path, "watch");
        var save = Path.Combine(temp.Path, "downloads");
        Directory.CreateDirectory(watched);
        Directory.CreateDirectory(save);
        File.WriteAllBytes(Path.Combine(watched, "a.torrent"), new byte[] { 0x01 });
        File.WriteAllBytes(Path.Combine(watched, "b.torrent"), new byte[] { 0x02 });

        var session = new RecordingSession();
        var service = new WatchedFolderService(NewPaths(temp), session, new NoopLog());

        await service.ScanAsync(new WatchedFolder
        {
            Path = watched,
            SavePath = save,
            StartImmediately = false,
            DeleteSourceOnAdd = true,
        });

        session.AddCalls.Should().HaveCount(2);
        session.AddCalls.Select(p => p.SavePath).Should().AllBe(save);
        session.AddCalls.Select(p => p.StartImmediately).Should().AllBeEquivalentTo(false);
        Directory.GetFiles(watched, "*.torrent").Should().BeEmpty();
    }

    [Fact]
    public async Task Scan_defaults_save_path_to_watched_folder_when_null()
    {
        using var temp = new TempDirectory();
        var watched = Path.Combine(temp.Path, "watch");
        Directory.CreateDirectory(watched);
        File.WriteAllBytes(Path.Combine(watched, "a.torrent"), new byte[] { 0x01 });

        var session = new RecordingSession();
        var service = new WatchedFolderService(NewPaths(temp), session, new NoopLog());

        await service.ScanAsync(new WatchedFolder { Path = watched, SavePath = null });

        session.AddCalls.Should().ContainSingle().Which.SavePath.Should().Be(watched);
    }

    [Fact]
    public async Task Scan_preserves_source_when_add_fails()
    {
        using var temp = new TempDirectory();
        var watched = Path.Combine(temp.Path, "watch");
        Directory.CreateDirectory(watched);
        var file = Path.Combine(watched, "a.torrent");
        File.WriteAllBytes(file, new byte[] { 0x01 });

        var session = new RecordingSession { Outcome = Result<TorrentId>.Failure("boom") };
        var service = new WatchedFolderService(NewPaths(temp), session, new NoopLog());

        await service.ScanAsync(new WatchedFolder { Path = watched, DeleteSourceOnAdd = true });

        File.Exists(file).Should().BeTrue();
    }

    [Fact]
    public async Task Scan_recursive_picks_up_nested_torrents()
    {
        using var temp = new TempDirectory();
        var watched = Path.Combine(temp.Path, "watch");
        var sub = Path.Combine(watched, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(watched, "a.torrent"), new byte[] { 0x01 });
        File.WriteAllBytes(Path.Combine(sub, "b.torrent"), new byte[] { 0x02 });

        var session = new RecordingSession();
        var service = new WatchedFolderService(NewPaths(temp), session, new NoopLog());

        await service.ScanAsync(new WatchedFolder { Path = watched, Recursive = true });

        session.AddCalls.Should().HaveCount(2);
    }

    [Fact]
    public async Task Start_scans_existing_files_and_watcher_picks_up_new_drops()
    {
        using var temp = new TempDirectory();
        var watched = Path.Combine(temp.Path, "watch");
        Directory.CreateDirectory(watched);
        File.WriteAllBytes(Path.Combine(watched, "preexisting.torrent"), new byte[] { 0x01 });

        var session = new RecordingSession();
        var service = new WatchedFolderService(NewPaths(temp), session, new NoopLog());
        await service.UpsertAsync(new WatchedFolder { Path = watched });

        await service.StartAsync(CancellationToken.None);
        session.AddCalls.Should().HaveCount(1);

        // Drop a second torrent and wait for the debounced scan to fire.
        File.WriteAllBytes(Path.Combine(watched, "new.torrent"), new byte[] { 0x02 });
        await WaitForAsync(() => session.AddCalls.Count >= 2, TimeSpan.FromSeconds(3));

        session.AddCalls.Count.Should().BeGreaterOrEqualTo(2);

        await service.StopAsync(CancellationToken.None);
    }

    private static Paths NewPaths(TempDirectory temp)
    {
        var opts = Options.Create(new WinBitCoreOptions { DataRoot = temp.Path });
        return new Paths(opts);
    }

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }
            await Task.Delay(50);
        }
    }

    private sealed class RecordingSession : ITorrentSessionService
    {
        public List<AddTorrentParams> AddCalls { get; } = new();
        public Result<TorrentId> Outcome { get; set; } = Result<TorrentId>.Success(TorrentId.FromInfoHash(new string('a', 40)));

        public Task<Result<TorrentId>> AddAsync(AddTorrentParams parameters, CancellationToken ct = default)
        {
            AddCalls.Add(parameters);
            return Task.FromResult(Outcome);
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
        public Task<Result> SetGlobalSpeedLimitsAsync(long downloadBps, long uploadBps, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> SetPortForwardingAsync(bool enabled, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> SetEncryptionModeAsync(EncryptionMode mode, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> SetPeerDiscoveryAsync(bool dht, bool pex, bool lsd, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public ShareLimitSnapshot? GetShareLimitSnapshot(TorrentId id) => null;
        public Task<IReadOnlyList<PeerInfo>> GetPeersAsync(TorrentId id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PeerInfo>>(Array.Empty<PeerInfo>());
        public Task<IReadOnlyList<TrackerInfo>> GetTrackersAsync(TorrentId id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TrackerInfo>>(Array.Empty<TrackerInfo>());
        public SessionStats GetSessionStats() => default;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopLog : ILogService
    {
        public IReadOnlyList<LogEntry> GetMessages(long afterId = -1, LogSeverity filter = LogSeverity.All) => Array.Empty<LogEntry>();
        public void Write(string message, LogSeverity severity = LogSeverity.Normal) { }
        public event EventHandler<LogEntry>? MessageLogged { add { } remove { } }
    }
}
