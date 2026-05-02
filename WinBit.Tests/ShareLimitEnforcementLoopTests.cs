using FluentAssertions;
using Microsoft.Extensions.Options;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Hosting;
using WinBit.Core.Logging;
using WinBit.Core.Persistence;
using WinBit.Core.Settings;
using WinBit.Core.Sharing;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class ShareLimitEnforcementLoopTests
{
    private static readonly TorrentId IdA = TorrentId.FromInfoHash("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

    private sealed record LoopHarness(
        ShareLimitEnforcementLoop Loop,
        FakeSessionService Session,
        ShareLimitOverrideService Overrides,
        InMemorySettingsService Settings,
        FakeTimeProvider Time,
        TempDirectory Temp) : IDisposable
    {
        public void Dispose() => Temp.Dispose();
    }

    private static LoopHarness BuildHarness()
    {
        var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));
        var overrides = new ShareLimitOverrideService(paths);
        var session = new FakeSessionService();
        var settings = new InMemorySettingsService();
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var loop = new ShareLimitEnforcementLoop(session, overrides, settings, new NoopLog(), time);
        // Warm overrides cache so Effective() works synchronously in-loop.
        _ = overrides.GetAllAsync().GetAwaiter().GetResult();
        return new LoopHarness(loop, session, overrides, settings, time, temp);
    }

    [Fact]
    public async Task Finished_torrent_over_ratio_is_paused()
    {
        using var h = BuildHarness();
        h.Settings.Current.BitTorrent.GlobalShareLimits = new ShareLimits
        {
            RatioLimit = 2.0,
            Action = ShareLimitAction.Stop,
        };
        h.Session.SetSnapshot(IdA, new ShareLimitSnapshot(
            Id: IdA, State: TorrentState.Seeding, IsFinished: true, IsForced: false,
            IsStopped: false, IsSuperSeeding: false, Ratio: 3.0, BytesUploaded: 1000));

        await h.Loop.TickAsync(CancellationToken.None);

        h.Session.PauseCalls.Should().ContainSingle().Which.Should().Be(IdA);
        h.Session.RemoveCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveWithContent_action_dispatches_with_delete_content_flag()
    {
        using var h = BuildHarness();
        h.Settings.Current.BitTorrent.GlobalShareLimits = new ShareLimits
        {
            RatioLimit = 2.0,
            Action = ShareLimitAction.RemoveWithContent,
        };
        h.Session.SetSnapshot(IdA, new ShareLimitSnapshot(
            Id: IdA, State: TorrentState.Seeding, IsFinished: true, IsForced: false,
            IsStopped: false, IsSuperSeeding: false, Ratio: 3.0, BytesUploaded: 1000));

        await h.Loop.TickAsync(CancellationToken.None);

        h.Session.RemoveCalls.Should().ContainSingle()
            .Which.Should().Be((IdA, true));
        h.Session.PauseCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task EnableSuperSeeding_action_engages_super_seeding_once()
    {
        using var h = BuildHarness();
        h.Settings.Current.BitTorrent.GlobalShareLimits = new ShareLimits
        {
            RatioLimit = 2.0,
            Action = ShareLimitAction.EnableSuperSeeding,
        };
        h.Session.SetSnapshot(IdA, new ShareLimitSnapshot(
            Id: IdA, State: TorrentState.Seeding, IsFinished: true, IsForced: false,
            IsStopped: false, IsSuperSeeding: false, Ratio: 3.0, BytesUploaded: 1000));

        await h.Loop.TickAsync(CancellationToken.None);

        // First tick flips the flag.
        h.Session.SuperSeedingCalls.Should().ContainSingle()
            .Which.Should().Be((IdA, true));

        // Subsequent ticks don't re-dispatch — evaluator short-circuits once the torrent is already
        // super-seeding (matches qBittorrent's `if (!torrent->superSeeding())` guard).
        await h.Loop.TickAsync(CancellationToken.None);
        h.Session.SuperSeedingCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Unfinished_torrent_is_left_alone()
    {
        using var h = BuildHarness();
        h.Settings.Current.BitTorrent.GlobalShareLimits = new ShareLimits
        {
            RatioLimit = 2.0,
            Action = ShareLimitAction.Stop,
        };
        h.Session.SetSnapshot(IdA, new ShareLimitSnapshot(
            Id: IdA, State: TorrentState.Downloading, IsFinished: false, IsForced: false,
            IsStopped: false, IsSuperSeeding: false, Ratio: 100.0, BytesUploaded: 0));

        await h.Loop.TickAsync(CancellationToken.None);

        h.Session.PauseCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Seeding_time_accumulates_across_ticks()
    {
        using var h = BuildHarness();
        h.Settings.Current.BitTorrent.GlobalShareLimits = new ShareLimits
        {
            SeedingTimeLimit = TimeSpan.FromMinutes(5),
            Action = ShareLimitAction.Stop,
        };
        h.Session.SetSnapshot(IdA, new ShareLimitSnapshot(
            Id: IdA, State: TorrentState.Seeding, IsFinished: true, IsForced: false,
            IsStopped: false, IsSuperSeeding: false, Ratio: 0.1, BytesUploaded: 1000));

        // t=0: establish baseline, nothing accumulated yet.
        await h.Loop.TickAsync(CancellationToken.None);
        h.Session.PauseCalls.Should().BeEmpty();

        // t=+3min: 3 minutes accumulated, still under 5-min cap.
        h.Time.Advance(TimeSpan.FromMinutes(3));
        await h.Loop.TickAsync(CancellationToken.None);
        h.Session.PauseCalls.Should().BeEmpty();

        // t=+6min: 6 minutes accumulated, crosses the cap.
        h.Time.Advance(TimeSpan.FromMinutes(3));
        await h.Loop.TickAsync(CancellationToken.None);
        h.Session.PauseCalls.Should().ContainSingle().Which.Should().Be(IdA);
    }

    [Fact]
    public async Task Inactive_seeding_time_grows_while_bytes_uploaded_unchanged()
    {
        using var h = BuildHarness();
        h.Settings.Current.BitTorrent.GlobalShareLimits = new ShareLimits
        {
            InactiveSeedingTimeLimit = TimeSpan.FromMinutes(10),
            Action = ShareLimitAction.Stop,
        };
        h.Session.SetSnapshot(IdA, new ShareLimitSnapshot(
            Id: IdA, State: TorrentState.Seeding, IsFinished: true, IsForced: false,
            IsStopped: false, IsSuperSeeding: false, Ratio: 0.1, BytesUploaded: 1000));

        await h.Loop.TickAsync(CancellationToken.None);

        // 11 minutes pass with no upload activity.
        h.Time.Advance(TimeSpan.FromMinutes(11));
        await h.Loop.TickAsync(CancellationToken.None);

        h.Session.PauseCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Upload_activity_resets_the_inactivity_clock()
    {
        using var h = BuildHarness();
        h.Settings.Current.BitTorrent.GlobalShareLimits = new ShareLimits
        {
            InactiveSeedingTimeLimit = TimeSpan.FromMinutes(10),
            Action = ShareLimitAction.Stop,
        };
        h.Session.SetSnapshot(IdA, new ShareLimitSnapshot(
            Id: IdA, State: TorrentState.Seeding, IsFinished: true, IsForced: false,
            IsStopped: false, IsSuperSeeding: false, Ratio: 0.1, BytesUploaded: 1000));
        await h.Loop.TickAsync(CancellationToken.None);

        // 8 min pass — still under cap — and a byte is uploaded, which resets the clock.
        h.Time.Advance(TimeSpan.FromMinutes(8));
        h.Session.SetSnapshot(IdA, h.Session.Snapshots[IdA] with { BytesUploaded = 2000 });
        await h.Loop.TickAsync(CancellationToken.None);

        // Another 8 min pass. Without the reset, we'd be at 16 min total; with it, only 8.
        h.Time.Advance(TimeSpan.FromMinutes(8));
        await h.Loop.TickAsync(CancellationToken.None);

        h.Session.PauseCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Override_wins_over_global_limits()
    {
        using var h = BuildHarness();
        h.Settings.Current.BitTorrent.GlobalShareLimits = new ShareLimits
        {
            RatioLimit = 10.0,
            Action = ShareLimitAction.Stop,
        };
        await h.Overrides.UpsertAsync(new PerTorrentShareLimitOverride
        {
            Id = IdA,
            RatioLimit = 1.0,
            Action = ShareLimitAction.Remove,
        });
        h.Session.SetSnapshot(IdA, new ShareLimitSnapshot(
            Id: IdA, State: TorrentState.Seeding, IsFinished: true, IsForced: false,
            IsStopped: false, IsSuperSeeding: false, Ratio: 1.5, BytesUploaded: 1000));

        await h.Loop.TickAsync(CancellationToken.None);

        h.Session.PauseCalls.Should().BeEmpty();
        h.Session.RemoveCalls.Should().ContainSingle()
            .Which.Should().Be((IdA, false));
    }

    [Fact]
    public async Task Trackers_for_removed_torrents_are_pruned()
    {
        using var h = BuildHarness();
        h.Settings.Current.BitTorrent.GlobalShareLimits = new ShareLimits
        {
            SeedingTimeLimit = TimeSpan.FromMinutes(5),
            Action = ShareLimitAction.Stop,
        };
        h.Session.SetSnapshot(IdA, new ShareLimitSnapshot(
            Id: IdA, State: TorrentState.Seeding, IsFinished: true, IsForced: false,
            IsStopped: false, IsSuperSeeding: false, Ratio: 0.1, BytesUploaded: 1000));
        await h.Loop.TickAsync(CancellationToken.None);

        // Remove the torrent from the session.
        h.Session.RemoveSnapshot(IdA);
        h.Time.Advance(TimeSpan.FromHours(1));
        await h.Loop.TickAsync(CancellationToken.None);

        // Re-add the same id — tracker should start fresh, not claim the hour of elapsed time.
        h.Session.SetSnapshot(IdA, new ShareLimitSnapshot(
            Id: IdA, State: TorrentState.Seeding, IsFinished: true, IsForced: false,
            IsStopped: false, IsSuperSeeding: false, Ratio: 0.1, BytesUploaded: 1000));
        await h.Loop.TickAsync(CancellationToken.None);
        h.Time.Advance(TimeSpan.FromMinutes(3));
        await h.Loop.TickAsync(CancellationToken.None);

        h.Session.PauseCalls.Should().BeEmpty("accumulated seeding time should be ~3 min, under the 5-min cap");
    }

    [Fact]
    public async Task Stopped_torrent_does_not_accumulate_seeding_time()
    {
        using var h = BuildHarness();
        h.Settings.Current.BitTorrent.GlobalShareLimits = new ShareLimits
        {
            SeedingTimeLimit = TimeSpan.FromMinutes(5),
            Action = ShareLimitAction.Stop,
        };
        h.Session.SetSnapshot(IdA, new ShareLimitSnapshot(
            Id: IdA, State: TorrentState.Stopped, IsFinished: true, IsForced: false,
            IsStopped: true, IsSuperSeeding: false, Ratio: 0.1, BytesUploaded: 1000));

        await h.Loop.TickAsync(CancellationToken.None);
        h.Time.Advance(TimeSpan.FromHours(1));
        await h.Loop.TickAsync(CancellationToken.None);

        h.Session.PauseCalls.Should().BeEmpty();
    }

    private sealed class FakeSessionService : ITorrentSessionService
    {
        public Dictionary<TorrentId, ShareLimitSnapshot> Snapshots { get; } = new();
        public List<TorrentId> PauseCalls { get; } = new();
        public List<(TorrentId Id, bool DeleteContent)> RemoveCalls { get; } = new();
        public List<(TorrentId Id, bool Enabled)> SuperSeedingCalls { get; } = new();

        public void SetSnapshot(TorrentId id, ShareLimitSnapshot snapshot) => Snapshots[id] = snapshot;
        public void RemoveSnapshot(TorrentId id) => Snapshots.Remove(id);

        public bool IsRunning => true;
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public IReadOnlyList<TorrentId> Torrents => Snapshots.Keys.ToArray();
        public event EventHandler<IReadOnlyList<TorrentSnapshot>>? TorrentUpdated { add { } remove { } }

        public void CaptureAndPublishSnapshots() { }
        public IReadOnlyList<TorrentSnapshot> GetSnapshots() => Array.Empty<TorrentSnapshot>();
        public Task PersistFastResumeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<Result<TorrentId>> AddAsync(AddTorrentParams parameters, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<Result> SetNameAsync(TorrentId id, string name, CancellationToken ct = default) => Task.FromResult(Result.Success());

        public Task<Result> RemoveAsync(TorrentId id, bool deleteContent = false, CancellationToken ct = default)
        {
            RemoveCalls.Add((id, deleteContent));
            Snapshots.Remove(id);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> PauseAsync(TorrentId id, CancellationToken ct = default)
        {
            PauseCalls.Add(id);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> ResumeAsync(TorrentId id, CancellationToken ct = default)
            => Task.FromResult(Result.Success());
        public Task<Result> ForceRecheckAsync(TorrentId id, CancellationToken ct = default)
            => Task.FromResult(Result.Success());
        public Task<Result> ForceReannounceAsync(TorrentId id, CancellationToken ct = default)
            => Task.FromResult(Result.Success());

        public string? GetMagnetUri(TorrentId id) => null;
        public string? GetSavePath(TorrentId id) => null;
        public string? GetName(TorrentId id) => null;
        public IReadOnlyList<string> GetTrackerHosts(TorrentId id) => Array.Empty<string>();
        public (long DownloadBps, long UploadBps)? GetSpeedLimits(TorrentId id) => null;
        public Task<Result> SetSpeedLimitsAsync(TorrentId id, long? downloadBps, long? uploadBps, CancellationToken ct = default)
            => Task.FromResult(Result.Success());

        public Task<Result> SetGlobalSpeedLimitsAsync(long downloadBps, long uploadBps, CancellationToken ct = default)
            => Task.FromResult(Result.Success());

        public Task<Result> SetPortForwardingAsync(bool enabled, CancellationToken ct = default)
            => Task.FromResult(Result.Success());

        public Task<Result> SetEncryptionModeAsync(WinBit.Core.Settings.EncryptionMode mode, CancellationToken ct = default)
            => Task.FromResult(Result.Success());

        public Task<Result> SetPeerDiscoveryAsync(bool dht, bool pex, bool lsd, CancellationToken ct = default)
            => Task.FromResult(Result.Success());

        public Task<Result> SetSuperSeedingAsync(TorrentId id, bool enabled, CancellationToken ct = default)
        {
            SuperSeedingCalls.Add((id, enabled));
            if (Snapshots.TryGetValue(id, out var snap))
            {
                Snapshots[id] = snap with { IsSuperSeeding = enabled };
            }
            return Task.FromResult(Result.Success());
        }

        public Task<Result> SetSequentialDownloadAsync(TorrentId id, bool enabled, CancellationToken ct = default)
            => Task.FromResult(Result.Success());

        public Task<Result> SetFirstLastPiecePriorityAsync(TorrentId id, bool enable, CancellationToken ct = default)
            => Task.FromResult(Result.Success());

        public Task<Result> ForceStartTorrentAsync(TorrentId id, bool forceStart, CancellationToken ct = default)
            => Task.FromResult(Result.Success());

        public Task<Result> RelocateTorrentAsync(TorrentId id, string newPath, CancellationToken ct = default)
            => Task.FromResult(Result.Success());

        public Task RenameFileAsync(TorrentId id, int fileIndex, string newRelativePath, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SetFilePriorityAsync(TorrentId id, int fileIndex, FileDownloadPriority priority, CancellationToken ct = default)
            => Task.CompletedTask;

        public ShareLimitSnapshot? GetShareLimitSnapshot(TorrentId id)
            => Snapshots.TryGetValue(id, out var s) ? s : null;

        public Task<IReadOnlyList<PeerInfo>> GetPeersAsync(TorrentId id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PeerInfo>>(Array.Empty<PeerInfo>());

        public Task<IReadOnlyList<TrackerInfo>> GetTrackersAsync(TorrentId id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TrackerInfo>>(Array.Empty<TrackerInfo>());

        public Task<Result> AddTrackerAsync(TorrentId id, string url, int tier = 0, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> RemoveTrackerAsync(TorrentId id, string url, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> EditTrackerAsync(TorrentId id, string oldUrl, string newUrl, int newTier, CancellationToken ct = default) => Task.FromResult(Result.Success());

        public Task<IReadOnlyList<WebSeedInfo>> GetWebSeedsAsync(TorrentId id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WebSeedInfo>>(Array.Empty<WebSeedInfo>());

        public Task<Result> AddWebSeedAsync(TorrentId id, string url, CancellationToken ct = default) => Task.FromResult(Result.Success());

        public Task<Result> RemoveWebSeedAsync(TorrentId id, string url, CancellationToken ct = default) => Task.FromResult(Result.Success());

        public Task<Result> AddPeerAsync(TorrentId id, string ipAddress, int port, CancellationToken ct = default) => Task.FromResult(Result.Success());

        public Task<byte[]?> ExportTorrentBytesAsync(TorrentId id, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);

        public Task<IReadOnlyList<TorrentFileEntry>> GetTorrentFilesAsync(TorrentId id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TorrentFileEntry>>(Array.Empty<TorrentFileEntry>());

        public Task<IReadOnlyList<bool>> GetPiecesAsync(TorrentId id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<bool>>(Array.Empty<bool>());

        public SessionStats GetSessionStats() => default;

        public Task<TorrentDetailInfo?> GetTorrentDetailAsync(TorrentId id, CancellationToken ct = default) =>
            Task.FromResult<TorrentDetailInfo?>(null);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InMemorySettingsService : ISettingsService
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
        public IReadOnlyList<LogEntry> GetMessages(long afterId = -1, LogSeverity filter = LogSeverity.All)
            => Array.Empty<LogEntry>();
        public void Write(string message, LogSeverity severity = LogSeverity.Normal) { }
        public event EventHandler<LogEntry>? MessageLogged { add { } remove { } }
    }

    /// <summary>Minimal controllable TimeProvider — advances only when the test asks.</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
