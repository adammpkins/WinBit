using WinBit.Core.Common;
using WinBit.Core.Persistence;

namespace WinBit.Tests.Helpers;

/// <summary>
/// No-op <see cref="ITorrentStateStore"/> for endpoint tests that don't exercise
/// category/tag persistence directly.
/// </summary>
public sealed class StubTorrentStateStore : ITorrentStateStore
{
    public Task UpsertTorrentAsync(TorrentStateRecord record, CancellationToken ct = default) => Task.CompletedTask;

    public Task RemoveTorrentAsync(TorrentId id, CancellationToken ct = default) => Task.CompletedTask;

    public Task SaveFastResumeAsync(TorrentId id, byte[] blob, int version, CancellationToken ct = default) => Task.CompletedTask;

    public Task<byte[]?> LoadFastResumeAsync(TorrentId id, int expectedVersion, CancellationToken ct = default) =>
        Task.FromResult<byte[]?>(null);

    public Task UpdateCompletedUtcAsync(TorrentId id, DateTime completedUtc, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<TorrentStateRecord>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TorrentStateRecord>>(Array.Empty<TorrentStateRecord>());

    public Task<TorrentStateRecord?> GetByIdAsync(TorrentId id, CancellationToken ct = default) =>
        Task.FromResult<TorrentStateRecord?>(null);
}
