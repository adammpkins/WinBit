using MonoTorrent;
using WinBit.Core.Common;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Wraps <see cref="MonoTorrent.TorrentCreator"/>. Takes a fully-formed
/// <see cref="TorrentCreateRequest"/> and writes a <c>.torrent</c> file to disk, reporting
/// hashing progress through <see cref="IProgress{T}"/>. Used by <c>TorrentCreatorPage</c>.
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

    /// <summary>Display name of the torrent. Null/empty = MonoTorrent default (source basename).</summary>
    public string? Name { get; init; }

    /// <summary>Tracker tiers — each inner list is one tier; clients try trackers in order.</summary>
    public IReadOnlyList<IReadOnlyList<string>> TrackerTiers { get; init; } = Array.Empty<IReadOnlyList<string>>();

    public IReadOnlyList<string> WebSeeds { get; init; } = Array.Empty<string>();

    public string? Comment { get; init; }

    public string? CreatedBy { get; init; }

    /// <summary>Piece length in bytes. Null = let MonoTorrent pick based on total size.</summary>
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
        if (string.IsNullOrWhiteSpace(request.SourcePath))
        {
            return Result.Failure("Source path is required.");
        }
        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            return Result.Failure("Output path is required.");
        }
        if (!File.Exists(request.SourcePath) && !Directory.Exists(request.SourcePath))
        {
            return Result.Failure($"Source '{request.SourcePath}' does not exist.");
        }

        try
        {
            var creator = new TorrentCreator(TorrentType.V1Only)
            {
                Private = request.IsPrivate,
            };

            if (!string.IsNullOrWhiteSpace(request.Comment))
            {
                creator.Comment = request.Comment;
            }
            if (!string.IsNullOrWhiteSpace(request.CreatedBy))
            {
                creator.CreatedBy = request.CreatedBy;
            }
            if (request.PieceLength is int len && len > 0)
            {
                creator.PieceLength = len;
            }

            foreach (var tier in request.TrackerTiers)
            {
                var cleaned = tier.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
                if (cleaned.Count > 0)
                {
                    creator.Announces.Add(cleaned);
                }
            }

            // MonoTorrent prefers `Announce` to be set when any tracker is known; point it at the
            // first tier's first URL for compatibility with clients that only read the root key.
            if (creator.Announces.Count > 0 && creator.Announces[0].Count > 0)
            {
                creator.Announce = creator.Announces[0][0];
            }

            foreach (var seed in request.WebSeeds.Where(u => !string.IsNullOrWhiteSpace(u)))
            {
                creator.GetrightHttpSeeds.Add(seed);
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                creator.Name = request.Name;
            }

            if (progress is not null)
            {
                creator.Hashed += (_, e) => progress.Report(
                    new TorrentCreateProgress(e.CurrentFile, e.OverallBytesHashed, e.OverallSize));
            }

            var source = new TorrentFileSource(request.SourcePath, request.IgnoreHidden);

            var outputDir = Path.GetDirectoryName(request.OutputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            await creator.CreateAsync(source, request.OutputPath, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            return Result.Failure("Torrent creation cancelled.");
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
