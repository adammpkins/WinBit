using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinBit.Core.BitTorrent;
using WinBit.Core.Categories;
using WinBit.Core.Settings;
using WinBit.Core.Sharing;
using WinBit.Core.Tags;
using WinBit.ViewModels.Search;
using WinBit.Views.Dialogs;

namespace WinBit.Views.Search;

public sealed partial class SearchPage : Page
{
    public SearchViewModel ViewModel { get; }

    public SearchPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SearchViewModel>();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Plugin set may have changed between page navigations (settings edits) — resync.
        ViewModel.RefreshPlugins();
    }

    private async void OnSearchClicked(object sender, RoutedEventArgs e) => await ViewModel.RunAsync();

    private async void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            await ViewModel.RunAsync();
        }
    }

    private async void OnAddClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not SearchHitRowViewModel row)
        {
            return;
        }

        var hwnd = App.MainWindow is { } w ? WinRT.Interop.WindowNative.GetWindowHandle(w) : 0;
        var xamlRoot = XamlRoot;

        if (!string.IsNullOrEmpty(row.Hit.MagnetUri))
        {
            var dialog = new AddMagnetDialog(
                App.Services.GetRequiredService<ITorrentSessionService>(),
                App.Services.GetRequiredService<ICategoryService>(),
                App.Services.GetRequiredService<ITagService>(),
                App.Services.GetRequiredService<IShareLimitOverrideService>(),
                App.Services.GetRequiredService<ISettingsService>(),
                hwnd)
            {
                XamlRoot = xamlRoot,
            };
            dialog.SetMagnet(row.Hit.MagnetUri);
            await dialog.ShowAsync();
        }
        else if (!string.IsNullOrEmpty(row.Hit.TorrentUrl))
        {
            var dialog = new DownloadFromUrlDialog(
                App.Services.GetRequiredService<ITorrentSessionService>(),
                App.Services.GetRequiredService<UrlDownloader>(),
                App.Services.GetRequiredService<ICategoryService>(),
                App.Services.GetRequiredService<ITagService>(),
                App.Services.GetRequiredService<IShareLimitOverrideService>(),
                App.Services.GetRequiredService<ISettingsService>(),
                hwnd)
            {
                XamlRoot = xamlRoot,
            };
            dialog.SetUrl(row.Hit.TorrentUrl);
            await dialog.ShowAsync();
        }
    }

    private string ResultCountText(int count) => count switch
    {
        0 => string.Empty,
        1 => "1 result",
        _ => $"{count:N0} results",
    };

    private Visibility HasHits(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;
}
