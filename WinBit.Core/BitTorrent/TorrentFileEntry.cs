namespace WinBit.Core.BitTorrent;

public sealed record TorrentFileEntry
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    public required string RelativePath { get; init; }
    public required long SizeBytes { get; init; }
    public required FileDownloadPriority Priority { get; init; }
}
