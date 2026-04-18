using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinBit.Core.Settings;
using WinBit.Core.Shell;

namespace WinBit.Views.Dialogs;

public sealed partial class FirstRunWizard : ContentDialog
{
    private readonly ISettingsService _settings;
    private readonly IShellAssociationService? _associations;
    private readonly nint _ownerHwnd;

    public FirstRunWizard(
        ISettingsService settings,
        IShellAssociationService? associations,
        nint ownerHwnd)
    {
        InitializeComponent();
        _settings = settings;
        _associations = associations;
        _ownerHwnd = ownerHwnd;

        SavePathBox.Text = settings.Current.Downloads.DefaultSavePath
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        RegisterDefaultsToggle.IsOn = _associations is not null;
        RegisterDefaultsToggle.IsEnabled = _associations is not null;
        EnableWebUiToggle.IsOn = settings.Current.WebUi.Enabled;

        PrimaryButtonClick += OnConfirmAsync;
        Closed += OnClosed;
    }

    private async void OnBrowseSavePathClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _ownerHwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            SavePathBox.Text = folder.Path;
        }
    }

    private async void OnConfirmAsync(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var savePath = SavePathBox.Text.Trim();
            var enableWebUi = EnableWebUiToggle.IsOn;
            await _settings.UpdateAsync(s =>
            {
                if (!string.IsNullOrWhiteSpace(savePath))
                {
                    s.Downloads.DefaultSavePath = savePath;
                }
                s.WebUi.Enabled = enableWebUi;
            });

            if (RegisterDefaultsToggle.IsOn && _associations is not null)
            {
                await _associations.RegisterAsync(torrent: true, magnet: true);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        // Regardless of primary/secondary/close, we only ever show this once. Also mark the
        // default-client prompt as dismissed so it doesn't fire on the same startup.
        await _settings.UpdateAsync(s =>
        {
            s.Behavior.FirstRunComplete = true;
            s.Behavior.DefaultClientPromptDismissed = true;
        });
    }
}
