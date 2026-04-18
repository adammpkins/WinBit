using FluentAssertions;
using WinBit.Core.Sharing;
using Xunit;

namespace WinBit.Tests;

/// <summary>
/// Parity tests for <see cref="ShareLimitEvaluator"/>. Fixtures derive from
/// <c>qbittorrent/src/base/bittorrent/sessionimpl.cpp</c> — <c>processTorrentShareLimits</c>
/// (lines 2345–2423).
/// </summary>
public sealed class ShareLimitEvaluatorTests
{
    private static ShareLimitInputs Finished(
        double ratio = 0.0,
        TimeSpan? seedingTime = null,
        TimeSpan? inactiveSeedingTime = null,
        bool isForced = false,
        bool isStopped = false,
        bool isSuperSeeding = false) =>
        new(
            IsFinished: true,
            IsForced: isForced,
            IsStopped: isStopped,
            IsSuperSeeding: isSuperSeeding,
            Ratio: ratio,
            SeedingTime: seedingTime ?? TimeSpan.Zero,
            InactiveSeedingTime: inactiveSeedingTime ?? TimeSpan.Zero);

    [Fact]
    public void Unfinished_torrents_are_never_touched()
    {
        var limits = new ShareLimits { RatioLimit = 1.0, Action = ShareLimitAction.Stop };
        var inputs = Finished(ratio: 10.0) with { IsFinished = false };
        ShareLimitEvaluator.Evaluate(limits, inputs).Should().Be(ShareLimitDecision.NoAction);
    }

    [Fact]
    public void Forced_torrents_bypass_enforcement_even_above_limits()
    {
        var limits = new ShareLimits { RatioLimit = 1.0, Action = ShareLimitAction.Stop };
        var inputs = Finished(ratio: 10.0, isForced: true);
        ShareLimitEvaluator.Evaluate(limits, inputs).Should().Be(ShareLimitDecision.NoAction);
    }

    [Fact]
    public void MatchAny_ratio_cap_triggers_stop()
    {
        var limits = new ShareLimits { RatioLimit = 2.0, Action = ShareLimitAction.Stop };
        ShareLimitEvaluator.Evaluate(limits, Finished(ratio: 2.0))
            .Should().Be(ShareLimitDecision.Stop);
    }

    [Fact]
    public void MatchAny_below_ratio_does_not_trigger()
    {
        var limits = new ShareLimits { RatioLimit = 2.0, Action = ShareLimitAction.Stop };
        ShareLimitEvaluator.Evaluate(limits, Finished(ratio: 1.99))
            .Should().Be(ShareLimitDecision.NoAction);
    }

    [Fact]
    public void MatchAny_seeding_time_cap_triggers_stop()
    {
        var limits = new ShareLimits
        {
            SeedingTimeLimit = TimeSpan.FromHours(1),
            Action = ShareLimitAction.Stop,
        };
        ShareLimitEvaluator.Evaluate(limits, Finished(seedingTime: TimeSpan.FromHours(1)))
            .Should().Be(ShareLimitDecision.Stop);
    }

    [Fact]
    public void MatchAny_inactive_seeding_time_cap_triggers_stop()
    {
        var limits = new ShareLimits
        {
            InactiveSeedingTimeLimit = TimeSpan.FromMinutes(30),
            Action = ShareLimitAction.Stop,
        };
        ShareLimitEvaluator.Evaluate(limits, Finished(inactiveSeedingTime: TimeSpan.FromMinutes(31)))
            .Should().Be(ShareLimitDecision.Stop);
    }

    [Fact]
    public void MatchAny_with_no_limits_never_triggers()
    {
        var limits = new ShareLimits { Action = ShareLimitAction.Stop };
        ShareLimitEvaluator.Evaluate(limits, Finished(ratio: 100.0))
            .Should().Be(ShareLimitDecision.NoAction);
    }

    [Fact]
    public void MatchAll_requires_every_set_cap_to_be_met()
    {
        var limits = new ShareLimits
        {
            RatioLimit = 2.0,
            SeedingTimeLimit = TimeSpan.FromHours(2),
            Mode = ShareLimitsMode.MatchAll,
            Action = ShareLimitAction.Stop,
        };

        // Ratio met but seeding time short → not reached.
        ShareLimitEvaluator.Evaluate(limits, Finished(ratio: 5.0, seedingTime: TimeSpan.FromHours(1)))
            .Should().Be(ShareLimitDecision.NoAction);

        // Seeding time met but ratio short → not reached.
        ShareLimitEvaluator.Evaluate(limits, Finished(ratio: 0.5, seedingTime: TimeSpan.FromHours(3)))
            .Should().Be(ShareLimitDecision.NoAction);

        // Both met → triggers.
        ShareLimitEvaluator.Evaluate(limits, Finished(ratio: 5.0, seedingTime: TimeSpan.FromHours(3)))
            .Should().Be(ShareLimitDecision.Stop);
    }

    [Fact]
    public void MatchAll_ignores_unset_caps_when_checking_meetings()
    {
        var limits = new ShareLimits
        {
            RatioLimit = 2.0,
            // seedingTime + inactiveSeedingTime unset → should not disqualify.
            Mode = ShareLimitsMode.MatchAll,
            Action = ShareLimitAction.Stop,
        };
        ShareLimitEvaluator.Evaluate(limits, Finished(ratio: 2.0))
            .Should().Be(ShareLimitDecision.Stop);
    }

    [Fact]
    public void MatchAll_with_no_limits_is_treated_as_not_reached()
    {
        var limits = new ShareLimits
        {
            Mode = ShareLimitsMode.MatchAll,
            Action = ShareLimitAction.Stop,
        };
        ShareLimitEvaluator.Evaluate(limits, Finished(ratio: 100.0))
            .Should().Be(ShareLimitDecision.NoAction);
    }

    [Fact]
    public void Stop_action_is_suppressed_when_already_stopped()
    {
        var limits = new ShareLimits { RatioLimit = 1.0, Action = ShareLimitAction.Stop };
        ShareLimitEvaluator.Evaluate(limits, Finished(ratio: 5.0, isStopped: true))
            .Should().Be(ShareLimitDecision.NoAction);
    }

    [Fact]
    public void Remove_action_fires_regardless_of_stopped_state()
    {
        var limits = new ShareLimits { RatioLimit = 1.0, Action = ShareLimitAction.Remove };
        ShareLimitEvaluator.Evaluate(limits, Finished(ratio: 5.0))
            .Should().Be(ShareLimitDecision.Remove);
        ShareLimitEvaluator.Evaluate(limits, Finished(ratio: 5.0, isStopped: true))
            .Should().Be(ShareLimitDecision.Remove);
    }

    [Fact]
    public void RemoveWithContent_is_returned_verbatim_when_reached()
    {
        var limits = new ShareLimits { RatioLimit = 1.0, Action = ShareLimitAction.RemoveWithContent };
        ShareLimitEvaluator.Evaluate(limits, Finished(ratio: 2.0))
            .Should().Be(ShareLimitDecision.RemoveWithContent);
    }

    [Fact]
    public void EnableSuperSeeding_is_suppressed_when_stopped_or_already_super_seeding()
    {
        var limits = new ShareLimits { RatioLimit = 1.0, Action = ShareLimitAction.EnableSuperSeeding };
        ShareLimitEvaluator.Evaluate(limits, Finished(ratio: 2.0))
            .Should().Be(ShareLimitDecision.EnableSuperSeeding);
        ShareLimitEvaluator.Evaluate(limits, Finished(ratio: 2.0, isStopped: true))
            .Should().Be(ShareLimitDecision.NoAction);
        ShareLimitEvaluator.Evaluate(limits, Finished(ratio: 2.0, isSuperSeeding: true))
            .Should().Be(ShareLimitDecision.NoAction);
    }

    [Fact]
    public void Default_action_is_normalized_to_stop()
    {
        // qBittorrent normalizes Default → Stop at load time (sessionimpl.cpp:559-560, 1235-1237).
        var limits = new ShareLimits { RatioLimit = 1.0, Action = ShareLimitAction.Default };
        ShareLimitEvaluator.Evaluate(limits, Finished(ratio: 2.0))
            .Should().Be(ShareLimitDecision.Stop);
    }

    [Fact]
    public void Default_mode_is_treated_as_match_any()
    {
        var limits = new ShareLimits
        {
            RatioLimit = 2.0,
            SeedingTimeLimit = TimeSpan.FromHours(100),
            Mode = ShareLimitsMode.Default,
            Action = ShareLimitAction.Stop,
        };
        // Ratio met, seeding time nowhere near — under MatchAny this should still trigger.
        ShareLimitEvaluator.Evaluate(limits, Finished(ratio: 5.0, seedingTime: TimeSpan.FromHours(1)))
            .Should().Be(ShareLimitDecision.Stop);
    }
}
