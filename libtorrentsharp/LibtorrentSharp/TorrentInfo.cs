// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
// csdl - a cross-platform libtorrent wrapper for .NET
// Licensed under Apache-2.0 - see the license file for more information

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp;

/// <summary>
/// Represents a file contained within a torrent.
/// </summary>
[DebuggerDisplay("{Path} ({FileSize} bytes)")]
public record TorrentFileInfo(int Index, long Offset, string Name, string Path, long FileSize, FileFlags Flags)
{
    /// <summary>Convenience accessor: true when <see cref="Flags"/> has the <see cref="FileFlags.PadFile"/> bit set.</summary>
    public bool IsPadFile => Flags.HasFlag(FileFlags.PadFile);
}

/// <summary>
/// Contains metadata related to a torrent file. <see cref="Hashes"/> is null only for
/// torrents libtorrent rejected (no v1 nor v2 info-hash); successful loads always
/// carry at least one hash.
/// </summary>
[DebuggerDisplay("{Name}")]
public record TorrentMetadata(string Name, string Creator, string Comment, int TotalFiles, long TotalSize, DateTimeOffset CreatedAt, InfoHashes? Hashes);

/// <summary>
/// One contiguous run within a single file covered by a
/// <see cref="TorrentInfo.MapBlock"/> query. Returned in file order.
/// </summary>
/// <param name="FileIndex">Index into <see cref="TorrentInfo.Files"/>.</param>
/// <param name="Offset">Byte offset into that file where this slice starts.</param>
/// <param name="Size">Number of bytes this slice covers.</param>
public record FileSlice(int FileIndex, long Offset, long Size);

/// <summary>
/// Represents a .torrent file.
/// </summary>
[DebuggerDisplay("{Metadata.Name} ({Files.Count} Files)")]
public class TorrentInfo
{
    internal readonly IntPtr InfoHandle;
    private IReadOnlyCollection<TorrentFileInfo> _files;

    private TorrentMetadata _metadata;

    /// <summary>
    /// Creates a new instance of <see cref="TorrentInfo"/> using the contents of a .torrent file from disk.
    /// </summary>
    /// <param name="fileName">The path to the .torrent file</param>
    /// <exception cref="FileNotFoundException">The file was not found</exception>
    /// <exception cref="InvalidOperationException">The file could not be loaded</exception>
    public TorrentInfo(string fileName)
    {
        if (!File.Exists(fileName))
        {
            throw new FileNotFoundException("The specified file does not exist.", fileName);
        }

        InfoHandle = NativeMethods.CreateTorrentFromFile(fileName);

        if (InfoHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create torrent from file provided.");
        }
    }

    /// <summary>
    /// Creates a new instance of <see cref="TorrentInfo"/> using the contents of a .torrent file from memory.
    /// </summary>
    /// <param name="fileBytes">The contents of a .torrent file, as a block of memory</param>
    /// <exception cref="InvalidOperationException">The provided data was invalid</exception>
    public TorrentInfo(byte[] fileBytes)
    {
        InfoHandle = NativeMethods.CreateTorrentFromBytes(fileBytes, fileBytes.Length);

        if (InfoHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create torrent from bytes provided.");
        }
    }

    /// <summary>
    /// Creates a new instance of <see cref="TorrentInfo"/> using the contents of a .torrent file from memory.
    /// </summary>
    /// <param name="memoryPtr">The pointer to the first byte of the file in memory</param>
    /// <param name="length">The size of the file</param>
    /// <exception cref="InvalidOperationException"></exception>
    public TorrentInfo(IntPtr memoryPtr, int length)
    {
        InfoHandle = NativeMethods.CreateTorrentFromBytes(memoryPtr, length);

        if (InfoHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create torrent from bytes provided.");
        }
    }

    /// <summary>
    /// Creates a new instance of <see cref="TorrentInfo"/> using the contents of a .torrent file from memory.
    /// </summary>
    /// <param name="memoryPtr">The pointer to the first byte of the file in memory</param>
    /// <param name="length">The size of the file</param>
    /// <exception cref="InvalidOperationException"></exception>
    [CLSCompliant(false)]
    public unsafe TorrentInfo(void* memoryPtr, int length)
        : this(new IntPtr(memoryPtr), length)
    {
    }

    // as TorrentInfo is shared a lot, we're not providing a dispose method
    // and instead letting the garbage collector handle it
    ~TorrentInfo()
    {
        NativeMethods.FreeTorrent(InfoHandle);
    }

    /// <summary>
    /// Gets metadata related to the torrent file.
    /// </summary>
    public TorrentMetadata Metadata => _metadata ??= GetInfo();

    /// <summary>
    /// Gets a list of files contained within the torrent.
    /// </summary>
    public IReadOnlyCollection<TorrentFileInfo> Files => _files ??= GetFiles();

    /// <summary>
    /// Uniform piece length in bytes — every piece has this size except
    /// (potentially) the last one. Always a power of two ≥ 16 KiB per
    /// libtorrent's torrent-creation defaults.
    /// </summary>
    public int PieceLength => NativeMethods.TorrentInfoPieceLength(InfoHandle);

    /// <summary>
    /// Total number of pieces in the torrent.
    /// </summary>
    public int NumPieces => NativeMethods.TorrentInfoNumPieces(InfoHandle);

    /// <summary>
    /// Size of a specific piece in bytes. Equal to <see cref="PieceLength"/>
    /// for every piece except (potentially) the last one, which can be
    /// smaller when <see cref="TorrentMetadata.TotalSize"/> isn't a multiple
    /// of the uniform piece length.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="pieceIndex"/> is negative or ≥ <see cref="NumPieces"/>.
    /// </exception>
    public int PieceSize(int pieceIndex)
    {
        if (pieceIndex < 0 || pieceIndex >= NumPieces)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex),
                $"pieceIndex must be in 0..{NumPieces - 1}.");
        }
        return NativeMethods.TorrentInfoPieceSize(InfoHandle, pieceIndex);
    }

    /// <summary>
    /// V1 SHA-1 hash of the piece at <paramref name="pieceIndex"/>. The leaves
    /// of libtorrent's V1 piece tree — the BEP-3 analogue of V2 merkle leaves.
    /// Returns null for V2-only torrents (no V1 piece hashes exist); for those,
    /// per-file V2 merkle access lands in a follow-up slice.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="pieceIndex"/> is negative or ≥ <see cref="NumPieces"/>.
    /// </exception>
    public Sha1Hash? HashForPiece(int pieceIndex)
    {
        if (pieceIndex < 0 || pieceIndex >= NumPieces)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex),
                $"pieceIndex must be in 0..{NumPieces - 1}.");
        }

        var buffer = new byte[Sha1Hash.ByteLength];
        return NativeMethods.TorrentInfoHashForPiece(InfoHandle, pieceIndex, buffer)
            ? new Sha1Hash(buffer)
            : null;
    }

    /// <summary>
    /// True when the torrent carries BEP-52 v2 metadata (SHA-256 merkle trees).
    /// A torrent can be v1-only, v2-only, or hybrid — this returns true for
    /// v2-only and hybrid. Pair with <see cref="TorrentMetadata.Hashes"/> to
    /// distinguish v2-only from hybrid.
    /// </summary>
    public bool IsV2 => NativeMethods.TorrentInfoIsV2(InfoHandle);

    /// <summary>
    /// Per-file flag bits for <paramref name="fileIndex"/>. Returns
    /// <see cref="FileFlags.None"/> on out-of-range index.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="fileIndex"/> is negative.
    /// </exception>
    public FileFlags GetFileFlags(int fileIndex)
    {
        if (fileIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileIndex),
                "fileIndex must be non-negative.");
        }
        return (FileFlags)NativeMethods.TorrentInfoFileFlags(InfoHandle, fileIndex);
    }

    /// <summary>
    /// V2 per-file merkle root (SHA-256). Returns null on V1-only torrents
    /// or files without a stored root (libtorrent reports an all-zero hash
    /// in those cases, which this wrapper surfaces as null).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="fileIndex"/> is negative.
    /// </exception>
    public Sha256Hash? GetFileRoot(int fileIndex)
    {
        if (fileIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileIndex),
                "fileIndex must be non-negative.");
        }

        var buffer = new byte[Sha256Hash.ByteLength];
        return NativeMethods.TorrentInfoFileRoot(InfoHandle, fileIndex, buffer)
            ? new Sha256Hash(buffer)
            : null;
    }

    /// <summary>
    /// Symlink target for a file marked with <see cref="FileFlags.Symlink"/>.
    /// Returns null when the file is not a symlink or the target is empty —
    /// pair with <see cref="GetFileFlags"/> to distinguish.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="fileIndex"/> is negative.
    /// </exception>
    public string GetSymlinkTarget(int fileIndex)
    {
        if (fileIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileIndex),
                "fileIndex must be non-negative.");
        }

        // 1024 handles every reasonable symlink path; MAX_PATH on Windows is
        // 260 and POSIX PATH_MAX is typically 4096 — 1024 is a practical
        // middle-ground for torrent-embedded symlinks. Silent truncation on
        // longer targets matches the native contract.
        const int BufferSize = 1024;
        var buffer = new byte[BufferSize];
        var written = NativeMethods.TorrentInfoSymlink(InfoHandle, fileIndex, buffer, BufferSize);
        if (written <= 0)
        {
            return null;
        }

        return System.Text.Encoding.UTF8.GetString(buffer, 0, written);
    }

    /// <summary>
    /// Maps a byte offset in the torrent's virtual concatenated stream to the
    /// index of the file that contains it. Useful for streaming scenarios —
    /// given a byte position, find which file serves it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="offset"/> is negative or ≥ <see cref="TorrentMetadata.TotalSize"/>.
    /// </exception>
    public int FileIndexAtOffset(long offset)
    {
        if (offset < 0 || offset >= Metadata.TotalSize)
        {
            throw new ArgumentOutOfRangeException(nameof(offset),
                $"offset must be in 0..{Metadata.TotalSize - 1}.");
        }
        return NativeMethods.TorrentInfoFileIndexAtOffset(InfoHandle, offset);
    }

    /// <summary>
    /// Number of pieces that overlap the file at <paramref name="fileIndex"/>.
    /// Matches libtorrent's `file_storage::file_num_pieces`. Zero for empty
    /// files.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="fileIndex"/> is negative or ≥
    /// <see cref="TorrentMetadata.TotalFiles"/>.
    /// </exception>
    public int GetFileNumPieces(int fileIndex)
    {
        if (fileIndex < 0 || fileIndex >= Metadata.TotalFiles)
        {
            throw new ArgumentOutOfRangeException(nameof(fileIndex),
                $"fileIndex must be in 0..{Metadata.TotalFiles - 1}.");
        }
        return NativeMethods.TorrentInfoFileNumPieces(InfoHandle, fileIndex);
    }

    /// <summary>
    /// Number of fixed-size 16 KiB blocks that overlap the file at
    /// <paramref name="fileIndex"/>. Blocks are libtorrent's sub-piece unit
    /// for the BitTorrent wire protocol — the granularity of request
    /// messages and partial-piece progress. Zero for empty files.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="fileIndex"/> is negative or ≥
    /// <see cref="TorrentMetadata.TotalFiles"/>.
    /// </exception>
    public int GetFileNumBlocks(int fileIndex)
    {
        if (fileIndex < 0 || fileIndex >= Metadata.TotalFiles)
        {
            throw new ArgumentOutOfRangeException(nameof(fileIndex),
                $"fileIndex must be in 0..{Metadata.TotalFiles - 1}.");
        }
        return NativeMethods.TorrentInfoFileNumBlocks(InfoHandle, fileIndex);
    }

    /// <summary>
    /// Piece extent for a file: the first piece that overlaps the file and
    /// the last piece (inclusive). Equivalent to libtorrent's half-open
    /// file_piece_range with the end converted to last = end - 1. Empty
    /// files and zero-length edge cases return first &gt; last.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="fileIndex"/> is negative or ≥
    /// <see cref="TorrentMetadata.TotalFiles"/>.
    /// </exception>
    public (int FirstPiece, int LastPiece) GetFilePieceRange(int fileIndex)
    {
        if (fileIndex < 0 || fileIndex >= Metadata.TotalFiles)
        {
            throw new ArgumentOutOfRangeException(nameof(fileIndex),
                $"fileIndex must be in 0..{Metadata.TotalFiles - 1}.");
        }

        if (!NativeMethods.TorrentInfoFilePieceRange(InfoHandle, fileIndex,
                out var first, out var end))
        {
            throw new InvalidOperationException(
                $"file_piece_range rejected (fileIndex={fileIndex}).");
        }
        return (first, end - 1);
    }

    /// <summary>
    /// Maps a (file, byte-offset, size) tuple onto the piece containing the
    /// start of that range. Writes the piece index, the byte offset inside
    /// that piece, and the number of contiguous bytes available from that
    /// offset (capped by the piece boundary and the file's remainder).
    /// Useful for random-access reads and streaming — given "file F offset O,
    /// read N bytes", returns which piece to request and where to start.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="fileIndex"/>, <paramref name="offset"/>, or
    /// <paramref name="size"/> is negative, or <paramref name="fileIndex"/>
    /// is ≥ <see cref="TorrentMetadata.TotalFiles"/>.
    /// </exception>
    public void MapFile(int fileIndex, long offset, int size,
        out int pieceIndex, out int pieceOffset, out int length)
    {
        if (fileIndex < 0 || fileIndex >= Metadata.TotalFiles)
        {
            throw new ArgumentOutOfRangeException(nameof(fileIndex),
                $"fileIndex must be in 0..{Metadata.TotalFiles - 1}.");
        }
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "offset must be non-negative.");
        }
        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "size must be non-negative.");
        }

        if (!NativeMethods.TorrentInfoMapFile(InfoHandle, fileIndex, offset, size,
                out pieceIndex, out pieceOffset, out length))
        {
            pieceIndex = 0;
            pieceOffset = 0;
            length = 0;
            throw new InvalidOperationException(
                $"map_file rejected (fileIndex={fileIndex}, offset={offset}, size={size}).");
        }
    }

    /// <summary>
    /// Inverse of <see cref="MapFile"/>: given a piece, a byte offset within
    /// that piece, and a size, returns the list of <see cref="FileSlice"/>
    /// entries the range overlaps (in file order). Most ranges touch a single
    /// file, but ranges spanning small files or pad-files can yield multiple
    /// slices.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="pieceIndex"/> is negative or ≥ <see cref="NumPieces"/>,
    /// or <paramref name="offset"/> / <paramref name="size"/> is negative.
    /// </exception>
    public IReadOnlyList<FileSlice> MapBlock(int pieceIndex, long offset, int size)
    {
        if (pieceIndex < 0 || pieceIndex >= NumPieces)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex),
                $"pieceIndex must be in 0..{NumPieces - 1}.");
        }
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "offset must be non-negative.");
        }
        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "size must be non-negative.");
        }

        NativeMethods.TorrentInfoMapBlock(InfoHandle, pieceIndex, offset, size, out var list);
        try
        {
            if (list.length <= 0 || list.slices == IntPtr.Zero)
            {
                return Array.Empty<FileSlice>();
            }

            var result = new FileSlice[list.length];
            var elementSize = Marshal.SizeOf<NativeStructs.FileSliceStruct>();
            for (var i = 0; i < list.length; i++)
            {
                var ptr = IntPtr.Add(list.slices, i * elementSize);
                var raw = Marshal.PtrToStructure<NativeStructs.FileSliceStruct>(ptr);
                result[i] = new FileSlice(raw.file_index, raw.offset, raw.size);
            }
            return result;
        }
        finally
        {
            NativeMethods.FreeFileSliceList(ref list);
        }
    }

    /// <summary>
    /// Per-file modification time stored in the .torrent. Returns null when
    /// the file has no recorded mtime (most torrents don't store this — it's
    /// optional per BEP-3) or the index is past the last file.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="fileIndex"/> is negative.
    /// </exception>
    public DateTimeOffset? GetFileMtime(int fileIndex)
    {
        if (fileIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileIndex),
                "fileIndex must be non-negative.");
        }
        var epoch = NativeMethods.TorrentInfoFileMtime(InfoHandle, fileIndex);
        return epoch <= 0 ? null : DateTimeOffset.FromUnixTimeSeconds(epoch);
    }

    /// <summary>
    /// V2 per-file piece layer: the full array of SHA-256 leaves covering
    /// every piece in the file. Returns null for V1-only torrents, files
    /// without a stored layer, or out-of-range positive indices. Pair with
    /// <see cref="GetFileRoot"/> to verify the layer against the tree root.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="fileIndex"/> is negative.
    /// </exception>
    public Sha256Hash[] GetPieceLayer(int fileIndex)
    {
        if (fileIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileIndex),
                "fileIndex must be non-negative.");
        }

        var needed = NativeMethods.TorrentInfoPieceLayer(InfoHandle, fileIndex, Array.Empty<byte>(), 0);
        if (needed <= 0 || needed % Sha256Hash.ByteLength != 0)
        {
            return null;
        }

        var buffer = new byte[needed];
        var written = NativeMethods.TorrentInfoPieceLayer(InfoHandle, fileIndex, buffer, needed);
        if (written != needed)
        {
            return null;
        }

        var count = needed / Sha256Hash.ByteLength;
        var hashes = new Sha256Hash[count];
        var leaf = new byte[Sha256Hash.ByteLength];
        for (var i = 0; i < count; i++)
        {
            Buffer.BlockCopy(buffer, i * Sha256Hash.ByteLength, leaf, 0, Sha256Hash.ByteLength);
            hashes[i] = new Sha256Hash(leaf);
        }
        return hashes;
    }

    private TorrentMetadata GetInfo()
    {
        var infoHandle = NativeMethods.GetTorrentInfo(InfoHandle);

        if (infoHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to retrieve torrent metadata.");
        }

        try
        {
            var info = Marshal.PtrToStructure<NativeStructs.TorrentMetadata>(infoHandle);
            return new TorrentMetadata(info.name,
                info.creator,
                info.comment,
                info.total_files,
                info.total_size,
                DateTimeOffset.FromUnixTimeSeconds(info.creation_epoch),
                InfoHashes.FromNativeBuffers(info.info_hash_sha1 ?? Array.Empty<byte>(), info.info_hash_sha256 ?? Array.Empty<byte>()));
        }
        finally
        {
            NativeMethods.FreeTorrentInfo(infoHandle);
        }
    }

    private IReadOnlyCollection<TorrentFileInfo> GetFiles()
    {
        NativeMethods.GetTorrentFileList(InfoHandle, out var list);

        try
        {
            var files = new List<TorrentFileInfo>(list.length);
            var size = Marshal.SizeOf<NativeStructs.TorrentFile>();

            for (var i = 0; i < list.length; i++)
            {
                var nativeFile = Marshal.PtrToStructure<NativeStructs.TorrentFile>(list.items + (size * i));
                var fileInfo = new TorrentFileInfo(nativeFile.index,
                    nativeFile.offset,
                    nativeFile.file_name,
                    nativeFile.file_path,
                    nativeFile.file_size,
                    (FileFlags)nativeFile.flags);

                files.Add(fileInfo);
            }

            return files;
        }
        finally
        {
            NativeMethods.FreeTorrentFileList(ref list);
        }
    }
}