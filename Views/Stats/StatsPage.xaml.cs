using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using WinBit.ViewModels.Stats;

namespace WinBit.Views.Stats;

public sealed partial class StatsPage : Page
{
    public StatsViewModel ViewModel { get; }

    public StatsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<StatsViewModel>();
        Loaded += (_, _) => ViewModel.Refresh();
    }
}
