using System;
using System.IO;
using System.Linq;
using LibtorrentSharp.Enums;
using Xunit;

namespace LibtorrentSharp.Tests;

public class GetTrackersSmokeTests
{
    // Magnet URI with two trackers bolted on so GetTrackers can observe them without
    // waiting for metadata resolution.
    private const string MagnetWithTrackers =
        "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c" +
        "&tr=udp%3A%2F%2Ftracker.example.org%3A6969" +
        "&tr=udp%3A%2F%2Ftracker.example.net%3A1337";

    [Fact]
    [Trait("Category", "Native")]
    public void GetTrackers_OnMagnetWithTrackers_ReturnsExpectedUrls()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = MagnetWithTrackers }).Magnet!;
        Assert.True(handle.IsValid);

        var trackers = handle.GetTrackers();

        Assert.NotNull(trackers);
        Assert.Equal(2, trackers.Count);

        var urls = trackers.Select(t => t.Url).ToHashSet();
        Assert.Contains("udp://tracker.example.org:6969", urls);
        Assert.Contains("udp://tracker.example.net:1337", urls);

        // Fresh tracker — nothing announced yet, scrape fields are the -1 sentinel.
        Assert.All(trackers, t => Assert.Equal(-1, t.ScrapeComplete));
        Assert.All(trackers, t => Assert.Equal(-1, t.ScrapeIncomplete));

        // **marshal-contract verification**: trackers added via
        // a magnet URI's `tr=` param should have the MagnetLink bit set
        // in their Source flags. Locks down the typed-enum
        // cast contract end-to-end (raw uint8_t → TrackerSource via
        // the (TrackerSource)entry.source cast in TrackerInfoMarshaller).
        Assert.All(trackers, t => Assert.True(
            t.Source.HasFlag(TrackerSource.MagnetLink),
            $"Magnet-added tracker {t.Url} missing MagnetLink flag (got Source={t.Source})"));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetTrackers_OnPlainMagnet_ReturnsEmptyList()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c" }).Magnet!;
        Assert.True(handle.IsValid);

        Assert.Empty(handle.GetTrackers());
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetTrackers_OnInvalidHandle_ReturnsEmptyList()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = "not-a-magnet" }).Magnet!;
        Assert.False(handle.IsValid);

        Assert.Empty(handle.GetTrackers());
    }

    private static LibtorrentSession NewClient() =>
        new()
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };
}
