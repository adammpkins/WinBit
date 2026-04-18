using WinBit.Core.Common;

namespace WinBit.Core.Persistence;

/// <summary>
/// Persisted torrent metadata. Subset of the <c>torrent</c> table columns that the engine
/// needs for cold-start rehydration. Fast-resume blob and version live in their own columns
/// and are written through <c>ITorrentStateStore.SaveFastResumeAsync</c>.
/// </summary>
public sealed record TorrentStateRecord
{
    public required TorrentId Id { get; init; }

    public required string Name { get; init; }

    public required string SavePath { get; init; }

    public DateTime AddedUtc { get; init; }

    public DateTime? CompletedUtc { get; init; }

    public string? Category { get; init; }

    /// <summary>JSON-serialized tag array. Null when no tags.</summary>
    public string? Tags { get; init; }
}
