using FluentAssertions;
using Microsoft.Extensions.Options;
using WinBit.Core.Hosting;
using WinBit.Core.Persistence;
using WinBit.Core.Rss;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class AutoDownloaderServiceTests
{
    [Fact]
    public async Task Empty_store_returns_empty_list()
    {
        using var temp = new TempDirectory();
        var svc = new AutoDownloaderService(NewPaths(temp));
        (await svc.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Upsert_then_GetAll_returns_rule_sorted_by_name()
    {
        using var temp = new TempDirectory();
        var svc = new AutoDownloaderService(NewPaths(temp));

        await svc.UpsertAsync(new AutoDownloadRule { Name = "bravo", MustContain = "B" });
        await svc.UpsertAsync(new AutoDownloadRule { Name = "alpha", MustContain = "A" });

        (await svc.GetAllAsync()).Select(r => r.Name).Should().Equal("alpha", "bravo");
    }

    [Fact]
    public async Task Upsert_replaces_existing_rule_at_same_name()
    {
        using var temp = new TempDirectory();
        var svc = new AutoDownloaderService(NewPaths(temp));

        await svc.UpsertAsync(new AutoDownloadRule { Name = "r", MustContain = "v1" });
        await svc.UpsertAsync(new AutoDownloadRule { Name = "r", MustContain = "v2" });

        (await svc.GetAsync("r"))!.MustContain.Should().Be("v2");
    }

    [Fact]
    public async Task Rules_round_trip_through_json_to_a_fresh_service_instance()
    {
        using var temp = new TempDirectory();
        var paths = NewPaths(temp);

        var first = new AutoDownloaderService(paths);
        await first.UpsertAsync(new AutoDownloadRule
        {
            Name = "TV",
            MustContain = "1080p",
            MustNotContain = "HDR",
            EpisodeFilter = "1x5-;",
            SmartFilter = true,
            AffectedFeeds = new[] { "http://feed" },
            PreviouslyMatchedEpisodes = new[] { "01x05" },
        });

        var fresh = new AutoDownloaderService(paths);
        var reloaded = (await fresh.GetAsync("TV"))!;

        reloaded.MustContain.Should().Be("1080p");
        reloaded.MustNotContain.Should().Be("HDR");
        reloaded.EpisodeFilter.Should().Be("1x5-;");
        reloaded.SmartFilter.Should().BeTrue();
        reloaded.AffectedFeeds.Should().ContainSingle().Which.Should().Be("http://feed");
        reloaded.PreviouslyMatchedEpisodes.Should().ContainSingle().Which.Should().Be("01x05");
    }

    [Fact]
    public async Task Remove_drops_rule_and_persists()
    {
        using var temp = new TempDirectory();
        var paths = NewPaths(temp);
        var svc = new AutoDownloaderService(paths);
        await svc.UpsertAsync(new AutoDownloadRule { Name = "r" });

        await svc.RemoveAsync("r");

        (await svc.GetAsync("r")).Should().BeNull();
        (await new AutoDownloaderService(paths).GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Upsert_with_empty_name_throws()
    {
        using var temp = new TempDirectory();
        var svc = new AutoDownloaderService(NewPaths(temp));

        Func<Task> act = () => svc.UpsertAsync(new AutoDownloadRule { Name = "" });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Mutations_raise_Changed()
    {
        using var temp = new TempDirectory();
        var svc = new AutoDownloaderService(NewPaths(temp));
        var hits = 0;
        svc.Changed += (_, _) => hits++;

        await svc.UpsertAsync(new AutoDownloadRule { Name = "r" });
        await svc.RemoveAsync("r");
        await svc.RemoveAsync("not-there"); // no-op → no event

        hits.Should().Be(2);
    }

    private static Paths NewPaths(TempDirectory temp)
    {
        var opts = Options.Create(new WinBitCoreOptions { DataRoot = temp.Path });
        return new Paths(opts);
    }
}
