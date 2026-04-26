namespace WinBit.Core.BitTorrent;

internal static class TrackerStatusMapper
{
    internal static TrackerStatus MapStatus(bool updating, byte fails, string? lastError, bool verified)
    {
        if (updating) return TrackerStatus.Updating;
        if (fails > 0 || !string.IsNullOrEmpty(lastError)) return TrackerStatus.Failure;
        if (verified) return TrackerStatus.Working;
        return TrackerStatus.NotContacted;
    }
}
