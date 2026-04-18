using System.Text.Json;
using WinBit.Core.Common;
using WinBit.Core.Persistence;

namespace WinBit.Core.Sharing;

public sealed class ShareLimitOverrideService : IShareLimitOverrideService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Paths _paths;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, PerTorrentShareLimitOverride> _byId = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public ShareLimitOverrideService(Paths paths) => _paths = paths;

    public async Task<IReadOnlyList<PerTorrentShareLimitOverride>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _byId.Values.OrderBy(e => e.Id.Value, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PerTorrentShareLimitOverride?> GetAsync(TorrentId id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _byId.TryGetValue(id.Value, out var e) ? e : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpsertAsync(PerTorrentShareLimitOverride entry, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entry.Id.Value))
        {
            throw new ArgumentException("TorrentId must not be empty.", nameof(entry));
        }

        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _byId[entry.Id.Value] = entry;
            await PersistAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveAsync(TorrentId id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_byId.Remove(id.Value))
            {
                await PersistAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public ShareLimits Effective(TorrentId id, ShareLimits global)
    {
        // Fast path — no overrides loaded means the merge is a no-op. EnsureLoadedAsync is
        // only called from the async surface; sync callers are responsible for warming the
        // cache with GetAllAsync() / GetAsync() before relying on Effective().
        if (!_loaded || !_byId.TryGetValue(id.Value, out var o))
        {
            return global;
        }

        return new ShareLimits
        {
            RatioLimit = o.RatioLimit ?? global.RatioLimit,
            SeedingTimeLimit = o.SeedingTimeLimit ?? global.SeedingTimeLimit,
            InactiveSeedingTimeLimit = o.InactiveSeedingTimeLimit ?? global.InactiveSeedingTimeLimit,
            Mode = o.Mode == ShareLimitsMode.Default ? global.Mode : o.Mode,
            Action = o.Action == ShareLimitAction.Default ? global.Action : o.Action,
        };
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

            if (File.Exists(_paths.ShareLimitOverridesFile))
            {
                await using var stream = File.OpenRead(_paths.ShareLimitOverridesFile);
                var loaded = await JsonSerializer.DeserializeAsync<List<PerTorrentShareLimitOverride>>(stream, JsonOptions, ct).ConfigureAwait(false);
                if (loaded is not null)
                {
                    foreach (var e in loaded)
                    {
                        if (!string.IsNullOrWhiteSpace(e.Id.Value))
                        {
                            _byId[e.Id.Value] = e;
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
        var tmp = _paths.ShareLimitOverridesFile + ".tmp";
        var snapshot = _byId.Values.OrderBy(e => e.Id.Value, StringComparer.OrdinalIgnoreCase).ToArray();

        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, ct).ConfigureAwait(false);
        }
        File.Move(tmp, _paths.ShareLimitOverridesFile, overwrite: true);
    }
}
