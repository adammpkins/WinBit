namespace WinBit.Core.BitTorrent;

public sealed record TorrentFileEntry
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    public required string RelativePath { get; init; }
    public required long SizeBytes { get; init; }
    public required FileDownloadPriority Priority { get; init; }

    public long DownloadedBytes { get; init; }

    public double ProgressFraction => SizeBytes > 0 ? Math.Clamp((double)DownloadedBytes / SizeBytes, 0.0, 1.0) : 0.0;

    public string ProgressDisplay => SizeBytes > 0 ? $"{ProgressFraction * 100:F1}%" : "—";
}
