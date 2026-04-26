using System;
using System.IO;
using Xunit;

namespace LibtorrentSharp.Tests;

public class ForceRecheckSmokeTests
{
    private const string ValidMagnetUri = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=ubuntu-14.04.1-desktop-amd64.iso";

    [Fact]
    [Trait("Category", "Native")]
    public void ForceRecheck_OnValidMagnetHandle_DoesNotThrow()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = ValidMagnetUri }).Magnet!;
        Assert.True(handle.IsValid);

        handle.ForceRecheck();
    }

    [Fact]
    [Trait("Category", "Native")]
    public void ForceRecheck_OnInvalidMagnetHandle_IsNoOp()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = "not-a-magnet-uri" }).Magnet!;
        Assert.False(handle.IsValid);

        handle.ForceRecheck();
    }

    private static LibtorrentSession NewClient() =>
        new()
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };
}
