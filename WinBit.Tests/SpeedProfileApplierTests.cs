using FluentAssertions;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Logging;
using WinBit.Core.Settings;
using WinBit.Core.Sharing;
using Xunit;

namespace WinBit.Tests;

public sealed class SpeedProfileApplierTests
{
    [Fact]
    public async Task Apply_uses_primary_profile_when_alt_disabled()
    {
        var session = new RecordingSession();
        var settings = new InMemorySettingsService();
        settings.Current.Speed = new SpeedSettings
        {
            GlobalDownBps = 500_000,
            GlobalUpBps = 100_000,
            AltDownBps = 50_000,
            AltUpBps = 10_000,
            AltEnabled = false,
        };

        var applier = new SpeedProfileApplier(session, settings, new NoopLog());
        await applier.ApplyAsync(settings.Current, CancellationToken.None);

        session.GlobalLimitCalls.Should().ContainSingle()
            .Which.Should().Be((500_000L, 100_000L));
    }

    [Fact]
    public async Task Apply_swaps_to_alt_profile_when_toggle_flips()
    {
        var session = new RecordingSession();
        var settings = new InMemorySettingsService();
        settings.Current.Speed = new SpeedSettings
        {
            GlobalDownBps = 500_000,
            GlobalUpBps = 100_000,
            AltDownBps = 50_000,
            AltUpBps = 10_000,
            AltEnabled = false,
        };

        var applier = new SpeedProfileApplier(session, settings, new NoopLog());
        await applier.StartAsync(CancellationToken.None);

        settings.Current.Speed.AltEnabled = true;
        await settings.UpdateAsync(s => s.Speed.AltEnabled = true);

        // First call at StartAsync with primary profile, second via the Changed event with alt.
        session.GlobalLimitCalls.Should().HaveCount(2);
        session.GlobalLimitCalls[0].Should().Be((500_000L, 100_000L));
        session.GlobalLimitCalls[1].Should().Be((50_000L, 10_000L));

        await applier.StopAsync(CancellationToken.None);
    }

    private sealed class RecordingSession : ITorrentSessionService
    {
        public List<(long Down, long Up)> GlobalLimitCalls { get; } = new();

        public Task<Result> SetGlobalSpeedLimitsAsync(long downloadBps, long uploadBps, CancellationToken ct = default)
        {
            GlobalLimitCalls.Add((downloadBps, uploadBps));
            return Task.FromResult(Result.Success());
        }

        public bool IsRunning => true;
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<TorrentId> Torrents => Array.Empty<TorrentId>();
        public event EventHandler<IReadOnlyList<TorrentSnapshot>>? TorrentUpdated { add { } remove { } }
        public void CaptureAndPublishSnapshots() { }
        public Task PersistFastResumeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<Result<TorrentId>> AddAsync(AddTorrentParams parameters, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Result> RemoveAsync(TorrentId id, bool deleteContent = false, CancellationToken ct = default)
            => Task.FromResult(Result.Success());
        public Task<Result> PauseAsync(TorrentId id, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> ResumeAsync(TorrentId id, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> ForceRecheckAsync(TorrentId id, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> ForceReannounceAsync(TorrentId id, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public string? GetMagnetUri(TorrentId id) => null;
        public string? GetSavePath(TorrentId id) => null;
        public string? GetName(TorrentId id) => null;
        public IReadOnlyList<string> GetTrackerHosts(TorrentId id) => Array.Empty<string>();
        public (long DownloadBps, long UploadBps)? GetSpeedLimits(TorrentId id) => null;
        public Task<Result> SetSpeedLimitsAsync(TorrentId id, long? downloadBps, long? uploadBps, CancellationToken ct = default)
            => Task.FromResult(Result.Success());
        public Task<Result> SetSuperSeedingAsync(TorrentId id, bool enabled, CancellationToken ct = default)
            => Task.FromResult(Result.Success());
        public Task<Result> SetPortForwardingAsync(bool enabled, CancellationToken ct = default)
            => Task.FromResult(Result.Success());
        public Task<Result> SetEncryptionModeAsync(WinBit.Core.Settings.EncryptionMode mode, CancellationToken ct = default)
            => Task.FromResult(Result.Success());
        public Task<Result> SetPeerDiscoveryAsync(bool dht, bool pex, bool lsd, CancellationToken ct = default)
            => Task.FromResult(Result.Success());
        public ShareLimitSnapshot? GetShareLimitSnapshot(TorrentId id) => null;
        public SessionStats GetSessionStats() => default;
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
}
