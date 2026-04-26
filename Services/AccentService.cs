using Microsoft.UI.Xaml;

namespace WinBit.Services;

/// <summary>
/// Applies the persisted accent color to the app's resource dictionary at startup. WinUI 3
/// controls resolve SystemAccentColor once at construction, so changing the accent mid-session
/// doesn't retroactively re-style already-rendered surfaces — the UI surfaces a restart-required
/// hint when the user picks a new swatch.
/// </summary>
public static class AccentService
{
    private static readonly string[] SystemAccentKeys = new[]
    {
        "SystemAccentColor",
        "SystemAccentColorLight1",
        "SystemAccentColorLight2",
        "SystemAccentColorLight3",
        "SystemAccentColorDark1",
        "SystemAccentColorDark2",
        "SystemAccentColorDark3",
    };

    public static void Apply(string? hex)
    {
        if (!AccentPalette.TryParse(hex, out var color))
        {
            return;
        }

        var resources = Application.Current.Resources;
        foreach (var key in SystemAccentKeys)
        {
            resources[key] = color;
        }
        // Fluent theme dictionaries also carry SystemAccentColor keys; without overriding them
        // too, {ThemeResource SystemAccentColor} lookups resolve to the theme-dictionary copy
        // and our app-scope override is ignored.
        foreach (var themeName in new[] { "Light", "Dark", "Default", "HighContrast" })
        {
            if (resources.ThemeDictionaries.TryGetValue(themeName, out var value)
                && value is Microsoft.UI.Xaml.ResourceDictionary theme)
            {
                foreach (var key in SystemAccentKeys)
                {
                    theme[key] = color;
                }
            }
        }
    }
}
