namespace WinBit.Core.Settings;

public interface ISettingsService
{
    AppSettings Current { get; }
    Task<AppSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
    Task UpdateAsync(Action<AppSettings> mutate, CancellationToken ct = default);
    event EventHandler<AppSettings>? Changed;
}
