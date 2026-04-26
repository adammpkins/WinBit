using System;
using System.IO;
using System.Text;
using LibtorrentSharp.Enums;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Round-trips the first slice of the f-handle-storage cluster: the
/// <see cref="TorrentInfo"/> scalar accessors that forward to
/// <c>libtorrent::file_storage</c> — <c>PieceLength</c>, <c>NumPieces</c>,
/// <c>PieceSize(int)</c>. The test uses a hand-built bencoded single-file
/// torrent so we can assert exact expected values (last-piece remainder,
/// piece counts) without depending on real network data.
/// </summary>
public sealed class TorrentInfoStorageSmokeTests
{
    private const int PieceLength = 16384;       // 16 KiB — libtorrent's minimum.
    private const long TotalLength = 32768 + 12; // 2 full pieces + 12-byte tail → 3 pieces total.
    private const int ExpectedNumPieces = 3;
    private const int ExpectedLastPieceSize = 12;

    [Fact]
    [Trait("Category", "Native")]
    public void PieceLength_ReturnsUniformSize()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.Equal(PieceLength, info.PieceLength);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void NumPieces_ReturnsCeilOfTotalOverPieceLength()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.Equal(ExpectedNumPieces, info.NumPieces);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void PieceSize_ReturnsUniformForAllButLast()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        Assert.Equal(PieceLength, info.PieceSize(0));
        Assert.Equal(PieceLength, info.PieceSize(1));
        Assert.Equal(ExpectedLastPieceSize, info.PieceSize(ExpectedNumPieces - 1));
    }

    [Fact]
    public void PieceSize_ThrowsForOutOfRangeIndex()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        Assert.Throws<ArgumentOutOfRangeException>(() => info.PieceSize(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.PieceSize(ExpectedNumPieces));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.PieceSize(int.MaxValue));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void MetadataTotalSize_AgreesWithPieceBreakdown()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        // Reality-check: the independent scalars should form a consistent
        // picture of the torrent. total_size == (N-1)*piece_length + last_piece.
        var reconstructed = (long)(info.NumPieces - 1) * info.PieceLength
                            + info.PieceSize(info.NumPieces - 1);
        Assert.Equal(info.Metadata.TotalSize, reconstructed);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void HashForPiece_OnV1Torrent_ReturnsLeafFromPiecesField()
    {
        // The fixture's `pieces` field is zero-filled (libtorrent only validates
        // length × num_pieces, not contents). Every piece hash is therefore the
        // 20-byte zero Sha1Hash.
        var info = new TorrentInfo(BuildMinimalTorrent());
        var zero = new Sha1Hash(new byte[Sha1Hash.ByteLength]);

        for (var i = 0; i < info.NumPieces; i++)
        {
            var hash = info.HashForPiece(i);
            Assert.NotNull(hash);
            Assert.Equal(zero, hash.Value);
            Assert.True(hash.Value.IsZero);
        }
    }

    [Fact]
    public void HashForPiece_ThrowsForOutOfRangeIndex()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        Assert.Throws<ArgumentOutOfRangeException>(() => info.HashForPiece(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.HashForPiece(ExpectedNumPieces));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.HashForPiece(int.MaxValue));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void IsV2_OnV1OnlyTorrent_ReturnsFalse()
    {
        // The hand-built fixture uses the v1-only info dict (`length` at info
        // level, no `file tree`). torrent_info::v2() must report false.
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.False(info.IsV2);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetFileFlags_OnBaseFixtureFile_ReturnsNone()
    {
        // Single-file fixture has no per-file attr set, so file_flags is 0.
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.Equal(FileFlags.None, info.GetFileFlags(0));
    }

    [Fact]
    public void GetFileFlags_ThrowsForNegativeIndex()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetFileFlags(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetFileFlags(int.MinValue));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetFileFlags_PositiveOutOfRangeIndex_ReturnsNone()
    {
        // The native contract is documented as "returns 0 on out-of-range
        // index"; the managed wrapper only hard-throws on negative indices.
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.Equal(FileFlags.None, info.GetFileFlags(999_999));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetFileRoot_OnV1OnlyTorrent_ReturnsNull()
    {
        // V1-only torrents have no V2 merkle roots; libtorrent returns an
        // all-zero sha256_hash which the wrapper normalizes to null.
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.Null(info.GetFileRoot(0));
    }

    [Fact]
    public void GetFileRoot_ThrowsForNegativeIndex()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetFileRoot(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetFileRoot(int.MinValue));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetFileRoot_PositiveOutOfRangeIndex_ReturnsNull()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.Null(info.GetFileRoot(999_999));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetSymlinkTarget_OnNonSymlinkFile_ReturnsNull()
    {
        // Fixture file has no symlink attr; libtorrent returns empty string
        // and the wrapper surfaces empty → null.
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.Null(info.GetSymlinkTarget(0));
    }

    [Fact]
    public void GetSymlinkTarget_ThrowsForNegativeIndex()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetSymlinkTarget(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetSymlinkTarget(int.MinValue));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetSymlinkTarget_PositiveOutOfRangeIndex_ReturnsNull()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.Null(info.GetSymlinkTarget(999_999));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetPieceLayer_OnV1OnlyTorrent_ReturnsNull()
    {
        // V1-only torrents have no V2 piece layers; libtorrent returns an
        // empty span which the wrapper surfaces as null.
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.Null(info.GetPieceLayer(0));
    }

    [Fact]
    public void GetPieceLayer_ThrowsForNegativeIndex()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetPieceLayer(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetPieceLayer(int.MinValue));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetPieceLayer_PositiveOutOfRangeIndex_ReturnsNull()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.Null(info.GetPieceLayer(999_999));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void MapFile_FirstByte_MapsToPiece0Offset0()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());
        info.MapFile(0, 0, PieceLength, out var piece, out var offset, out var length);

        Assert.Equal(0, piece);
        Assert.Equal(0, offset);
        Assert.Equal(PieceLength, length);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void MapFile_MidPiece_MapsToCorrectPiece()
    {
        // offset = PieceLength + 100 → inside piece 1, at byte 100.
        var info = new TorrentInfo(BuildMinimalTorrent());
        info.MapFile(0, PieceLength + 100, 50, out var piece, out var offset, out var length);

        Assert.Equal(1, piece);
        Assert.Equal(100, offset);
        Assert.Equal(50, length);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void MapFile_LastByte_MapsToLastPiece()
    {
        // Last valid offset → inside piece 2, at byte (ExpectedLastPieceSize-1).
        var info = new TorrentInfo(BuildMinimalTorrent());
        info.MapFile(0, TotalLength - 1, 1, out var piece, out var offset, out var length);

        Assert.Equal(ExpectedNumPieces - 1, piece);
        Assert.Equal(ExpectedLastPieceSize - 1, offset);
        Assert.Equal(1, length);
    }

    [Fact]
    public void MapFile_NegativeInputs_Throw()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            info.MapFile(-1, 0, 0, out _, out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            info.MapFile(0, -1, 0, out _, out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            info.MapFile(0, 0, -1, out _, out _, out _));
    }

    [Fact]
    public void MapFile_FileIndexOutOfRange_Throws()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            info.MapFile(999_999, 0, 0, out _, out _, out _));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void MapBlock_WholePiece_MapsToSingleFile()
    {
        // V1 single-file fixture: every piece lands entirely in file 0 at
        // the offset equal to piece_index × piece_length.
        var info = new TorrentInfo(BuildMinimalTorrent());
        var slices = info.MapBlock(0, 0, PieceLength);

        Assert.Single(slices);
        Assert.Equal(0, slices[0].FileIndex);
        Assert.Equal(0, slices[0].Offset);
        Assert.Equal(PieceLength, slices[0].Size);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void MapBlock_LastPieceRemainder_SizeMatchesExpectedTail()
    {
        // The last piece holds only the 12-byte tail of the single file.
        var info = new TorrentInfo(BuildMinimalTorrent());
        var slices = info.MapBlock(ExpectedNumPieces - 1, 0, PieceLength);

        Assert.Single(slices);
        Assert.Equal(0, slices[0].FileIndex);
        Assert.Equal((long)(ExpectedNumPieces - 1) * PieceLength, slices[0].Offset);
        Assert.Equal(ExpectedLastPieceSize, (int)slices[0].Size);
    }

    [Fact]
    public void MapBlock_NegativeInputs_Throw()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        Assert.Throws<ArgumentOutOfRangeException>(() => info.MapBlock(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.MapBlock(0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.MapBlock(0, 0, -1));
    }

    [Fact]
    public void MapBlock_PieceIndexOutOfRange_Throws()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        Assert.Throws<ArgumentOutOfRangeException>(() => info.MapBlock(ExpectedNumPieces, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.MapBlock(int.MaxValue, 0, 0));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetFilePieceRange_SingleFile_SpansAllPieces()
    {
        // V1 fixture has one file spanning the full torrent, so its piece
        // range should be the entire piece set [0, ExpectedNumPieces - 1].
        var info = new TorrentInfo(BuildMinimalTorrent());
        var (first, last) = info.GetFilePieceRange(0);

        Assert.Equal(0, first);
        Assert.Equal(ExpectedNumPieces - 1, last);
    }

    [Fact]
    public void GetFilePieceRange_NegativeIndex_Throws()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetFilePieceRange(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetFilePieceRange(int.MinValue));
    }

    [Fact]
    public void GetFilePieceRange_PositiveOutOfRangeIndex_Throws()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetFilePieceRange(999_999));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetFileNumPieces_SingleFile_MatchesTotalPieces()
    {
        // Fixture file spans every piece, so file_num_pieces == NumPieces.
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.Equal(ExpectedNumPieces, info.GetFileNumPieces(0));
    }

    [Fact]
    public void GetFileNumPieces_OutOfRangeIndex_Throws()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetFileNumPieces(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetFileNumPieces(int.MinValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetFileNumPieces(999_999));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetFileNumBlocks_SingleFile_AtLeastMatchesPieceCount()
    {
        // Fixture uses 16 KiB pieces == libtorrent's block size, so each piece
        // has exactly one block. For the 3-piece fixture that's 3 blocks.
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.Equal(ExpectedNumPieces, info.GetFileNumBlocks(0));
    }

    [Fact]
    public void GetFileNumBlocks_OutOfRangeIndex_Throws()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetFileNumBlocks(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetFileNumBlocks(int.MinValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetFileNumBlocks(999_999));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetFileMtime_OnFixtureWithoutMtime_ReturnsNull()
    {
        // Hand-built fixture omits the optional `mtime` per-file attr;
        // libtorrent reports 0, the wrapper surfaces null.
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.Null(info.GetFileMtime(0));
    }

    [Fact]
    public void GetFileMtime_NegativeIndex_Throws()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetFileMtime(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetFileMtime(int.MinValue));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetFileMtime_PositiveOutOfRangeIndex_ReturnsNull()
    {
        // Native contract returns 0 for out-of-range positive indices, which
        // the managed wrapper translates to null (same as "no mtime stored").
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.Null(info.GetFileMtime(999_999));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void IsV2_OnV1Fixture_ReturnsFalse()
    {
        // The hand-built fixture is BEP-3 V1-only (no `meta version` / `file tree`
        // fields). libtorrent::torrent_info::v2() should report false.
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.False(info.IsV2);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetFileFlags_OnSingleFileFixture_ReturnsNone()
    {
        // Single-file torrent with no symlink / hidden / executable / pad-file
        // flags set — libtorrent should report None for the only file.
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.Equal(FileFlags.None, info.GetFileFlags(0));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetFileFlags_OutOfRangeIndex_ReturnsNone()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());
        // Past the single file — native returns 0, we surface as None.
        Assert.Equal(FileFlags.None, info.GetFileFlags(999_999));
    }

    [Fact]
    public void GetFileFlags_NegativeIndex_Throws()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());
        Assert.Throws<ArgumentOutOfRangeException>(() => info.GetFileFlags(-1));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void FileIndexAtOffset_WithinSingleFile_ReturnsZero()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        // Single-file fixture: every byte maps to file 0 — first, middle, last.
        Assert.Equal(0, info.FileIndexAtOffset(0));
        Assert.Equal(0, info.FileIndexAtOffset(TotalLength / 2));
        Assert.Equal(0, info.FileIndexAtOffset(TotalLength - 1));
    }

    [Fact]
    public void FileIndexAtOffset_OutOfRange_Throws()
    {
        var info = new TorrentInfo(BuildMinimalTorrent());

        Assert.Throws<ArgumentOutOfRangeException>(() => info.FileIndexAtOffset(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.FileIndexAtOffset(long.MinValue));
        // Offset exactly at total_size is one-past-end — out of range.
        Assert.Throws<ArgumentOutOfRangeException>(() => info.FileIndexAtOffset(TotalLength));
        Assert.Throws<ArgumentOutOfRangeException>(() => info.FileIndexAtOffset(long.MaxValue));
    }

    /// <summary>
    /// Builds a tiny valid single-file .torrent payload (bencoded) so we can
    /// exercise the file_storage accessors without a network fetch or fixture
    /// file. No announce list needed — libtorrent accepts info-only torrents.
    /// The piece hashes are zeros; libtorrent only validates their length
    /// (20 bytes per piece × num_pieces).
    /// </summary>
    private static byte[] BuildMinimalTorrent()
    {
        var numPieces = (int)((TotalLength + PieceLength - 1) / PieceLength);
        var pieces = new byte[numPieces * 20]; // zero-filled: length-only validation.

        using var ms = new MemoryStream();
        WriteByte(ms, 'd'); // outer dict
        WriteBencString(ms, "info");
        WriteByte(ms, 'd'); // info dict (keys in bencoded sort order)
        WriteBencString(ms, "length"); WriteBencInt(ms, TotalLength);
        WriteBencString(ms, "name");   WriteBencString(ms, "test");
        WriteBencString(ms, "piece length"); WriteBencInt(ms, PieceLength);
        WriteBencString(ms, "pieces"); WriteBencBytes(ms, pieces);
        WriteByte(ms, 'e'); // close info
        WriteByte(ms, 'e'); // close outer
        return ms.ToArray();
    }

    private static void WriteByte(Stream s, char c) => s.WriteByte((byte)c);

    private static void WriteBencString(Stream s, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteBencBytes(s, bytes);
    }

    private static void WriteBencBytes(Stream s, byte[] bytes)
    {
        var header = Encoding.ASCII.GetBytes($"{bytes.Length}:");
        s.Write(header, 0, header.Length);
        s.Write(bytes, 0, bytes.Length);
    }

    private static void WriteBencInt(Stream s, long value)
    {
        var bytes = Encoding.ASCII.GetBytes($"i{value}e");
        s.Write(bytes, 0, bytes.Length);
    }
}
