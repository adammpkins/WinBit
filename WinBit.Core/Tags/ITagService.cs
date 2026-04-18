namespace WinBit.Core.Tags;

/// <summary>
/// Reads and writes the user-defined tag vocabulary to <c>Paths.TagsFile</c>. Per-torrent tag
/// assignments live on the torrent record itself.
/// </summary>
public interface ITagService
{
    Task<IReadOnlyList<string>> GetAllAsync(CancellationToken ct = default);

    Task AddAsync(string tag, CancellationToken ct = default);

    Task RemoveAsync(string tag, CancellationToken ct = default);
}
