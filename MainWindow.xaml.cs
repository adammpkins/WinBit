using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinBit.Core.BitTorrent;
using WinBit.Core.Categories;
using WinBit.Core.Settings;
using WinBit.Core.Sharing;
using WinBit.Core.Tags;
using WinBit.Services;
using WinBit.ViewModels.Shell;
using WinBit.Views.Dialogs;
using WinBit.Views.Shell;

namespace WinBit;

public sealed partial class MainWindow : Window
{
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IThemeService _themes;
    private readonly ISettingsService _settings;
    private bool _forceExit;

    public ShellStatusViewModel Status { get; }

    public ICommand ShowCommand { get; }
    public ICommand ToggleVisibilityCommand { get; }

    public MainWindow()
    {
        ShowCommand = new RelayCommand(ShowFromTray);
        ToggleVisibilityCommand = new RelayCommand(ToggleVisibilityFromTray);

        InitializeComponent();

        _navigation = App.Services.GetRequiredService<INavigationService>();
        _dialogs = App.Services.GetRequiredService<IDialogService>();
        _themes = App.Services.GetRequiredService<IThemeService>();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        Status = App.Services.GetRequiredService<ShellStatusViewModel>();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ApplyTaskbarIcon();

        _navigation.Initialize(RootFrame);
        RootFrame.Loaded += OnRootFrameLoaded;
        _themes.ThemeChanged += OnThemeChanged;
        AppWindow.Closing += OnAppWindowClosing;
        Closed += OnWindowClosed;

        TrayIcon.ForceCreate();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // The tray menu's Exit sets _forceExit before calling Application.Current.Exit(), so the
        // teardown path bypasses close-to-tray. A bare close-button press honors the setting.
        if (_forceExit)
        {
            return;
        }
        if (_settings.Current.Behavior.CloseToTray)
        {
            args.Cancel = true;
            HideToTray();
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        TrayIcon.Dispose();
    }

    private void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
    }

    private void HideToTray()
    {
        AppWindow.Hide();
    }

    private void ToggleVisibilityFromTray()
    {
        if (AppWindow.IsVisible)
        {
            HideToTray();
        }
        else
        {
            ShowFromTray();
        }
    }

    private void OnTrayShowClicked(object sender, RoutedEventArgs e) => ShowFromTray();

    private void OnTrayHideClicked(object sender, RoutedEventArgs e) => HideToTray();

    private async void OnTrayAltSpeedClicked(object sender, RoutedEventArgs e)
    {
        // ToggleMenuFlyoutItem flips IsChecked on click; revert so the menu reflects the
        // persisted setting that Status mirrors back via OneWay binding on the next tick.
        if (sender is ToggleMenuFlyoutItem item)
        {
            item.IsChecked = Status.AltSpeedEnabled;
        }
        await Status.ToggleAltSpeedAsync();
    }

    private void OnTrayExitClicked(object sender, RoutedEventArgs e)
    {
        _forceExit = true;
        Application.Current.Exit();
    }

    private void ApplyTaskbarIcon()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            foreach (var candidate in IconCandidatePaths())
            {
                if (File.Exists(candidate))
                {
                    appWindow.SetIcon(candidate);
                    return;
                }
            }
        }
        catch
        {
            // Icon application is best-effort — the taskbar falls back to the package manifest logo.
        }
    }

    private static IEnumerable<string> IconCandidatePaths()
    {
        // AppWindow.SetIcon requires .ico on WinAppSDK; try that first, fall back to the
        // multi-resolution .png only if the ico is missing for some reason.
        string[] names = { "AppIcon.ico", "AppIcon.png" };

        string? packagedRoot = null;
        try
        {
            packagedRoot = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
        }
        catch
        {
            // Not packaged; fall through to unpackaged probes.
        }

        foreach (var name in names)
        {
            if (packagedRoot is not null)
            {
                yield return Path.Combine(packagedRoot, "Assets", name);
            }
            yield return Path.Combine(AppContext.BaseDirectory, "AppX", "Assets", name);
            yield return Path.Combine(AppContext.BaseDirectory, "Assets", name);
        }
    }

    private void OnRootFrameLoaded(object sender, RoutedEventArgs e)
    {
        _dialogs.AttachRoot(Content.XamlRoot);
        _navigation.NavigateTo(typeof(ShellPage));
        ApplyThemeToRoot(_themes.CurrentTheme);
    }

    private void OnThemeChanged(object? sender, ElementTheme theme) => ApplyThemeToRoot(theme);

    private void ApplyThemeToRoot(ElementTheme theme)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }
    }

    private async void OnAddTorrentClicked(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dialog = new AddTorrentDialog(
            App.Services.GetRequiredService<ITorrentSessionService>(),
            App.Services.GetRequiredService<ISettingsService>(),
            App.Services.GetRequiredService<ICategoryService>(),
            App.Services.GetRequiredService<ITagService>(),
            App.Services.GetRequiredService<IShareLimitOverrideService>(),
            hwnd)
        {
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private async void OnAddMagnetClicked(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dialog = new AddMagnetDialog(
            App.Services.GetRequiredService<ITorrentSessionService>(),
            App.Services.GetRequiredService<ICategoryService>(),
            App.Services.GetRequiredService<ITagService>(),
            App.Services.GetRequiredService<IShareLimitOverrideService>(),
            App.Services.GetRequiredService<ISettingsService>(),
            hwnd)
        {
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private async void OnAddFromUrlClicked(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dialog = new DownloadFromUrlDialog(
            App.Services.GetRequiredService<ITorrentSessionService>(),
            App.Services.GetRequiredService<UrlDownloader>(),
            App.Services.GetRequiredService<ICategoryService>(),
            App.Services.GetRequiredService<ITagService>(),
            App.Services.GetRequiredService<IShareLimitOverrideService>(),
            App.Services.GetRequiredService<ISettingsService>(),
            hwnd)
        {
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Add to WinBit";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
            e.Handled = true;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var file = items.OfType<StorageFile>()
            .FirstOrDefault(f => string.Equals(f.FileType, ".torrent", StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dialog = new AddTorrentDialog(
            App.Services.GetRequiredService<ITorrentSessionService>(),
            App.Services.GetRequiredService<ISettingsService>(),
            App.Services.GetRequiredService<ICategoryService>(),
            App.Services.GetRequiredService<ITagService>(),
            App.Services.GetRequiredService<IShareLimitOverrideService>(),
            hwnd)
        {
            XamlRoot = Content.XamlRoot,
        };
        await dialog.PreloadTorrentAsync(file.Path);
        await dialog.ShowAsync();
    }

    private async void OnAltSpeedToggleClicked(object sender, RoutedEventArgs e)
    {
        // The ToggleButton auto-flips IsChecked on click; revert it so our toggle reflects the
        // persisted setting only. ShellStatusViewModel mirrors ISettingsService.Changed and
        // pushes AltSpeedEnabled back into both toggles via OneWay bindings.
        if (sender is ToggleButton toggle)
        {
            toggle.IsChecked = Status.AltSpeedEnabled;
        }
        await Status.ToggleAltSpeedAsync();
    }

    private void OnThemeClicked(object sender, RoutedEventArgs e)
    {
        var next = _themes.CurrentTheme switch
        {
            ElementTheme.Default => ElementTheme.Light,
            ElementTheme.Light => ElementTheme.Dark,
            ElementTheme.Dark => ElementTheme.Default,
            _ => ElementTheme.Default,
        };

        _themes.Apply(next);
    }
}
