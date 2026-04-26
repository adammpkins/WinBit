using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinBit.Core.BitTorrent;

namespace WinBit.Controls;

/// <summary>
/// Renders one of the nine <see cref="TorrentState"/> values as a Fluent pill: icon + label on a
/// rounded, theme-aware background. All brushes resolve from <c>Application.Current.Resources</c>
/// so light/dark and high-contrast themes work without extra wiring.
/// </summary>
public sealed partial class StatePill : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(TorrentState),
        typeof(StatePill),
        new PropertyMetadata(TorrentState.Stopped, OnStateChanged));

    public StatePill()
    {
        InitializeComponent();
        Apply(State);
    }

    public TorrentState State
    {
        get => (TorrentState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((StatePill)d).Apply((TorrentState)e.NewValue);

    private void Apply(TorrentState state)
    {
        var palette = Palettes[state];

        PillIcon.Glyph = palette.Glyph;
        PillLabel.Text = palette.Label;
        PillBorder.Background = (Brush)Application.Current.Resources[palette.BackgroundKey];
        PillBorder.BorderBrush = (Brush)Application.Current.Resources[palette.BorderKey];
        PillIcon.Foreground = (Brush)Application.Current.Resources[palette.ForegroundKey];
        PillLabel.Foreground = (Brush)Application.Current.Resources[palette.ForegroundKey];

        AutomationProperties.SetName(this, $"State {palette.Label}");
    }

    private readonly record struct Palette(string Glyph, string Label, string BackgroundKey, string BorderKey, string ForegroundKey);

    private static readonly Dictionary<TorrentState, Palette> Palettes = new()
    {
        [TorrentState.Downloading] = new("\uE896", "Downloading", "AccentFillColorTertiaryBrush", "AccentFillColorSecondaryBrush", "TextOnAccentFillColorPrimaryBrush"),
        [TorrentState.Seeding]     = new("\uE898", "Seeding",     "SystemFillColorSuccessBackgroundBrush", "SystemFillColorSuccessBrush", "TextFillColorPrimaryBrush"),
        [TorrentState.Paused]      = new("\uE769", "Paused",      "ControlAltFillColorTertiaryBrush",      "ControlStrokeColorDefaultBrush", "TextFillColorSecondaryBrush"),
        [TorrentState.Queued]      = new("\uE823", "Queued",      "ControlAltFillColorTertiaryBrush",      "ControlStrokeColorDefaultBrush", "TextFillColorSecondaryBrush"),
        [TorrentState.Checking]    = new("\uE895", "Checking",    "AccentFillColorTertiaryBrush",          "AccentFillColorSecondaryBrush", "TextOnAccentFillColorPrimaryBrush"),
        [TorrentState.Stalled]     = new("\uE7BA", "Stalled",     "SystemFillColorCautionBackgroundBrush", "SystemFillColorCautionBrush",   "TextFillColorPrimaryBrush"),
        [TorrentState.Completed]   = new("\uE930", "Completed",   "SystemFillColorSuccessBackgroundBrush", "SystemFillColorSuccessBrush",   "TextFillColorPrimaryBrush"),
        [TorrentState.Error]       = new("\uE783", "Error",       "SystemFillColorCriticalBackgroundBrush","SystemFillColorCriticalBrush",  "TextFillColorPrimaryBrush"),
        [TorrentState.Stopped]     = new("\uE71A", "Stopped",     "ControlAltFillColorTertiaryBrush",      "ControlStrokeColorDefaultBrush", "TextFillColorSecondaryBrush"),
        [TorrentState.Metadata]    = new("\uE895", "Fetching metadata", "ControlAltFillColorTertiaryBrush", "ControlStrokeColorDefaultBrush", "TextFillColorSecondaryBrush"),
    };
}
