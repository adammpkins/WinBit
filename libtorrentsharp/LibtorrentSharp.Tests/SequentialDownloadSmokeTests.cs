using System;
using System.IO;
using LibtorrentSharp.Enums;
using Xunit;

namespace LibtorrentSharp.Tests;

public class SequentialDownloadSmokeTests
{
    private const string ValidMagnetUri = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=ubuntu-14.04.1-desktop-amd64.iso";

    [Fact]
    [Trait("Category", "Native")]
    public void SetSequentialDownload_FlipsFlag()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = ValidMagnetUri }).Magnet!;
        Assert.True(handle.IsValid);

        handle.SetSequentialDownload(true);
        Assert.True(handle.GetCurrentStatus().Flags.HasFlag(TorrentFlags.SequentialDownload));

        handle.SetSequentialDownload(false);
        Assert.False(handle.GetCurrentStatus().Flags.HasFlag(TorrentFlags.SequentialDownload));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void SetSequentialDownload_OnInvalidHandle_IsNoOp()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = "not-a-magnet" }).Magnet!;
        Assert.False(handle.IsValid);

        handle.SetSequentialDownload(true);
    }

    private static LibtorrentSession NewClient() =>
        new()
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };
}
