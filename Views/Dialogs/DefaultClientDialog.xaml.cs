using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using WinBit.Core.Settings;
using WinBit.Core.Shell;

namespace WinBit.Views.Dialogs;

public sealed partial class DefaultClientDialog : ContentDialog
{
    private readonly IShellAssociationService _associations;
    private readonly ISettingsService _settings;

    public string TorrentStatusText { get; }
    public string MagnetStatusText { get; }

    public DefaultClientDialog(
        IShellAssociationService associations,
        ISettingsService settings,
        ShellAssociationStatus status)
    {
        InitializeComponent();
        _associations = associations;
        _settings = settings;

        TorrentStatusText = status.TorrentFile
            ? ".torrent files: already opened by WinBit"
            : ".torrent files: handled by another app (or unset)";
        MagnetStatusText = status.MagnetProtocol
            ? "magnet: links: already opened by WinBit"
            : "magnet: links: handled by another app (or unset)";

        PrimaryButtonClick += OnRegisterClicked;
        SecondaryButtonClick += OnOpenSettingsClicked;
        Closing += OnClosing;
    }

    private async void OnRegisterClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await _associations.RegisterAsync(torrent: true, magnet: true);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnOpenSettingsClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // ms-settings:defaultapps lands the user on the per-app default-handler page. Fire-and-
        // forget — any error is non-fatal and the dialog still closes.
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:defaultapps",
                UseShellExecute = true,
            });
        }
        catch
        {
            // Ignore — the user can still reach Default apps manually.
        }
    }

    private async void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // Any exit path counts as "don't ask again". Manual registration from Settings bypasses
        // this policy.
        await _settings.UpdateAsync(s => s.Behavior.DefaultClientPromptDismissed = true);
    }
}
