using FluentAssertions;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Logging;
using WinBit.Core.Settings;
using WinBit.Core.Sharing;
using Xunit;

namespace WinBit.Tests;

public sealed class PeerDiscoveryApplierTests
{
    [Fact]
    public async Task Start_pushes_current_flags_into_session()
    {
        var session = new RecordingSession();
        var settings = new InMemorySettingsService();
        settings.Current.BitTorrent.Dht = false;
        settings.Current.BitTorrent.Pex = true;
        settings.Current.BitTorrent.Lsd = true;

        var applier = new PeerDiscoveryApplier(session, settings, new NoopLog());
        await applier.StartAsync(CancellationToken.None);

        session.Calls.Should().ContainSingle()
            .Which.Should().Be((false, true, true));

        await applier.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Settings_change_forwards_new_flag_set()
    {
        var session = new RecordingSession();
        var settings = new InMemorySettingsService();
        settings.Current.BitTorrent.Dht = true;
        settings.Current.BitTorrent.Pex = true;
        settings.Current.BitTorrent.Lsd = true;

        var applier = new PeerDiscoveryApplier(session, settings, new NoopLog());
        await applier.StartAsync(CancellationToken.None);

        await settings.UpdateAsync(s => s.BitTorrent.Lsd = false);

        session.Calls.Should().HaveCount(2);
        session.Calls[1].Should().Be((true, true, false));

        await applier.StopAsync(CancellationToken.None);
    }

    private sealed class RecordingSession : ITorrentSessionService
    {
        public List<(bool Dht, bool Pex, bool Lsd)> Calls { get; } = new();

        public Task<Result> SetPeerDiscoveryAsync(bool dht, bool pex, bool lsd, CancellationToken ct = default)
        {
            Calls.Add((dht, pex, lsd));
            return Task.FromResult(Result.Success());
        }

        public bool IsRunning => true;
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<TorrentId> Torrents => Array.Empty<TorrentId>();
        public event EventHandler<IReadOnlyList<TorrentSnapshot>>? TorrentUpdated { add { } remove { } }
        public void CaptureAndPublishSnapshots() { }
        public Task PersistFastResumeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<Result<TorrentId>> AddAsync(AddTorrentParams parameters, CancellationToken ct = default) => throw new NotImplementedException();
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
        public IReadOnlyList<LogEntry> GetMessages(long afterId = -1, LogSeverity filter = LogSeverity.All) => Array.Empty<LogEntry>();
        public void Write(string message, LogSeverity severity = LogSeverity.Normal) { }
        public event EventHandler<LogEntry>? MessageLogged { add { } remove { } }
    }
}
