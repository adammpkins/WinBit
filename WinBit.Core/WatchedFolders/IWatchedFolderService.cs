namespace WinBit.Core.WatchedFolders;

public interface IWatchedFolderService
{
    Task<IReadOnlyList<WatchedFolder>> GetAllAsync(CancellationToken ct = default);

    Task UpsertAsync(WatchedFolder folder, CancellationToken ct = default);

    Task RemoveAsync(string path, CancellationToken ct = default);
}
