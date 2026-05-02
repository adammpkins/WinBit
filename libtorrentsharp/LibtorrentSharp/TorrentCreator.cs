#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LibtorrentSharp.Native;

namespace LibtorrentSharp;

/// <summary>
/// Inputs to <see cref="TorrentCreator.CreateAsync"/>. Mirrors the C ABI surface
/// (one parameter per native arg) so the binding stays a thin, faithful wrapper.
/// Tracker tiers go in via <see cref="TrackerTiers"/> as a list-of-lists; the
/// helper marshals them into the native flat-list-with-blank-tier-boundary
/// format documented on the C ABI declaration.
/// </summary>
public sealed record CreateTorrentParams
{
    public required string SourcePath { get; init; }
    public required string OutputPath { get; init; }
    public int PieceSize { get; init; }
    public bool IsPrivate { get; init; }
    public string? Comment { get; init; }
    public string? CreatedBy { get; init; }
    public IReadOnlyList<IReadOnlyList<string>> TrackerTiers { get; init; } = Array.Empty<IReadOnlyList<string>>();
    public IReadOnlyList<string> WebSeeds { get; init; } = Array.Empty<string>();
    public bool IgnoreHidden { get; init; } = true;
}

/// <summary>
/// Per-piece progress event published by <see cref="TorrentCreator.CreateAsync"/>.
/// <see cref="CurrentPiece"/> is 0 for the initial pre-hashing fire; <see cref="TotalSize"/>
/// is the immutable concatenated size of all included files.
/// </summary>
public readonly record struct CreateTorrentProgress(
    long CurrentPiece,
    long TotalPieces,
    long PieceSize,
    long TotalSize)
{
    public long BytesHashed => CurrentPiece * PieceSize > TotalSize ? TotalSize : CurrentPiece * PieceSize;
}

/// <summary>
/// Static facade over <see cref="NativeMethods.CreateTorrent"/>. Hides the pinned
/// cancel-flag + delegate-keepalive + error-buffer plumbing the native API requires.
/// Hashing runs on a worker via <see cref="Task.Run(Action)"/>; the callback bridges
/// to <see cref="IProgress{T}"/> on whatever thread reports progress.
/// </summary>
public static class TorrentCreator
{
    private const int ErrorBufferSize = 512;

    /// <summary>
    /// Hashes <paramref name="parameters"/>.SourcePath, builds the .torrent metadata,
    /// and writes the bencoded result to <paramref name="parameters"/>.OutputPath.
    /// Throws <see cref="OperationCanceledException"/> when <paramref name="ct"/>
    /// trips during hashing; the output file is never written in that case. Throws
    /// <see cref="InvalidOperationException"/> on any other native failure with the
    /// libtorrent error message.
    /// </summary>
    public static Task CreateAsync(
        CreateTorrentParams parameters,
        IProgress<CreateTorrentProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return Task.Run(() => CreateCore(parameters, progress, ct), ct);
    }

    private static unsafe void CreateCore(
        CreateTorrentParams p,
        IProgress<CreateTorrentProgress>? progress,
        CancellationToken ct)
    {
        var trackers = FormatTrackers(p.TrackerTiers);
        var webSeeds = FormatWebSeeds(p.WebSeeds);

        // Pin the cancel flag so the native side can poll it directly. Holding a
        // pinned address is required because the cancellation callback registered
        // below writes to *cancelFlag from an arbitrary thread (the one that
        // signals the CancellationToken).
        var cancelFlagArray = new int[1];
        var cancelHandle = GCHandle.Alloc(cancelFlagArray, GCHandleType.Pinned);

        try
        {
            int* cancelPtr = (int*)cancelHandle.AddrOfPinnedObject();

            // Bridge the CancellationToken to the pinned int*. Volatile.Write
            // because the native poll runs on the hashing thread; the trip is
            // racy by design but a missed observation just means the abort fires
            // on the next piece.
            using var registration = ct.Register(() => Volatile.Write(ref cancelFlagArray[0], 1));

            NativeMethods.CreateTorrentProgressCallback? callback = null;
            if (progress is not null)
            {
                callback = (current, total, pieceSize, totalSize, _) =>
                {
                    progress.Report(new CreateTorrentProgress(current, total, pieceSize, totalSize));
                };
            }

            // Zero-initialize so a failure path that doesn't touch the buffer
            // (e.g. -5 cancellation) doesn't surface stack garbage to PtrToStringUTF8.
            var errorBuf = stackalloc byte[ErrorBufferSize];
            new Span<byte>(errorBuf, ErrorBufferSize).Clear();

            var rc = NativeMethods.CreateTorrent(
                p.SourcePath,
                p.OutputPath,
                p.PieceSize,
                p.IsPrivate ? 1 : 0,
                p.Comment,
                p.CreatedBy,
                trackers,
                webSeeds,
                p.IgnoreHidden ? 1 : 0,
                callback,
                IntPtr.Zero,
                cancelPtr,
                errorBuf,
                ErrorBufferSize);

            // Keep the delegate alive until the native call returns — the GC has
            // no other rooted reference once the callback variable goes out of
            // scope, and the native side dereferences the function pointer up to
            // the very last piece.
            GC.KeepAlive(callback);

            if (rc == 0)
            {
                return;
            }

            if (rc == -5)
            {
                throw new OperationCanceledException(ct.IsCancellationRequested ? ct : new CancellationToken(true));
            }

            var message = Marshal.PtrToStringUTF8((IntPtr)errorBuf) ?? $"native error {rc}";
            throw new InvalidOperationException($"lts_create_torrent failed ({rc}): {message}");
        }
        finally
        {
            cancelHandle.Free();
        }
    }

    // Emits the wire format the C ABI expects: newline-separated URLs with a
    // blank line between tiers. For [["a","b"], ["c"]] that's:
    //   a\nb\n\nc
    // The double \n closes URL b's line AND inserts the blank-line tier marker
    // that apply_trackers in library.cpp uses to bump its tier counter. A single
    // \n between tiers would cause the native parser to keep both URLs at tier 0
    // (it only advances on observed empty lines).
    internal static string? FormatTrackers(IReadOnlyList<IReadOnlyList<string>> tiers)
    {
        if (tiers.Count == 0) return null;
        var sb = new System.Text.StringBuilder();
        for (int t = 0; t < tiers.Count; t++)
        {
            if (t > 0) sb.Append("\n\n");
            var urls = tiers[t];
            for (int u = 0; u < urls.Count; u++)
            {
                if (u > 0) sb.Append('\n');
                sb.Append(urls[u]);
            }
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    private static string? FormatWebSeeds(IReadOnlyList<string> seeds)
    {
        if (seeds.Count == 0) return null;
        return string.Join('\n', seeds);
    }
}
