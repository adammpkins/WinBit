using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinBit.Services;
using WinBit.Views.Shell;

namespace WinBit;

public sealed partial class MainWindow : Window
{
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IThemeService _themes;

    public MainWindow()
    {
        InitializeComponent();

        _navigation = App.Services.GetRequiredService<INavigationService>();
        _dialogs = App.Services.GetRequiredService<IDialogService>();
        _themes = App.Services.GetRequiredService<IThemeService>();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        _navigation.Initialize(RootFrame);
        RootFrame.Loaded += OnRootFrameLoaded;
        _themes.ThemeChanged += OnThemeChanged;
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
        await _dialogs.ShowAsync("Add torrent", "Torrent adding lands in milestone M3.");
    }

    private async void OnAddMagnetClicked(object sender, RoutedEventArgs e)
    {
        await _dialogs.ShowAsync("Add magnet", "Magnet adding lands in milestone M3.");
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
