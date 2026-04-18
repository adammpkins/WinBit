using System.Text.Json;
using WinBit.Core.Persistence;

namespace WinBit.Core.Tags;

public sealed class TagService : ITagService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Paths _paths;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly SortedSet<string> _tags = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public TagService(Paths paths) => _paths = paths;

    public async Task<IReadOnlyList<string>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _tags.ToArray();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddAsync(string tag, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new ArgumentException("Tag must not be empty.", nameof(tag));
        }

        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_tags.Add(tag))
            {
                await PersistAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveAsync(string tag, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_tags.Remove(tag))
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

            if (File.Exists(_paths.TagsFile))
            {
                await using var stream = File.OpenRead(_paths.TagsFile);
                var loaded = await JsonSerializer.DeserializeAsync<List<string>>(stream, JsonOptions, ct).ConfigureAwait(false);
                if (loaded is not null)
                {
                    foreach (var t in loaded)
                    {
                        if (!string.IsNullOrWhiteSpace(t))
                        {
                            _tags.Add(t);
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
        var tmp = _paths.TagsFile + ".tmp";
        var snapshot = _tags.ToArray();

        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, ct).ConfigureAwait(false);
        }
        File.Move(tmp, _paths.TagsFile, overwrite: true);
    }
}
