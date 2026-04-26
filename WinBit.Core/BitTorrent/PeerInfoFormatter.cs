namespace WinBit.Core.BitTorrent;

/// <summary>
/// Pure formatting helpers for <see cref="PeerInfo"/> fields. Extracted to Core so the
/// display logic can be covered by unit tests without a WinUI project reference.
/// </summary>
public static class PeerInfoFormatter
{
    public static string BuildFlags(PeerInfo info)
    {
        if (!info.IsSeeder && !info.IsEncrypted) return "—";
        var sb = new System.Text.StringBuilder();
        if (info.IsSeeder) sb.Append('S');
        if (info.IsEncrypted) sb.Append('E');
        return sb.ToString();
    }

    public static string FormatSpeed(long bps)
    {
        if (bps <= 0) return "—";
        return FormatBytes(bps) + "/s";
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024 => $"{bytes / 1_024.0:F1} KB",
        _ => $"{bytes} B",
    };
}
