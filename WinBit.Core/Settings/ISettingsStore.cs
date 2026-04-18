namespace WinBit.Core.Settings;

public interface ISettingsStore : IAsyncDisposable
{
    Task<AppSettings?> LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Records the latest settings snapshot and schedules a debounced write. Successive calls
    /// inside the debounce window coalesce — only the most recent snapshot reaches disk.
    /// </summary>
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);

    /// <summary>
    /// Cancels any pending debounce and writes the latest snapshot immediately.
    /// </summary>
    Task FlushAsync(CancellationToken ct = default);
}
