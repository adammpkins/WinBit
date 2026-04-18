using Microsoft.UI.Xaml.Controls;
using WinBit.Core.Common;
using WinBit.Core.Sharing;

namespace WinBit.Views.Dialogs;

/// <summary>
/// Edits per-torrent <see cref="PerTorrentShareLimitOverride"/> entries. Null fields mean
/// "inherit from global"; <see cref="ShareLimitAction.Default"/> / <see cref="ShareLimitsMode.Default"/>
/// do the same for action/mode. When every field is inheriting, Save removes the override row
/// rather than persisting a redundant record.
/// </summary>
public sealed partial class PerTorrentShareLimitDialog : ContentDialog
{
    private readonly IShareLimitOverrideService _overrides;
    private readonly IReadOnlyList<TorrentId> _targets;

    public PerTorrentShareLimitDialog(IShareLimitOverrideService overrides, IReadOnlyList<TorrentId> targets)
    {
        InitializeComponent();
        _overrides = overrides;
        _targets = targets;

        TargetLabel.Text = targets.Count == 1
            ? $"Editing 1 torrent ({targets[0].Value[..8]}…)."
            : $"Editing {targets.Count} torrents — all selected torrents get the same override.";

        PrimaryButtonClick += OnSaveClicked;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_targets.Count == 0)
        {
            return;
        }

        var existing = await _overrides.GetAsync(_targets[0]);
        if (existing is null)
        {
            return;
        }

        RatioEnabled.IsOn = existing.RatioLimit.HasValue;
        if (existing.RatioLimit.HasValue)
        {
            RatioValue.Value = existing.RatioLimit.Value;
        }

        SeedingTimeEnabled.IsOn = existing.SeedingTimeLimit.HasValue;
        if (existing.SeedingTimeLimit.HasValue)
        {
            SeedingTimeValue.Value = existing.SeedingTimeLimit.Value.TotalMinutes;
        }

        InactiveEnabled.IsOn = existing.InactiveSeedingTimeLimit.HasValue;
        if (existing.InactiveSeedingTimeLimit.HasValue)
        {
            InactiveValue.Value = existing.InactiveSeedingTimeLimit.Value.TotalMinutes;
        }

        ModeCombo.SelectedIndex = existing.Mode switch
        {
            ShareLimitsMode.MatchAny => 1,
            ShareLimitsMode.MatchAll => 2,
            _ => 0,
        };

        ActionCombo.SelectedIndex = existing.Action switch
        {
            ShareLimitAction.Stop => 1,
            ShareLimitAction.Remove => 2,
            ShareLimitAction.RemoveWithContent => 3,
            ShareLimitAction.EnableSuperSeeding => 4,
            _ => 0,
        };
    }

    private async void OnSaveClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var ratio = RatioEnabled.IsOn ? (double?)RatioValue.Value : null;
            var seeding = SeedingTimeEnabled.IsOn
                ? (TimeSpan?)TimeSpan.FromMinutes(SeedingTimeValue.Value)
                : null;
            var inactive = InactiveEnabled.IsOn
                ? (TimeSpan?)TimeSpan.FromMinutes(InactiveValue.Value)
                : null;

            var mode = ModeCombo.SelectedIndex switch
            {
                1 => ShareLimitsMode.MatchAny,
                2 => ShareLimitsMode.MatchAll,
                _ => ShareLimitsMode.Default,
            };
            var action = ActionCombo.SelectedIndex switch
            {
                1 => ShareLimitAction.Stop,
                2 => ShareLimitAction.Remove,
                3 => ShareLimitAction.RemoveWithContent,
                4 => ShareLimitAction.EnableSuperSeeding,
                _ => ShareLimitAction.Default,
            };

            var allInherited = ratio is null && seeding is null && inactive is null
                && mode == ShareLimitsMode.Default && action == ShareLimitAction.Default;

            var failures = new List<string>();
            foreach (var id in _targets)
            {
                try
                {
                    if (allInherited)
                    {
                        await _overrides.RemoveAsync(id);
                    }
                    else
                    {
                        await _overrides.UpsertAsync(new PerTorrentShareLimitOverride
                        {
                            Id = id,
                            RatioLimit = ratio,
                            SeedingTimeLimit = seeding,
                            InactiveSeedingTimeLimit = inactive,
                            Mode = mode,
                            Action = action,
                        });
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{id.Value[..8]}: {ex.Message}");
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
