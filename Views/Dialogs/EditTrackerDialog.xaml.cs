using Microsoft.UI.Xaml.Controls;

namespace WinBit.Views.Dialogs;

public sealed partial class EditTrackerDialog : ContentDialog
{
    public EditTrackerDialog(string currentUrl, int currentTier)
    {
        InitializeComponent();
        UrlBox.Text = currentUrl;
        TierBox.Value = currentTier;
        // URL is already populated; enable Save immediately.
        IsPrimaryButtonEnabled = true;
    }

    public string TrackerUrl => UrlBox.Text.Trim();
    public int Tier => (int)TierBox.Value;

    private void OnUrlChanged(object sender, TextChangedEventArgs e)
    {
        IsPrimaryButtonEnabled = Uri.TryCreate(UrlBox.Text.Trim(), UriKind.Absolute, out _);
    }
}
