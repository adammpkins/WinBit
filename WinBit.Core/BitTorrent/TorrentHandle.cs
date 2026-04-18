using WinBit.Core.Common;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Read-only reference to a torrent in the session. Commands (pause, resume, recheck,
/// reannounce) live on <c>ITorrentSessionService</c> and take a <see cref="TorrentId"/> —
/// handles never hold back-references to the engine.
/// </summary>
public sealed record TorrentHandle
{
    public required TorrentId Id { get; init; }

    public required string Name { get; init; }

    public required string SavePath { get; init; }

    public required long TotalSize { get; init; }

    public string? Category { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public DateTime AddedUtc { get; init; }

    public DateTime? CompletedUtc { get; init; }
}
