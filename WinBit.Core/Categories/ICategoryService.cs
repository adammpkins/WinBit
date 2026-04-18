namespace WinBit.Core.Categories;

/// <summary>
/// Reads and writes user-defined categories to <c>Paths.CategoriesFile</c>. Atomic writes
/// (temp + rename). Assignment to a specific torrent lives on <c>ITorrentStateStore</c>.
/// </summary>
public interface ICategoryService
{
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default);

    Task<Category?> GetAsync(string name, CancellationToken ct = default);

    /// <summary>Adds a new category or replaces the existing entry with the same name.</summary>
    Task UpsertAsync(Category category, CancellationToken ct = default);

    Task RemoveAsync(string name, CancellationToken ct = default);
}
