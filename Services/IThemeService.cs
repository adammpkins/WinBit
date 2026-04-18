using Microsoft.UI.Xaml;

namespace WinBit.Services;

public interface IThemeService
{
    ElementTheme CurrentTheme { get; }

    /// <summary>Loads the persisted theme from <c>ISettingsService</c> and syncs state.</summary>
    Task InitializeAsync();

    /// <summary>Updates the in-memory theme, raises <see cref="ThemeChanged"/>, and persists via settings.</summary>
    void Apply(ElementTheme theme);

    event EventHandler<ElementTheme>? ThemeChanged;
}
