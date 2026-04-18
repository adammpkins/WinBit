using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace WinBit.Views.Settings;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Navigate("downloads");
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
            "downloads" => typeof(DownloadsPage),
            "connection" => typeof(ConnectionPage),
            "speed" => typeof(SpeedPage),
            "bittorrent" => typeof(BitTorrentPage),
            "rss" => typeof(RssPage),
            "webui" => typeof(WebUiPage),
            "advanced" => typeof(AdvancedPage),
            _ => typeof(DownloadsPage),
        };

        if (SectionFrame.CurrentSourcePageType == pageType)
        {
            return;
        }

        SectionFrame.Navigate(pageType, tag, new EntranceNavigationTransitionInfo());
    }
}
