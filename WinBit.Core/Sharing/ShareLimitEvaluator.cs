namespace WinBit.Core.Sharing;

/// <summary>
/// The action an enforcement tick should take against a single torrent, or
/// <see cref="NoAction"/> if the torrent is outside the enforcement window (not finished,
/// forced, below its caps, or already in the target state).
/// </summary>
public enum ShareLimitDecision
{
    NoAction,
    Stop,
    Remove,
    RemoveWithContent,
    EnableSuperSeeding,
}

/// <summary>
/// Snapshot of a torrent's share-limit inputs at the moment the evaluator runs. Mirrors the
/// fields qBittorrent's <c>SessionImpl::processTorrentShareLimits</c> reads off
/// <c>TorrentImpl</c> (see <c>qbittorrent/src/base/bittorrent/sessionimpl.cpp</c> lines
/// 2345–2423). The enforcement hosted service (separate deliverable) supplies these.
/// </summary>
public readonly record struct ShareLimitInputs(
    bool IsFinished,
    bool IsForced,
    bool IsStopped,
    bool IsSuperSeeding,
    double Ratio,
    TimeSpan SeedingTime,
    TimeSpan InactiveSeedingTime);

/// <summary>
/// Pure, side-effect-free port of qBittorrent's <c>SessionImpl::processTorrentShareLimits</c>.
/// The enforcement hosted service is responsible for invoking this per torrent per tick and
/// applying the returned <see cref="ShareLimitDecision"/> via <c>ITorrentSessionService</c>.
/// </summary>
public static class ShareLimitEvaluator
{
    public static ShareLimitDecision Evaluate(ShareLimits limits, ShareLimitInputs inputs)
    {
        // qBittorrent skips anything that isn't a fully-completed, non-forced torrent.
        if (!inputs.IsFinished || inputs.IsForced)
        {
            return ShareLimitDecision.NoAction;
        }

        if (!AreLimitsReached(limits, inputs))
        {
            return ShareLimitDecision.NoAction;
        }

        // ShareLimitAction.Default is a per-torrent inheritance marker. qBittorrent normalizes
        // Default → Stop at load time; mirror that here so Evaluate is robust if callers hand
        // us an un-merged per-torrent limits record.
        var action = limits.Action == ShareLimitAction.Default ? ShareLimitAction.Stop : limits.Action;

        return action switch
        {
            ShareLimitAction.Remove => ShareLimitDecision.Remove,
            ShareLimitAction.RemoveWithContent => ShareLimitDecision.RemoveWithContent,
            ShareLimitAction.Stop => inputs.IsStopped ? ShareLimitDecision.NoAction : ShareLimitDecision.Stop,
            ShareLimitAction.EnableSuperSeeding when inputs.IsStopped || inputs.IsSuperSeeding
                => ShareLimitDecision.NoAction,
            ShareLimitAction.EnableSuperSeeding => ShareLimitDecision.EnableSuperSeeding,
            _ => ShareLimitDecision.NoAction,
        };
    }

    private static bool AreLimitsReached(ShareLimits limits, ShareLimitInputs inputs)
    {
        var mode = limits.Mode == ShareLimitsMode.Default ? ShareLimitsMode.MatchAny : limits.Mode;

        if (mode == ShareLimitsMode.MatchAny)
        {
            return (limits.RatioLimit.HasValue && inputs.Ratio >= limits.RatioLimit.Value)
                || (limits.SeedingTimeLimit.HasValue && inputs.SeedingTime >= limits.SeedingTimeLimit.Value)
                || (limits.InactiveSeedingTimeLimit.HasValue && inputs.InactiveSeedingTime >= limits.InactiveSeedingTimeLimit.Value);
        }

        // MatchAll: every *set* cap must be met or exceeded. An unset cap is skipped without
        // disqualifying the torrent (mirrors qBittorrent's else-if chain). If nothing is set,
        // we treat the torrent as not-yet-at-limit rather than trigger an accidental action —
        // qBittorrent only invokes processTorrentShareLimits from a timer that's only armed
        // when at least one cap exists, so this branch is never hit upstream either way.
        if (!limits.RatioLimit.HasValue
            && !limits.SeedingTimeLimit.HasValue
            && !limits.InactiveSeedingTimeLimit.HasValue)
        {
            return false;
        }

        if (limits.RatioLimit.HasValue && inputs.Ratio < limits.RatioLimit.Value)
        {
            return false;
        }
        if (limits.SeedingTimeLimit.HasValue && inputs.SeedingTime < limits.SeedingTimeLimit.Value)
        {
            return false;
        }
        if (limits.InactiveSeedingTimeLimit.HasValue && inputs.InactiveSeedingTime < limits.InactiveSeedingTimeLimit.Value)
        {
            return false;
        }
        return true;
    }
}
