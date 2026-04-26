using System;
using System.IO;
using LibtorrentSharp.Enums;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Round-trips the piece-level priority surface added by f-handle-piece-priorities.
/// Uses magnet handles since add-via-magnet is the simplest path to a valid
/// handle that doesn't depend on a real .torrent fixture. Pre-metadata the
/// native side short-circuits, so assertions focus on no-crash plus the
/// documented "return 0 / empty array / false" contract.
/// </summary>
public sealed class PiecePrioritySmokeTests
{
    private const string ValidMagnetUri = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=ubuntu-14.04.1-desktop-amd64.iso";

    [Fact]
    [Trait("Category", "Native")]
    public void SingleGet_PreMetadata_ReturnsDoNotDownload()
    {
        using var session = NewSession();
        var handle = AddMagnet(session);

        // No metadata yet; out-of-range index is also no-op.
        Assert.Equal(FileDownloadPriority.DoNotDownload, handle.GetPiecePriority(0));
        Assert.Equal(FileDownloadPriority.DoNotDownload, handle.GetPiecePriority(-1));
        Assert.Equal(FileDownloadPriority.DoNotDownload, handle.GetPiecePriority(999_999));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void SingleSet_PreMetadata_IsNoOp()
    {
        using var session = NewSession();
        var handle = AddMagnet(session);

        // Must not crash on bad indices or missing metadata.
        handle.SetPiecePriority(0, FileDownloadPriority.High);
        handle.SetPiecePriority(-1, FileDownloadPriority.High);
        handle.SetPiecePriority(999_999, FileDownloadPriority.High);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void BulkGet_PreMetadata_ReturnsEmptyArray()
    {
        using var session = NewSession();
        var handle = AddMagnet(session);

        var priorities = handle.GetPiecePriorities();
        Assert.NotNull(priorities);
        Assert.Empty(priorities);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void BulkSet_PreMetadata_IsNoOp()
    {
        using var session = NewSession();
        var handle = AddMagnet(session);

        // Empty span is no-op; populated span is also no-op (no metadata).
        handle.SetPiecePriorities(ReadOnlySpan<FileDownloadPriority>.Empty);

        Span<FileDownloadPriority> buffer = stackalloc FileDownloadPriority[4];
        buffer[0] = FileDownloadPriority.High;
        buffer[1] = FileDownloadPriority.Low;
        buffer[2] = FileDownloadPriority.Normal;
        buffer[3] = FileDownloadPriority.DoNotDownload;
        handle.SetPiecePriorities(buffer);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void HavePiece_PreMetadata_ReturnsFalse()
    {
        using var session = NewSession();
        var handle = AddMagnet(session);

        // No metadata + out-of-range: never true, never throws.
        Assert.False(handle.HavePiece(0));
        Assert.False(handle.HavePiece(-1));
        Assert.False(handle.HavePiece(999_999));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void AllAccessors_OnInvalidMagnetHandle_AreSafeNoOps()
    {
        using var session = NewSession();
        var result = session.Add(new AddTorrentParams { MagnetUri = "not-a-magnet" });
        Assert.False(result.IsValid);
        var handle = result.Magnet!;

        Assert.Equal(FileDownloadPriority.DoNotDownload, handle.GetPiecePriority(0));
        handle.SetPiecePriority(0, FileDownloadPriority.High);
        Assert.Empty(handle.GetPiecePriorities());
        handle.SetPiecePriorities(new[] { FileDownloadPriority.High });
        Assert.False(handle.HavePiece(0));
    }

    private static LibtorrentSession NewSession() =>
        new()
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };

    private static MagnetHandle AddMagnet(LibtorrentSession session)
    {
        var result = session.Add(new AddTorrentParams { MagnetUri = ValidMagnetUri });
        Assert.True(result.IsValid);
        return result.Magnet!;
    }
}
