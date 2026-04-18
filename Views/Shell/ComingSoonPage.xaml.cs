using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace WinBit.Views.Shell;

public sealed partial class ComingSoonPage : Page
{
    private static readonly Dictionary<string, string> Titles = new()
    {
        ["rss"] = "RSS",
        ["search"] = "Search",
        ["logs"] = "Logs",
        ["creator"] = "Torrent creator",
        ["stats"] = "Statistics",
        ["settings"] = "Settings",
    };

    public ComingSoonPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string tag && Titles.TryGetValue(tag, out var title))
        {
            AreaTitle.Text = title;
        }
    }
}
