using WinBit.Core.Common;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Builds a <c>.torrent</c> file from a source path. Currently a stub: the LibtorrentSharp
/// binding does not yet wrap libtorrent's <c>create_torrent</c> surface, and the previous
/// engine adapter was removed in the 2026-04-27 engine swap. Tracked under Phase G of
/// <c>LIBTORRENT_TASKS.md</c>. The interface, request/progress shape, and DI registration
/// stay wired so callers (UI, Web UI, tests) keep compiling;
/// <see cref="ITorrentCreatorService.CreateAsync"/> throws <see cref="NotSupportedException"/>
/// at runtime until the binding catches up.
/// </summary>
public interface ITorrentCreatorService
{
    Task<Result> CreateAsync(TorrentCreateRequest request, IProgress<TorrentCreateProgress>? progress = null, CancellationToken ct = default);
}

public sealed record TorrentCreateRequest
{
    /// <summary>Source file or directory to hash.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Where the resulting <c>.torrent</c> will be written.</summary>
    public required string OutputPath { get; init; }

    /// <summary>Display name of the torrent. Null/empty = source basename.</summary>
    public string? Name { get; init; }

    /// <summary>Tracker tiers — each inner list is one tier; clients try trackers in order.</summary>
    public IReadOnlyList<IReadOnlyList<string>> TrackerTiers { get; init; } = Array.Empty<IReadOnlyList<string>>();

    public IReadOnlyList<string> WebSeeds { get; init; } = Array.Empty<string>();

    public string? Comment { get; init; }

    public string? CreatedBy { get; init; }

    /// <summary>Piece length in bytes. Null = let the engine pick based on total size.</summary>
    public int? PieceLength { get; init; }

    /// <summary>If true, sets the private flag (peers come only from trackers, no DHT/PEX).</summary>
    public bool IsPrivate { get; init; }

    /// <summary>Skip files whose name starts with '.' (Unix-hidden convention).</summary>
    public bool IgnoreHidden { get; init; } = true;
}

public readonly record struct TorrentCreateProgress(string CurrentFile, long OverallBytesHashed, long OverallSize)
{
    /// <summary>0..1 overall completion.</summary>
    public double OverallCompletion => OverallSize == 0 ? 0 : (double)OverallBytesHashed / OverallSize;
}

public sealed class TorrentCreatorService : ITorrentCreatorService
{
    public Task<Result> CreateAsync(TorrentCreateRequest request, IProgress<TorrentCreateProgress>? progress = null, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "Torrent creation is not yet supported with the libtorrent engine. " +
            "Tracked under Phase G of LIBTORRENT_TASKS.md.");
}
