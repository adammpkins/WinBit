using System.Text.Json;
using WinBit.Core.Persistence;

namespace WinBit.Core.Categories;

public sealed class CategoryService : ICategoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Paths _paths;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, Category> _byName = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public CategoryService(Paths paths) => _paths = paths;

    public async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _byName.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Category?> GetAsync(string name, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _byName.TryGetValue(name, out var c) ? c : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpsertAsync(Category category, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(category.Name))
        {
            throw new ArgumentException("Category name must not be empty.", nameof(category));
        }

        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _byName[category.Name] = category;
            await PersistAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveAsync(string name, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_byName.Remove(name))
            {
                await PersistAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded)
        {
            return;
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            if (File.Exists(_paths.CategoriesFile))
            {
                await using var stream = File.OpenRead(_paths.CategoriesFile);
                var loaded = await JsonSerializer.DeserializeAsync<List<Category>>(stream, JsonOptions, ct).ConfigureAwait(false);
                if (loaded is not null)
                {
                    foreach (var c in loaded)
                    {
                        if (!string.IsNullOrWhiteSpace(c.Name))
                        {
                            _byName[c.Name] = c;
                        }
                    }
                }
            }
            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        var tmp = _paths.CategoriesFile + ".tmp";
        var snapshot = _byName.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, ct).ConfigureAwait(false);
        }
        File.Move(tmp, _paths.CategoriesFile, overwrite: true);
    }
}
