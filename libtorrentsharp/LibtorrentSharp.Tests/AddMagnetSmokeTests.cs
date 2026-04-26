using System;
using System.IO;
using Xunit;

namespace LibtorrentSharp.Tests;

public class AddMagnetSmokeTests
{
    // Ubuntu 14.04.1 Desktop 64-bit ISO — a stable, syntactically valid public magnet.
    // The test does not wait for metadata or peers, so the DHT doesn't need to be live.
    private const string ValidMagnetUri = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=ubuntu-14.04.1-desktop-amd64.iso";

    [Fact]
    [Trait("Category", "Native")]
    public void Add_MagnetWithValidUri_ReturnsValidHandle()
    {
        using var client = new LibtorrentSession { DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N")) };

        var result = client.Add(new AddTorrentParams { MagnetUri = ValidMagnetUri });

        Assert.NotNull(result.Magnet);
        Assert.True(result.IsValid);
        Assert.StartsWith(client.DefaultDownloadPath, result.Magnet!.SavePath);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void Add_MagnetWithMalformedUri_ReturnsInvalidHandle()
    {
        using var client = new LibtorrentSession();

        var result = client.Add(new AddTorrentParams { MagnetUri = "not-a-magnet-uri" });

        Assert.NotNull(result.Magnet);
        Assert.False(result.IsValid);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void Add_MagnetWithEmptyUri_Throws()
    {
        using var client = new LibtorrentSession();

        Assert.Throws<ArgumentException>(() => client.Add(new AddTorrentParams { MagnetUri = string.Empty }));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void Add_WithNoSourceSet_Throws()
    {
        using var client = new LibtorrentSession();

        Assert.Throws<ArgumentException>(() => client.Add(new AddTorrentParams()));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void Add_WithMultipleSources_Throws()
    {
        using var client = new LibtorrentSession();

        Assert.Throws<ArgumentException>(() => client.Add(new AddTorrentParams
        {
            MagnetUri = ValidMagnetUri,
            ResumeData = new byte[] { 0x01, 0x02 },
        }));
    }
}
