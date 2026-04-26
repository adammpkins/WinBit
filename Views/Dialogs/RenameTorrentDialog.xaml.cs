using Microsoft.UI.Xaml.Controls;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;

namespace WinBit.Views.Dialogs;

public sealed partial class RenameTorrentDialog : ContentDialog
{
    private readonly ITorrentSessionService _session;
    private readonly TorrentId _target;

    public RenameTorrentDialog(ITorrentSessionService session, TorrentId target, string currentName)
    {
        InitializeComponent();
        _session = session;
        _target = target;
        NameBox.Text = currentName;
        PrimaryButtonClick += OnRenameClicked;
    }

    private async void OnRenameClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            ErrorBar.IsOpen = false;
            var newName = NameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                ErrorBar.Message = "Name must not be empty.";
                ErrorBar.IsOpen = true;
                args.Cancel = true;
                return;
            }

            var result = await _session.SetNameAsync(_target, newName);
            if (!result.IsSuccess)
            {
                ErrorBar.Message = result.Error;
                ErrorBar.IsOpen = true;
                args.Cancel = true;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }
}
