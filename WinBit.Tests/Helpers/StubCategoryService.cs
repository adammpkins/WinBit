using WinBit.Core.Categories;
using WinBit.Core.Tags;

namespace WinBit.Tests.Helpers;

public sealed class StubCategoryService : ICategoryService
{
    private readonly Dictionary<string, Category> _byName = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Category>>(_byName.Values.ToArray());

    public Task<Category?> GetAsync(string name, CancellationToken ct = default) =>
        Task.FromResult(_byName.TryGetValue(name, out var c) ? c : null);

    public Task UpsertAsync(Category category, CancellationToken ct = default)
    {
        _byName[category.Name] = category;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string name, CancellationToken ct = default)
    {
        _byName.Remove(name);
        return Task.CompletedTask;
    }
}

public sealed class StubTagService : ITagService
{
    private readonly HashSet<string> _tags = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<string>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(_tags.ToArray());

    public Task AddAsync(string tag, CancellationToken ct = default)
    {
        _tags.Add(tag);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string tag, CancellationToken ct = default)
    {
        _tags.Remove(tag);
        return Task.CompletedTask;
    }
}
