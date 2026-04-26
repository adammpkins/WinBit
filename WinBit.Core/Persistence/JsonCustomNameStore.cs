using System.Collections.Concurrent;
using System.Text.Json;
using WinBit.Core.Common;

namespace WinBit.Core.Persistence;

/// <summary>
/// Persists user-assigned display names for torrents in a JSON file keyed by info-hash.
/// Writes are atomic (temp file + rename) so a crash mid-write never corrupts the store.
/// The dictionary is populated eagerly in the constructor so <see cref="GetName"/> is safe
/// to call before any async method fires — important because the polling tick calls it
/// synchronously once per second from a background thread while UI-thread renames write concurrently.
/// </summary>
public sealed class JsonCustomNameStore : ICustomNameStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Paths _paths;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    public JsonCustomNameStore(Paths paths)
    {
        _paths = paths;
        LoadFromDisk();
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(_paths.CustomNamesFile))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_paths.CustomNamesFile);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            if (loaded is not null)
            {
                foreach (var (key, value) in loaded)
                {
                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                    {
                        _names[key] = value;
                    }
                }
            }
        }
        catch
        {
            // A corrupt or unreadable file is non-fatal; the store starts empty and
            // overwrites the file on the next successful write.
        }
    }

    public string? GetName(TorrentId id) =>
        _names.TryGetValue(id.Value, out var name) ? name : null;

    public async Task SetNameAsync(TorrentId id, string name, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _names[id.Value] = name;
            await PersistAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveNameAsync(TorrentId id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_names.TryRemove(id.Value, out _))
            {
                await PersistAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        var tmp = _paths.CustomNamesFile + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, _names, JsonOptions, ct).ConfigureAwait(false);
        }
        File.Move(tmp, _paths.CustomNamesFile, overwrite: true);
    }

    public ValueTask DisposeAsync()
    {
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }
}
