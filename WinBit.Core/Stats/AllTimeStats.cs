namespace WinBit.Core.Stats;

/// <summary>
/// Cumulative bytes-transferred counters persisted across app restarts. Updated by the
/// <see cref="IAllTimeStatsService"/> from engine ticks and flushed to
/// <c>Paths.AllTimeStatsFile</c> periodically.
/// </summary>
public sealed record AllTimeStats
{
    public long DownloadedBytes { get; init; }

    public long UploadedBytes { get; init; }
}
