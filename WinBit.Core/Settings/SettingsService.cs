namespace WinBit.Core.Settings;

public sealed class SettingsService : ISettingsService
{
    private readonly ISettingsStore _store;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private AppSettings _current = new();

    public SettingsService(ISettingsStore store) => _store = store;

    public AppSettings Current => _current;

    public event EventHandler<AppSettings>? Changed;

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            _current = await _store.LoadAsync(ct) ?? new AppSettings();
            Changed?.Invoke(this, _current);
            return _current;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            await _store.SaveAsync(_current, ct);
            await _store.FlushAsync(ct);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task UpdateAsync(Action<AppSettings> mutate, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            mutate(_current);
            await _store.SaveAsync(_current, ct);
        }
        finally
        {
            _mutex.Release();
        }

        Changed?.Invoke(this, _current);
    }
}
