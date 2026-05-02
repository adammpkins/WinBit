using LibtorrentSharp;
using WinBit.Core.Common;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Builds a <c>.torrent</c> file from a source path. Backed by LibtorrentSharp's
/// <see cref="LibtorrentSharp.TorrentCreator"/> static helper, which wraps libtorrent's
/// <c>create_torrent</c> + <c>set_piece_hashes</c> + <c>bencode</c> surface through the
/// C ABI. Hashing runs on a worker thread; cancellation is observed mid-hash via the
/// pinned cancel flag the binding plumbs through to the native side.
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
    public async Task<Result> CreateAsync(TorrentCreateRequest request, IProgress<TorrentCreateProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SourcePath))
        {
            return Result.Failure("SourcePath is required.");
        }
        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            return Result.Failure("OutputPath is required.");
        }
        if (!File.Exists(request.SourcePath) && !Directory.Exists(request.SourcePath))
        {
            return Result.Failure($"Source path does not exist: {request.SourcePath}");
        }

        // Stable display label for progress events. The native progress callback
        // doesn't know which file is being hashed at piece N (that would require
        // a piece->file mapping query the binding doesn't currently expose); the
        // source basename is good enough for "we're working on <thing>" UX.
        var displayName = request.Name;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = Path.GetFileName(request.SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        var bridged = progress is null
            ? null
            : new Progress<CreateTorrentProgress>(p => progress.Report(
                new TorrentCreateProgress(displayName ?? string.Empty, p.BytesHashed, p.TotalSize)));

        var nativeParams = new CreateTorrentParams
        {
            SourcePath = request.SourcePath,
            OutputPath = request.OutputPath,
            PieceSize = request.PieceLength.GetValueOrDefault(0),
            IsPrivate = request.IsPrivate,
            Comment = request.Comment,
            CreatedBy = request.CreatedBy,
            TrackerTiers = request.TrackerTiers,
            WebSeeds = request.WebSeeds,
            IgnoreHidden = request.IgnoreHidden,
        };

        try
        {
            await LibtorrentSharp.TorrentCreator.CreateAsync(nativeParams, bridged, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Surface cancellation as the .NET-idiomatic exception so callers using
            // structured concurrency can catch it cleanly. TorrentCreatorQueue treats
            // any thrown exception the same as a Result.Failure (queue file: TaskRecord
            // catch block), so this stays compatible with the existing queue surface.
            throw;
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
