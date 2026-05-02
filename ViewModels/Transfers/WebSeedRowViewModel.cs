using CommunityToolkit.Mvvm.ComponentModel;

namespace WinBit.ViewModels.Transfers;

public sealed partial class WebSeedRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _url = string.Empty;
}
