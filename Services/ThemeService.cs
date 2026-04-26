using Microsoft.UI.Xaml;
using WinBit.Core.Logging;
using WinBit.Core.Settings;

namespace WinBit.Services;

/// <summary>
/// Tracks the user's theme preference and mirrors it into <see cref="AppSettings.UiState.Theme"/>.
/// Writes are fire-and-forget because <c>ISettingsService.UpdateAsync</c> is already debounced
/// by <c>JsonSettingsStore</c>, and cycling themes must not block the UI thread.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private const string SystemLabel = "System";
    private const string LightLabel = "Light";
    private const string DarkLabel = "Dark";

    private readonly ISettingsService _settings;
    private readonly ILogService _log;

    public ThemeService(ISettingsService settings, ILogService log)
    {
        _settings = settings;
        _log = log;
    }

    public ElementTheme CurrentTheme { get; private set; } = ElementTheme.Default;

    public event EventHandler<ElementTheme>? ThemeChanged;

    public Task InitializeAsync()
    {
        var parsed = ParseTheme(_settings.Current.UiState.Theme);
        if (parsed != CurrentTheme)
        {
            CurrentTheme = parsed;
            ThemeChanged?.Invoke(this, parsed);
        }
        return Task.CompletedTask;
    }

    public void Apply(ElementTheme theme)
    {
        if (CurrentTheme == theme)
        {
            _log.Write($"Theme apply skipped — already {theme}", LogSeverity.Info);
            return;
        }

        var subs = ThemeChanged?.GetInvocationList().Length ?? 0;
        _log.Write($"Theme apply: {CurrentTheme} → {theme} (subscribers:{subs})", LogSeverity.Info);
        CurrentTheme = theme;
        ThemeChanged?.Invoke(this, theme);

        var label = ToLabel(theme);
        _ = _settings.UpdateAsync(s => s.UiState.Theme = label);
    }

    private static ElementTheme ParseTheme(string? label) => label switch
    {
        LightLabel => ElementTheme.Light,
        DarkLabel => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    private static string ToLabel(ElementTheme theme) => theme switch
    {
        ElementTheme.Light => LightLabel,
        ElementTheme.Dark => DarkLabel,
        _ => SystemLabel,
    };
}
