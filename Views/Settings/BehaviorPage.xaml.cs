using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using WinBit.Core.Settings;
using WinBit.Core.Shell;
using WinBit.Core.Updates;
using WinBit.Services;
using WinBit.Views.Dialogs;

namespace WinBit.Views.Settings;

public sealed partial class BehaviorPage : Page
{
    private readonly ISettingsService _settings;
    private readonly IShellAssociationService? _associations;
    private readonly IThemeService _themes;
    private bool _loading = true;

    public BehaviorPage()
    {
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _associations = App.Services.GetService<IShellAssociationService>();
        _themes = App.Services.GetRequiredService<IThemeService>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        try
        {
            CloseToTrayToggle.IsOn = _settings.Current.Behavior.CloseToTray;
            SlowDownloadWarningToggle.IsOn = _settings.Current.Behavior.SlowDownloadWarningEnabled;
            PreventSleepToggle.IsOn = _settings.Current.Behavior.PreventSleepWhileActive;
            RefreshDefaultClientCard();
            PopulateLanguageCombo();
            PopulateThemeCombo();
            PopulateAccentPalette();
        }
        finally
        {
            _loading = false;
        }
    }

    private static readonly (string Tag, string Display)[] SupportedLanguages = new[]
    {
        (string.Empty, "System default"),
        ("en-US", "English (United States)"),
        ("fr-FR", "Français (France)"),
    };

    private void PopulateLanguageCombo()
    {
        LanguageCombo.Items.Clear();
        var current = _settings.Current.UiState.LanguageTag ?? string.Empty;
        var selectedIndex = 0;
        for (var i = 0; i < SupportedLanguages.Length; i++)
        {
            var (tag, display) = SupportedLanguages[i];
            LanguageCombo.Items.Add(new ComboBoxItem { Content = display, Tag = tag });
            if (string.Equals(tag, current, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
            }
        }
        LanguageCombo.SelectedIndex = selectedIndex;
    }

    private async void OnAboutClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutDialog { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private void PopulateThemeCombo()
    {
        var current = _themes.CurrentTheme;
        ThemeCombo.SelectedIndex = current switch
        {
            ElementTheme.Light => 1,
            ElementTheme.Dark => 2,
            _ => 0,
        };
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        if (ThemeCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        var theme = (item.Tag as string) switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        _themes.Apply(theme);
    }

    private void PopulateAccentPalette()
    {
        AccentPaletteStack.Children.Clear();
        var current = _settings.Current.UiState.AccentColor;

        AccentPaletteStack.Children.Add(BuildSwatch(null, "System default", current is null));
        foreach (var swatch in AccentPalette.Swatches)
        {
            var selected = string.Equals(current, swatch.Hex, StringComparison.OrdinalIgnoreCase);
            AccentPaletteStack.Children.Add(BuildSwatch(swatch.Hex, swatch.Name, selected));
        }
    }

    private UIElement BuildSwatch(string? hex, string tooltip, bool selected)
    {
        var color = hex is null ? Colors.Transparent : (AccentPalette.TryParse(hex, out var c) ? c : Colors.Gray);
        var border = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(selected ? 2 : 1),
            BorderBrush = new SolidColorBrush(ResolveColor("TextFillColorPrimary", Colors.Gray)),
            Background = hex is null
                ? new SolidColorBrush(ResolveColor("SystemAccentColor", Colors.SteelBlue)) { Opacity = 0.15 }
                : new SolidColorBrush(color),
            Tag = hex,
        };
        ToolTipService.SetToolTip(border, tooltip);
        border.PointerPressed += OnSwatchPressed;
        if (hex is null)
        {
            border.Child = new TextBlock
            {
                Text = "—",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(ResolveColor("TextFillColorSecondary", Colors.DimGray)),
            };
        }
        return border;
    }

    private static Color ResolveColor(string key, Color fallback)
    {
        var resources = Application.Current.Resources;
        if (resources.TryGetValue(key, out var direct) && direct is Color dc)
        {
            return dc;
        }
        var themeName = Application.Current.RequestedTheme == ApplicationTheme.Dark ? "Dark" : "Light";
        if (resources.ThemeDictionaries.TryGetValue(themeName, out var themeObj)
            && themeObj is ResourceDictionary theme
            && theme.TryGetValue(key, out var themed)
            && themed is Color tc)
        {
            return tc;
        }
        return fallback;
    }

    private async void OnSwatchPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not Border border)
        {
            return;
        }
        var hex = border.Tag as string;
        var current = _settings.Current.UiState.AccentColor;
        if (string.Equals(current, hex, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        await _settings.UpdateAsync(s => s.UiState.AccentColor = hex);
        AccentService.Apply(hex);
        ForceThemeResourceRefresh();
        PopulateAccentPalette();
    }

    private static void ForceThemeResourceRefresh()
    {
        // WinUI 3 controls latch their accent brush at construction. Toggling RequestedTheme
        // forces every {ThemeResource …} lookup in the live visual tree to re-resolve, which
        // picks up the new SystemAccentColor* we just wrote. Restoring the original value in
        // the same pump keeps the user on whichever light/dark they picked.
        if (App.MainWindow?.Content is not FrameworkElement root)
        {
            return;
        }
        var original = root.RequestedTheme;
        var pivot = original == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        root.RequestedTheme = pivot;
        root.RequestedTheme = original;
    }

    private async void OnCheckUpdatesClicked(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateStatusBar.IsOpen = true;
        UpdateStatusBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;
        UpdateStatusBar.Title = "Checking for updates…";
        UpdateStatusBar.Message = null;
        try
        {
            var checker = App.Services.GetRequiredService<IUpdateChecker>();
            var info = await checker.CheckAsync();
            if (info.HasUpdate && info.LatestTag is not null)
            {
                UpdateStatusBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success;
                UpdateStatusBar.Title = $"Update available: {info.LatestTag}";
                UpdateStatusBar.Message = $"You're running {info.Current}. Open the release page to download.";
                if (!string.IsNullOrEmpty(info.ReleaseUrl))
                {
                    var hb = new HyperlinkButton
                    {
                        Content = "Open release page",
                        NavigateUri = new Uri(info.ReleaseUrl),
                    };
                    UpdateStatusBar.ActionButton = hb;
                }
            }
            else if (info.Latest is null)
            {
                UpdateStatusBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
                UpdateStatusBar.Title = "Couldn't check for updates";
                UpdateStatusBar.Message = "The GitHub release feed was unreachable or returned an unexpected payload.";
            }
            else
            {
                UpdateStatusBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;
                UpdateStatusBar.Title = "Up to date";
                UpdateStatusBar.Message = $"You're on the latest version ({info.Current}).";
            }
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private async void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        if (LanguageCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        var tag = (item.Tag as string) ?? string.Empty;
        var normalized = string.IsNullOrWhiteSpace(tag) ? null : tag;
        if (string.Equals(_settings.Current.UiState.LanguageTag ?? string.Empty, tag, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        await _settings.UpdateAsync(s => s.UiState.LanguageTag = normalized);
        LanguageRestartBar.IsOpen = true;
    }

    private async void OnCloseToTrayToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var value = CloseToTrayToggle.IsOn;
        await _settings.UpdateAsync(s => s.Behavior.CloseToTray = value);
    }

    private async void OnSlowDownloadWarningToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var value = SlowDownloadWarningToggle.IsOn;
        await _settings.UpdateAsync(s => s.Behavior.SlowDownloadWarningEnabled = value);
    }

    private async void OnPreventSleepToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var value = PreventSleepToggle.IsOn;
        await _settings.UpdateAsync(s => s.Behavior.PreventSleepWhileActive = value);
    }

    private async void OnRegisterDefaultClientClicked(object sender, RoutedEventArgs e)
    {
        if (_associations is null)
        {
            return;
        }
        await _associations.RegisterAsync(torrent: true, magnet: true);
        RefreshDefaultClientCard();
    }

    private void OnOpenDefaultAppsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:defaultapps",
                UseShellExecute = true,
            });
        }
        catch
        {
            // Swallow — any user can reach Default apps from Start menu anyway.
        }
    }

    private void RefreshDefaultClientCard()
    {
        if (_associations is null)
        {
            DefaultClientCard.Description = "Unavailable on this platform.";
            RegisterDefaultClientButton.IsEnabled = false;
            return;
        }
        var status = _associations.GetStatus();
        DefaultClientCard.Description = status switch
        {
            { TorrentFile: true, MagnetProtocol: true } => "WinBit handles both. Re-register to refresh the entry.",
            { TorrentFile: true, MagnetProtocol: false } => "WinBit handles .torrent but not magnet: links.",
            { TorrentFile: false, MagnetProtocol: true } => "WinBit handles magnet: but not .torrent files.",
            _ => "Neither is registered to WinBit.",
        };
    }
}
