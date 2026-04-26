using System;
using System.IO;
using Xunit;

namespace LibtorrentSharp.Tests;

public class GetPeersSmokeTests
{
    private const string ValidMagnetUri = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=ubuntu-14.04.1-desktop-amd64.iso";

    [Fact]
    [Trait("Category", "Native")]
    public void GetPeers_OnFreshlyAddedMagnet_ReturnsEmptyList()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = ValidMagnetUri }).Magnet!;
        Assert.True(handle.IsValid);

        // No network activity yet (paused + freshly added) — peer count is zero but
        // the P/Invoke round-trip must succeed and return an empty list.
        var peers = handle.GetPeers();

        Assert.NotNull(peers);
        Assert.Empty(peers);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetPeers_OnInvalidHandle_ReturnsEmptyList()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = "not-a-magnet" }).Magnet!;
        Assert.False(handle.IsValid);

        var peers = handle.GetPeers();

        Assert.NotNull(peers);
        Assert.Empty(peers);
    }

    private static LibtorrentSession NewClient() =>
        new()
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };
}
