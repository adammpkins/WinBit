using System.Text.Json;
using WinBit.Core.Persistence;

namespace WinBit.Core.Rss;

public sealed class AutoDownloaderService : IAutoDownloaderService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Paths _paths;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, AutoDownloadRule> _byName = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public event EventHandler? Changed;

    public AutoDownloaderService(Paths paths) => _paths = paths;

    public async Task<IReadOnlyList<AutoDownloadRule>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _byName.Values
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AutoDownloadRule?> GetAsync(string name, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _byName.TryGetValue(name, out var rule) ? rule : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpsertAsync(AutoDownloadRule rule, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            throw new ArgumentException("Rule name must not be empty.", nameof(rule));
        }

        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _byName[rule.Name] = rule;
            await PersistAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveAsync(string name, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        var removed = false;
        try
        {
            if (_byName.Remove(name))
            {
                removed = true;
                await PersistAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
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

            Directory.CreateDirectory(_paths.RssDir);
            if (File.Exists(RulesFile))
            {
                try
                {
                    await using var stream = File.OpenRead(RulesFile);
                    var loaded = await JsonSerializer.DeserializeAsync<List<AutoDownloadRule>>(stream, JsonOptions, ct).ConfigureAwait(false);
                    if (loaded is not null)
                    {
                        foreach (var rule in loaded)
                        {
                            if (!string.IsNullOrWhiteSpace(rule.Name))
                            {
                                _byName[rule.Name] = rule;
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // Corrupt file — treat as empty; next write replaces it.
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
        Directory.CreateDirectory(_paths.RssDir);
        var tmp = RulesFile + ".tmp";
        var snapshot = _byName.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, ct).ConfigureAwait(false);
        }
        File.Move(tmp, RulesFile, overwrite: true);
    }

    private string RulesFile => Path.Combine(_paths.RssDir, "rules.json");
}
