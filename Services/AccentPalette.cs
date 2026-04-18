using Windows.UI;

namespace WinBit.Services;

/// <summary>
/// Named accent swatches shown in Settings → Behavior. The hex value is written back to
/// <c>AppSettings.UiState.AccentColor</c> and applied on the next app startup via
/// <see cref="AccentService"/>.
/// </summary>
public static class AccentPalette
{
    public readonly record struct Swatch(string Name, string Hex, Color Color);

    public static IReadOnlyList<Swatch> Swatches { get; } = new[]
    {
        Make("Aurora", "#0078D4"),
        Make("Sapphire", "#005FB8"),
        Make("Cobalt", "#2D7D9A"),
        Make("Teal", "#00A88E"),
        Make("Fern", "#107C10"),
        Make("Ochre", "#DA9D00"),
        Make("Ember", "#D13438"),
        Make("Orchid", "#744DA9"),
    };

    public static bool TryParse(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }
        var trimmed = hex.TrimStart('#');
        if (trimmed.Length != 6 && trimmed.Length != 8)
        {
            return false;
        }
        try
        {
            byte a = 0xFF, r, g, b;
            var offset = 0;
            if (trimmed.Length == 8)
            {
                a = Convert.ToByte(trimmed[..2], 16);
                offset = 2;
            }
            r = Convert.ToByte(trimmed.Substring(offset, 2), 16);
            g = Convert.ToByte(trimmed.Substring(offset + 2, 2), 16);
            b = Convert.ToByte(trimmed.Substring(offset + 4, 2), 16);
            color = Color.FromArgb(a, r, g, b);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Swatch Make(string name, string hex)
    {
        TryParse(hex, out var color);
        return new Swatch(name, hex, color);
    }
}
