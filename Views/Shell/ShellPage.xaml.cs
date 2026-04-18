using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using WinBit.ViewModels.Shell;
using WinBit.Views.Logs;
using WinBit.Views.Rss;
using WinBit.Views.Settings;
using WinBit.Views.Stats;
using WinBit.Views.TorrentCreator;
using WinBit.Views.Transfers;

namespace WinBit.Views.Shell;

public sealed partial class ShellPage : Page
{
    public ShellViewModel ViewModel { get; }

    public ShellPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<ShellViewModel>();
        Loaded += (_, _) => Navigate("transfers");
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item && item.Tag is string tag)
        {
            Navigate(tag);
        }
    }

    private void Navigate(string tag)
    {
        var pageType = tag switch
        {
            "transfers" => typeof(TransfersPage),
            "stats" => typeof(StatsPage),
            "logs" => typeof(LogsPage),
            "creator" => typeof(TorrentCreatorPage),
            "rss" => typeof(RssReaderPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(ComingSoonPage),
        };

        if (ContentFrame.CurrentSourcePageType == pageType)
        {
            return;
        }

        ContentFrame.Navigate(pageType, tag, new EntranceNavigationTransitionInfo());
    }
}
