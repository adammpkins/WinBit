using WinBit.Core.Common;

namespace WinBit.Core.Persistence;

public interface ICustomNameStore
{
    string? GetName(TorrentId id);
    Task SetNameAsync(TorrentId id, string name, CancellationToken ct = default);
    Task RemoveNameAsync(TorrentId id, CancellationToken ct = default);
}
