using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinBit.ViewModels.Logs;

namespace WinBit.Views.Logs;

public sealed partial class LogsPage : Page
{
    public LogsViewModel ViewModel { get; }

    public PeerLogViewModel PeerViewModel { get; }

    public LogsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<LogsViewModel>();
        PeerViewModel = App.Services.GetRequiredService<PeerLogViewModel>();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Both VMs are singletons — scroll each tab to the newest row whenever we return.
        if (LogList.Items.Count > 0)
        {
            LogList.ScrollIntoView(LogList.Items[^1]);
        }
        if (PeerList.Items.Count > 0)
        {
            PeerList.ScrollIntoView(PeerList.Items[^1]);
        }
    }

    private void OnClearExecutionClicked(object sender, RoutedEventArgs e) => ViewModel.ClearView();

    private void OnClearPeersClicked(object sender, RoutedEventArgs e) => PeerViewModel.ClearView();
}
