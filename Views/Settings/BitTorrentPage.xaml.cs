using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinBit.Core.Settings;
using WinBit.Views.Dialogs;

namespace WinBit.Views.Settings;

public sealed partial class BitTorrentPage : Page
{
    public BitTorrentPage() => InitializeComponent();

    private async void OnConfigureShareLimitsClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new ShareLimitsDialog(App.Services.GetRequiredService<ISettingsService>())
        {
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
