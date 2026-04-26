using System;
using System.IO;
using LibtorrentSharp.Enums;
using Xunit;

namespace LibtorrentSharp.Tests;

public class FilePiecePrioritySmokeTests
{
    private const string ValidMagnetUri = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=ubuntu-14.04.1-desktop-amd64.iso";

    [Fact]
    [Trait("Category", "Native")]
    public void SetFileFirstLastPiecePriority_OnMagnetWithoutMetadata_IsNoOp()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = ValidMagnetUri }).Magnet!;
        Assert.True(handle.IsValid);

        // Metadata not resolved — native code short-circuits cleanly.
        handle.SetFileFirstLastPiecePriority(0, FileDownloadPriority.High);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void SetFileFirstLastPiecePriority_OnInvalidHandle_IsNoOp()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = "not-a-magnet" }).Magnet!;
        Assert.False(handle.IsValid);

        handle.SetFileFirstLastPiecePriority(0, FileDownloadPriority.High);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void SetFileFirstLastPiecePriority_WithOutOfRangeIndex_IsNoOp()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = ValidMagnetUri }).Magnet!;

        // Negative and very large indices must not crash — guarded in native.
        handle.SetFileFirstLastPiecePriority(-1, FileDownloadPriority.High);
        handle.SetFileFirstLastPiecePriority(999_999, FileDownloadPriority.High);
    }

    private static LibtorrentSession NewClient() =>
        new()
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };
}
