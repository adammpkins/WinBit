using WinBit.Core.Common;

namespace WinBit.Core.Sharing;

/// <summary>
/// A user-saved share-limit override for a single torrent. Mirrors qBittorrent's per-torrent
/// share-limit fields on <c>BitTorrent::Torrent</c>. Nullable limits mean "no cap"; the
/// sentinel <see cref="ShareLimitAction.Default"/> / <see cref="ShareLimitsMode.Default"/>
/// mean "inherit the global setting". The global configuration lives on
/// <c>AppSettings.GlobalShareLimits</c>.
/// </summary>
public sealed record PerTorrentShareLimitOverride
{
    public required TorrentId Id { get; init; }

    public double? RatioLimit { get; init; }

    public TimeSpan? SeedingTimeLimit { get; init; }

    public TimeSpan? InactiveSeedingTimeLimit { get; init; }

    public ShareLimitsMode Mode { get; init; } = ShareLimitsMode.Default;

    public ShareLimitAction Action { get; init; } = ShareLimitAction.Default;
}
