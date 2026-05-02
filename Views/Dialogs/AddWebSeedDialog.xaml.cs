using Microsoft.UI.Xaml.Controls;

namespace WinBit.Views.Dialogs;

public sealed partial class AddWebSeedDialog : ContentDialog
{
    public AddWebSeedDialog()
    {
        InitializeComponent();
        IsPrimaryButtonEnabled = false;
    }

    public string SeedUrl => UrlBox.Text.Trim();

    private void OnUrlChanged(object sender, TextChangedEventArgs e)
    {
        IsPrimaryButtonEnabled = Uri.TryCreate(UrlBox.Text.Trim(), UriKind.Absolute, out _);
    }
}
