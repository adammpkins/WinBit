using Microsoft.UI.Xaml.Controls;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;

namespace WinBit.Views.Dialogs;

/// <summary>
/// Edits per-torrent MaximumDownloadRate / MaximumUploadRate on the MonoTorrent
/// <c>TorrentManager</c>. UI exposes KB/s for sanity; we convert to bytes at save time.
/// </summary>
public sealed partial class PerTorrentSpeedLimitDialog : ContentDialog
{
    private const int BytesPerKilobyte = 1024;

    private readonly ITorrentSessionService _session;
    private readonly IReadOnlyList<TorrentId> _targets;

    public PerTorrentSpeedLimitDialog(ITorrentSessionService session, IReadOnlyList<TorrentId> targets)
    {
        InitializeComponent();
        _session = session;
        _targets = targets;

        TargetLabel.Text = targets.Count == 1
            ? $"Editing 1 torrent ({targets[0].Value[..8]}…)"
            : $"Editing {targets.Count} torrents.";

        if (_targets.Count > 0)
        {
            var current = _session.GetSpeedLimits(_targets[0]);
            if (current is { } c)
            {
                DownloadEnabled.IsOn = c.DownloadBps > 0;
                if (c.DownloadBps > 0)
                {
                    DownloadKBps.Value = Math.Max(1, c.DownloadBps / BytesPerKilobyte);
                }

                UploadEnabled.IsOn = c.UploadBps > 0;
                if (c.UploadBps > 0)
                {
                    UploadKBps.Value = Math.Max(1, c.UploadBps / BytesPerKilobyte);
                }
            }
        }

        PrimaryButtonClick += OnSaveClicked;
    }

    private async void OnSaveClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            long? downloadBps = DownloadEnabled.IsOn
                ? (long)DownloadKBps.Value * BytesPerKilobyte
                : 0L;
            long? uploadBps = UploadEnabled.IsOn
                ? (long)UploadKBps.Value * BytesPerKilobyte
                : 0L;

            var failures = new List<string>();
            foreach (var id in _targets)
            {
                var result = await _session.SetSpeedLimitsAsync(id, downloadBps, uploadBps);
                if (!result.IsSuccess)
                {
                    failures.Add($"{id.Value[..8]}: {result.Error}");
                }
            }

            if (failures.Count > 0)
            {
                ErrorBar.Message = string.Join('\n', failures);
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
