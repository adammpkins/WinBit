using System;
using System.IO;
using LibtorrentSharp.Enums;
using Xunit;

namespace LibtorrentSharp.Tests;

public class GetStatusSmokeTests
{
    private const string ValidMagnetUri = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=ubuntu-14.04.1-desktop-amd64.iso";

    [Fact]
    [Trait("Category", "Native")]
    public void GetCurrentStatus_OnFreshlyAddedMagnet_ReturnsPopulatedSnapshot()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = ValidMagnetUri }).Magnet!;
        Assert.True(handle.IsValid);

        var status = handle.GetCurrentStatus();

        Assert.NotNull(status);
        Assert.Equal(0f, status.Progress);
        Assert.Equal(0, status.BytesUploaded);
        Assert.Equal(0, status.BytesDownloaded);

        // Freshly added + paused — no throughput, so ETA and Ratio are the null sentinels.
        Assert.Null(status.Eta);
        Assert.Null(status.Ratio);

        // Save path reflects what we passed at AddMagnet time.
        Assert.Equal(client.DefaultDownloadPath, status.SavePath);

        Assert.Equal(string.Empty, status.ErrorMessage);

        // Durations start at zero but the types must be valid.
        Assert.True(status.ActiveDuration >= TimeSpan.Zero);
        Assert.True(status.SeedingDuration >= TimeSpan.Zero);

        // State is one of the valid enum values — for a paused magnet without metadata
        // it's typically downloading_metadata or checking_files.
        Assert.True(Enum.IsDefined(typeof(TorrentState), status.State));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetCurrentStatus_OnInvalidHandle_Throws()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = "not-a-magnet" }).Magnet!;
        Assert.False(handle.IsValid);

        Assert.Throws<InvalidOperationException>(() => handle.GetCurrentStatus());
    }

    private static LibtorrentSession NewClient() =>
        new()
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };
}
