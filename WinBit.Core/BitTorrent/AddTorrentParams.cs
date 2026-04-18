namespace WinBit.Core.BitTorrent;

/// <summary>
/// Input to <c>ITorrentSessionService.AddAsync</c>. <see cref="Source"/> may be a local
/// <c>.torrent</c> file path, an HTTP(S) URL to a <c>.torrent</c>, or a <c>magnet:</c> URI.
/// </summary>
public sealed record AddTorrentParams
{
    public required string Source { get; init; }

    public required string SavePath { get; init; }

    public string? Category { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public bool StartImmediately { get; init; } = true;

    public bool SkipHashCheck { get; init; }

    public bool Sequential { get; init; }

    public bool FirstAndLastPiecePriority { get; init; }
}
