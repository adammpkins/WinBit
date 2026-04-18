using Microsoft.UI.Xaml.Controls;
using WinBit.Core.Settings;
using WinBit.Core.Sharing;

namespace WinBit.Views.Dialogs;

/// <summary>
/// Edits the global <see cref="ShareLimits"/> stored in <c>AppSettings.BitTorrent</c>. Per-
/// torrent overrides are a future M5 sub-item — they need engine-level enforcement, which the
/// dialog alone can't deliver.
/// </summary>
public sealed partial class ShareLimitsDialog : ContentDialog
{
    private readonly ISettingsService _settings;

    public ShareLimitsDialog(ISettingsService settings)
    {
        InitializeComponent();
        _settings = settings;

        Load(settings.Current.BitTorrent.GlobalShareLimits);
        PrimaryButtonClick += OnSaveClicked;
    }

    private void Load(ShareLimits limits)
    {
        RatioEnabled.IsOn = limits.RatioLimit.HasValue;
        if (limits.RatioLimit.HasValue)
        {
            RatioValue.Value = limits.RatioLimit.Value;
        }

        SeedingTimeEnabled.IsOn = limits.SeedingTimeLimit.HasValue;
        if (limits.SeedingTimeLimit.HasValue)
        {
            SeedingTimeValue.Value = limits.SeedingTimeLimit.Value.TotalMinutes;
        }

        InactiveEnabled.IsOn = limits.InactiveSeedingTimeLimit.HasValue;
        if (limits.InactiveSeedingTimeLimit.HasValue)
        {
            InactiveValue.Value = limits.InactiveSeedingTimeLimit.Value.TotalMinutes;
        }

        ModeCombo.SelectedIndex = limits.Mode == ShareLimitsMode.MatchAll ? 1 : 0;
        ActionCombo.SelectedIndex = limits.Action switch
        {
            ShareLimitAction.Remove => 1,
            ShareLimitAction.RemoveWithContent => 2,
            ShareLimitAction.EnableSuperSeeding => 3,
            _ => 0,
        };
    }

    private async void OnSaveClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var limits = new ShareLimits
        {
            RatioLimit = RatioEnabled.IsOn ? RatioValue.Value : null,
            SeedingTimeLimit = SeedingTimeEnabled.IsOn
                ? TimeSpan.FromMinutes(SeedingTimeValue.Value)
                : null,
            InactiveSeedingTimeLimit = InactiveEnabled.IsOn
                ? TimeSpan.FromMinutes(InactiveValue.Value)
                : null,
            Mode = ModeCombo.SelectedIndex == 1 ? ShareLimitsMode.MatchAll : ShareLimitsMode.MatchAny,
            Action = ActionCombo.SelectedIndex switch
            {
                1 => ShareLimitAction.Remove,
                2 => ShareLimitAction.RemoveWithContent,
                3 => ShareLimitAction.EnableSuperSeeding,
                _ => ShareLimitAction.Stop,
            },
        };

        await _settings.UpdateAsync(s => s.BitTorrent.GlobalShareLimits = limits);
    }
}
