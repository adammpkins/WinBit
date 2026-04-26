using System;
using System.IO;
using LibtorrentSharp.Enums;
using Xunit;

namespace LibtorrentSharp.Tests;

public class MoveStorageSmokeTests
{
    private const string ValidMagnetUri = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=ubuntu-14.04.1-desktop-amd64.iso";

    [Fact]
    [Trait("Category", "Native")]
    public void MoveStorage_OnValidMagnetHandle_DoesNotThrow()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = ValidMagnetUri }).Magnet!;
        Assert.True(handle.IsValid);

        var destination = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", "move-" + Guid.NewGuid().ToString("N"));

        handle.MoveStorage(destination);
        handle.MoveStorage(destination, MoveStorageFlags.DontReplace);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void MoveStorage_OnInvalidHandle_IsNoOp()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = "not-a-magnet" }).Magnet!;
        Assert.False(handle.IsValid);

        handle.MoveStorage(Path.GetTempPath());
    }

    [Fact]
    [Trait("Category", "Native")]
    public void MoveStorage_WithEmptyPath_Throws()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = ValidMagnetUri }).Magnet!;

        Assert.Throws<ArgumentException>(() => handle.MoveStorage(string.Empty));
        Assert.Throws<ArgumentException>(() => handle.MoveStorage(null!));
    }

    private static LibtorrentSession NewClient() =>
        new()
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };
}
