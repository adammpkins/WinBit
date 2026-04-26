using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using WinBit.Core.Hosting;
using WinBit.Core.Persistence;

namespace WinBit.Core.Settings;

/// <summary>
/// Atomic JSON-file settings store. <see cref="SaveAsync"/> schedules a debounced rewrite;
/// successive calls inside the debounce window coalesce into a single atomic write
/// (temp file + rename). <see cref="FlushAsync"/> and <see cref="DisposeAsync"/> force a
/// pending write to land immediately so shutdown never loses in-flight edits.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) },
    };

    private readonly Paths _paths;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private AppSettings? _pending;
    private CancellationTokenSource? _debounceCts;
    private Task _inFlight = Task.CompletedTask;

    public JsonSettingsStore(Paths paths, IOptions<WinBitCoreOptions> options)
        : this(paths, options.Value.SettingsSaveDebounce)
    {
    }

    public JsonSettingsStore(Paths paths, TimeSpan debounce)
    {
        _paths = paths;
        _debounce = debounce;
    }

    public async Task<AppSettings?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_paths.SettingsFile))
        {
            return null;
        }

        await using var stream = File.OpenRead(_paths.SettingsFile);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options, ct);
    }

    public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _pending = settings;
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            _inFlight = RunDebouncedAsync(_debounceCts.Token);
        }
        return Task.CompletedTask;
    }

    private async Task RunDebouncedAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_debounce, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        AppSettings? snapshot;
        lock (_gate)
        {
            snapshot = _pending;
            _pending = null;
            _debounceCts?.Cancel();
        }

        if (snapshot is null)
        {
            return;
        }

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await WriteAtomicAsync(snapshot, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task WriteAtomicAsync(AppSettings settings, CancellationToken ct)
    {
        var tmp = _paths.SettingsFile + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, settings, Options, ct).ConfigureAwait(false);
        }
        File.Move(tmp, _paths.SettingsFile, overwrite: true);
    }

    public async ValueTask DisposeAsync()
    {
        Task inFlight;
        lock (_gate)
        {
            _debounceCts?.Cancel();
            inFlight = _inFlight;
        }

        try
        {
            await inFlight.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Debounce path handles its own cancellation; nothing to do.
        }

        await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        _writeLock.Dispose();
    }
}
