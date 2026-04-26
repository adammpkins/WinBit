using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Settings;
using WinBit.Core.Sharing;

namespace WinBit.Tests.Helpers;

/// <summary>
/// Default <see cref="ITorrentSessionService"/> stub used by the Web UI endpoint tests.
/// Returns empty snapshots and records every control-method call the endpoint router fires
/// so tests can assert the endpoint layer hits the session correctly.
/// </summary>
public sealed class StubTorrentSession : ITorrentSessionService
{
    public Dictionary<string, TorrentSnapshot> SnapshotsByHash { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> SavePaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> MagnetUris { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AddTorrentParams> AddCalls { get; } = new();
    public List<(string Op, TorrentId Id)> ControlCalls { get; } = new();
    public List<(TorrentId Id, bool DeleteContent)> RemoveCalls { get; } = new();

    public bool IsRunning => true;

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

    public IReadOnlyList<TorrentId> Torrents => SnapshotsByHash.Keys.Select(TorrentId.FromInfoHash).ToArray();

    public event EventHandler<IReadOnlyList<TorrentSnapshot>>? TorrentUpdated { add { } remove { } }

    public void CaptureAndPublishSnapshots() { }

    public IReadOnlyList<TorrentSnapshot> GetSnapshots() => SnapshotsByHash.Values.ToArray();

    public Task PersistFastResumeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<Result<TorrentId>> AddAsync(AddTorrentParams parameters, CancellationToken ct = default)
    {
        AddCalls.Add(parameters);
        return Task.FromResult(Result<TorrentId>.Success(TorrentId.FromInfoHash(new string('a', 40))));
    }

    public Task<Result> RemoveAsync(TorrentId id, bool deleteContent = false, CancellationToken ct = default)
    {
        RemoveCalls.Add((id, deleteContent));
        return Task.FromResult(Result.Success());
    }

    public Task<Result> SetNameAsync(TorrentId id, string name, CancellationToken ct = default)
    {
        Names[id.Value] = name;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> PauseAsync(TorrentId id, CancellationToken ct = default)
    {
        ControlCalls.Add(("pause", id));
        return Task.FromResult(Result.Success());
    }

    public Task<Result> ResumeAsync(TorrentId id, CancellationToken ct = default)
    {
        ControlCalls.Add(("resume", id));
        return Task.FromResult(Result.Success());
    }

    public Task<Result> ForceRecheckAsync(TorrentId id, CancellationToken ct = default)
    {
        ControlCalls.Add(("recheck", id));
        return Task.FromResult(Result.Success());
    }

    public Task<Result> ForceReannounceAsync(TorrentId id, CancellationToken ct = default) => Task.FromResult(Result.Success());

    public string? GetMagnetUri(TorrentId id) => MagnetUris.TryGetValue(id.Value, out var v) ? v : null;
    public string? GetSavePath(TorrentId id) => SavePaths.TryGetValue(id.Value, out var v) ? v : null;
    public string? GetName(TorrentId id) => Names.TryGetValue(id.Value, out var v) ? v : null;
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

    public SessionStats CurrentStats { get; set; }
    public SessionStats GetSessionStats() => CurrentStats;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
