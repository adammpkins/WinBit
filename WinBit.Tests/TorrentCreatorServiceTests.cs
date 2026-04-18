using FluentAssertions;
using MonoTorrent;
using WinBit.Core.BitTorrent;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class TorrentCreatorServiceTests
{
    [Fact]
    public async Task Create_writes_parseable_torrent_with_metadata_from_request()
    {
        using var temp = new TempDirectory();
        var sourceDir = Path.Combine(temp.Path, "payload");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllBytes(Path.Combine(sourceDir, "a.bin"), new byte[1024]);
        File.WriteAllBytes(Path.Combine(sourceDir, "b.bin"), new byte[2048]);

        var output = Path.Combine(temp.Path, "out.torrent");
        var service = new TorrentCreatorService();

        var result = await service.CreateAsync(new TorrentCreateRequest
        {
            SourcePath = sourceDir,
            OutputPath = output,
            Name = "payload",
            Comment = "unit-test comment",
            CreatedBy = "WinBit test",
            IsPrivate = true,
            TrackerTiers = new[]
            {
                new[] { "udp://tracker.example:6969/announce" },
                new[] { "http://backup.example/announce" },
            },
            WebSeeds = new[] { "https://seed.example/payload/" },
        });

        result.IsSuccess.Should().BeTrue(result.Error);
        File.Exists(output).Should().BeTrue();

        var loaded = Torrent.Load(output);
        loaded.Name.Should().Be("payload");
        loaded.IsPrivate.Should().BeTrue();
        loaded.Comment.Should().Be("unit-test comment");
        loaded.CreatedBy.Should().Be("WinBit test");
        loaded.AnnounceUrls.Should().HaveCount(2);
        loaded.AnnounceUrls[0].Should().Contain("udp://tracker.example:6969/announce");
        loaded.HttpSeeds.Select(u => u.ToString()).Should().Contain("https://seed.example/payload/");
    }

    [Fact]
    public async Task Create_reports_progress_for_large_source()
    {
        using var temp = new TempDirectory();
        var sourceDir = Path.Combine(temp.Path, "big");
        Directory.CreateDirectory(sourceDir);
        // ~1.5 MiB of junk — enough to fire at least one Hashed event.
        File.WriteAllBytes(Path.Combine(sourceDir, "blob.bin"), new byte[1_500_000]);

        var output = Path.Combine(temp.Path, "out.torrent");
        var service = new TorrentCreatorService();

        var progress = new List<TorrentCreateProgress>();
        var reporter = new Progress<TorrentCreateProgress>(p => progress.Add(p));

        var result = await service.CreateAsync(new TorrentCreateRequest
        {
            SourcePath = sourceDir,
            OutputPath = output,
        }, reporter);

        result.IsSuccess.Should().BeTrue(result.Error);
        // Give Progress<T> its posted callbacks time to land on the sync-context scheduler.
        await Task.Delay(50);
        progress.Should().NotBeEmpty();
        progress.Max(p => p.OverallBytesHashed).Should().BeGreaterOrEqualTo(1_500_000);
        progress[0].OverallSize.Should().Be(1_500_000);
    }

    [Fact]
    public async Task Create_fails_when_source_missing()
    {
        using var temp = new TempDirectory();
        var service = new TorrentCreatorService();

        var result = await service.CreateAsync(new TorrentCreateRequest
        {
            SourcePath = Path.Combine(temp.Path, "missing"),
            OutputPath = Path.Combine(temp.Path, "out.torrent"),
        });

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("does not exist");
    }

    [Fact]
    public async Task Create_fails_fast_on_blank_paths()
    {
        var service = new TorrentCreatorService();
        (await service.CreateAsync(new TorrentCreateRequest { SourcePath = "", OutputPath = "x" })).IsSuccess.Should().BeFalse();
        (await service.CreateAsync(new TorrentCreateRequest { SourcePath = "x", OutputPath = "" })).IsSuccess.Should().BeFalse();
    }
}
