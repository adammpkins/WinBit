namespace WinBit.Core.Hosting;

/// <summary>
/// Configures WinBit.Core at startup. Consumed by <see cref="ServiceCollectionExtensions.AddWinBitCore"/>.
/// </summary>
public sealed class WinBitCoreOptions
{
    /// <summary>
    /// Override the default data root (%LOCALAPPDATA%\WinBit). Primarily used by tests.
    /// </summary>
    public string? DataRoot { get; set; }

    /// <summary>
    /// Debounce window before the settings file is rewritten. Successive <c>SaveAsync</c>
    /// calls inside this window coalesce into a single atomic write. Tests override this to
    /// keep the suite fast; production defaults to 500 ms.
    /// </summary>
    public TimeSpan SettingsSaveDebounce { get; set; } = TimeSpan.FromMilliseconds(500);
}
