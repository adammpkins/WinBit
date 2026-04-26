using System;
using System.IO;
using Xunit;

namespace LibtorrentSharp.Tests;

public class SuperSeedingSmokeTests
{
    private const string ValidMagnetUri = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=ubuntu-14.04.1-desktop-amd64.iso";

    [Fact]
    [Trait("Category", "Native")]
    public void SetSuperSeeding_OnValidHandle_DoesNotThrow()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = ValidMagnetUri }).Magnet!;
        Assert.True(handle.IsValid);

        handle.SetSuperSeeding(true);
        handle.SetSuperSeeding(false);
        handle.SetSuperSeeding(true);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void SetSuperSeeding_OnInvalidHandle_IsNoOp()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = "not-a-magnet" }).Magnet!;
        Assert.False(handle.IsValid);

        handle.SetSuperSeeding(true);
    }

    private static LibtorrentSession NewClient() =>
        new()
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };
}
