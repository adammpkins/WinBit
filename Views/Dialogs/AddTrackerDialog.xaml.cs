using Microsoft.UI.Xaml.Controls;

namespace WinBit.Views.Dialogs;

public sealed partial class AddTrackerDialog : ContentDialog
{
    public AddTrackerDialog()
    {
        InitializeComponent();
        IsPrimaryButtonEnabled = false;
    }

    public string TrackerUrl => UrlBox.Text.Trim();
    public int Tier => (int)TierBox.Value;

    private void OnUrlChanged(object sender, TextChangedEventArgs e)
    {
        IsPrimaryButtonEnabled = Uri.TryCreate(UrlBox.Text.Trim(), UriKind.Absolute, out _);
    }
}
