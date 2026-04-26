using System;
using System.IO;
using Xunit;

namespace LibtorrentSharp.Tests;

public class RateLimitSmokeTests
{
    private const string ValidMagnetUri = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=ubuntu-14.04.1-desktop-amd64.iso";

    [Fact]
    [Trait("Category", "Native")]
    public void RateLimits_RoundTripThroughNative()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = ValidMagnetUri }).Magnet!;
        Assert.True(handle.IsValid);

        handle.UploadRateLimit = 128 * 1024;
        handle.DownloadRateLimit = 512 * 1024;

        Assert.Equal(128 * 1024, handle.UploadRateLimit);
        Assert.Equal(512 * 1024, handle.DownloadRateLimit);

        handle.UploadRateLimit = 0;
        handle.DownloadRateLimit = 0;

        Assert.Equal(0, handle.UploadRateLimit);
        Assert.Equal(0, handle.DownloadRateLimit);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void RateLimits_NonPositiveMeansUnlimited()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = ValidMagnetUri }).Magnet!;

        // First pin a non-zero limit so the subsequent "unlimited" set has something to clear.
        handle.UploadRateLimit = 4096;
        handle.DownloadRateLimit = 4096;

        handle.UploadRateLimit = -1;
        handle.DownloadRateLimit = 0;

        // libtorrent's getter may return 0 or -1 for the unlimited state depending on
        // its internal representation — either is acceptable, but neither should surface
        // as a real-looking positive limit.
        Assert.True(handle.UploadRateLimit <= 0, $"Expected <= 0 (unlimited) but got {handle.UploadRateLimit}");
        Assert.True(handle.DownloadRateLimit <= 0, $"Expected <= 0 (unlimited) but got {handle.DownloadRateLimit}");
    }

    [Fact]
    [Trait("Category", "Native")]
    public void RateLimits_OnInvalidHandle_AreNoOp()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = "not-a-magnet" }).Magnet!;
        Assert.False(handle.IsValid);

        // No throw; reads return 0.
        handle.UploadRateLimit = 1000;
        Assert.Equal(0, handle.UploadRateLimit);
    }

    private static LibtorrentSession NewClient() =>
        new()
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };
}
