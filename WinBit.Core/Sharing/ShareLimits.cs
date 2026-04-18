namespace WinBit.Core.Sharing;

/// <summary>
/// Action to take when a share-limit trigger fires. Ports <c>BitTorrent::ShareLimitAction</c>
/// from <c>qbittorrent/src/base/bittorrent/sharelimits.h</c>. <c>Default</c> is used for
/// category/torrent overrides to mean "inherit from global"; it is not valid on the global
/// ShareLimits itself.
/// </summary>
public enum ShareLimitAction
{
    Default = -1,
    Stop = 0,
    Remove = 1,
    EnableSuperSeeding = 2,
    RemoveWithContent = 3,
}

/// <summary>How multiple limits combine. Matches qBittorrent's <c>ShareLimitsMode</c>.</summary>
public enum ShareLimitsMode
{
    Default = -1,
    MatchAny = 0,
    MatchAll = 1,
}

/// <summary>
/// qBittorrent's <c>BitTorrent::ShareLimits</c> (from <c>sharelimits.h</c>) ported to idiomatic
/// C#. Null limit values mean "unlimited"; qBittorrent's -1 / -2 sentinels are replaced with
/// nullable .NET types. <see cref="ShareLimitAction.Default"/> / <see cref="ShareLimitsMode.Default"/>
/// are reserved for per-category/per-torrent overrides that should fall back to the global
/// configuration.
/// </summary>
public sealed record ShareLimits
{
    /// <summary>Upload/download ratio cap. Null = no ratio limit.</summary>
    public double? RatioLimit { get; init; }

    /// <summary>Total active seeding time cap. Null = no seeding-time limit.</summary>
    public TimeSpan? SeedingTimeLimit { get; init; }

    /// <summary>Time since the last uploaded piece before the limit trips. Null = no limit.</summary>
    public TimeSpan? InactiveSeedingTimeLimit { get; init; }

    public ShareLimitsMode Mode { get; init; } = ShareLimitsMode.MatchAny;

    public ShareLimitAction Action { get; init; } = ShareLimitAction.Stop;
}
