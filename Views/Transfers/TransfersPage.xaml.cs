using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinBit.Services;
using WinBit.ViewModels.Transfers;

namespace WinBit.Views.Transfers;

public sealed partial class TransfersPage : Page
{
    public TransfersViewModel ViewModel { get; }

    public TransfersPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<TransfersViewModel>();
    }

    private async void OnAddTorrentClicked(object sender, RoutedEventArgs e)
    {
        await App.Services.GetRequiredService<IDialogService>()
            .ShowAsync("Add torrent", "Torrent adding lands in milestone M3.");
    }

    private async void OnAddMagnetClicked(object sender, RoutedEventArgs e)
    {
        await App.Services.GetRequiredService<IDialogService>()
            .ShowAsync("Add magnet", "Magnet adding lands in milestone M3.");
    }
}
