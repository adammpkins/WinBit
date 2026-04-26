namespace WinBit.Core.BitTorrent;

/// <summary>Snapshot of a single tracker the torrent announces to.</summary>
public sealed record TrackerInfo
{
    public required Uri Url { get; init; }

    public required TrackerStatus Status { get; init; }

    public int Seeds { get; init; }

    public int Leeches { get; init; }

    public int Completed { get; init; }

    public DateTimeOffset? NextAnnounceUtc { get; init; }

    public string? LastError { get; init; }
}

public enum TrackerStatus
{
    NotContacted,
    Updating,
    Working,
    Failure,
}
