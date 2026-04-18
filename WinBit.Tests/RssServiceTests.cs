using FluentAssertions;
using Microsoft.Extensions.Options;
using WinBit.Core.Hosting;
using WinBit.Core.Persistence;
using WinBit.Core.Rss;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class RssServiceTests
{
    [Fact]
    public async Task Empty_tree_has_a_root_folder_with_no_children()
    {
        using var temp = new TempDirectory();
        var svc = new RssService(NewPaths(temp));

        var tree = await svc.GetTreeAsync();
        tree.Folders.Should().BeEmpty();
        tree.Feeds.Should().BeEmpty();
    }

    [Fact]
    public async Task UpsertFolder_creates_intermediate_folders()
    {
        using var temp = new TempDirectory();
        var svc = new RssService(NewPaths(temp));

        await svc.UpsertFolderAsync("TV/Shows/Drama");

        var tree = await svc.GetTreeAsync();
        var tv = tree.Folders.Should().ContainSingle().Which;
        tv.Name.Should().Be("TV");
        var shows = tv.Folders.Should().ContainSingle().Which;
        shows.Name.Should().Be("Shows");
        shows.Folders.Should().ContainSingle().Which.Name.Should().Be("Drama");
    }

    [Fact]
    public async Task UpsertFeed_at_root_and_in_a_folder_round_trips_through_json()
    {
        using var temp = new TempDirectory();
        var paths = NewPaths(temp);
        var svc1 = new RssService(paths);

        await svc1.UpsertFeedAsync("", new RssFeedConfig { Url = "http://root.example/feed" });
        await svc1.UpsertFeedAsync("TV/Shows", new RssFeedConfig
        {
            Url = "http://shows.example/feed",
            Title = "Shows",
            RefreshIntervalMinutesOverride = 15,
        });

        // New service instance → forces re-load from disk.
        var svc2 = new RssService(paths);
        var tree = await svc2.GetTreeAsync();

        tree.Feeds.Should().ContainSingle().Which.Url.Should().Be("http://root.example/feed");
        var shows = tree.Folders.Single().Folders.Single();
        shows.Feeds.Should().ContainSingle().Which.RefreshIntervalMinutesOverride.Should().Be(15);
    }

    [Fact]
    public async Task UpsertFeed_replaces_existing_feed_at_same_url()
    {
        using var temp = new TempDirectory();
        var svc = new RssService(NewPaths(temp));

        await svc.UpsertFeedAsync("", new RssFeedConfig { Url = "http://f/1", Title = "v1" });
        await svc.UpsertFeedAsync("", new RssFeedConfig { Url = "http://f/1", Title = "v2" });

        var tree = await svc.GetTreeAsync();
        tree.Feeds.Should().ContainSingle().Which.Title.Should().Be("v2");
    }

    [Fact]
    public async Task RemoveFolder_drops_folder_and_children()
    {
        using var temp = new TempDirectory();
        var svc = new RssService(NewPaths(temp));
        await svc.UpsertFolderAsync("TV/Shows");
        await svc.UpsertFeedAsync("TV/Shows", new RssFeedConfig { Url = "http://f/1" });

        await svc.RemoveFolderAsync("TV/Shows");

        var tree = await svc.GetTreeAsync();
        tree.Folders.Single().Folders.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveFeed_only_removes_the_matching_url()
    {
        using var temp = new TempDirectory();
        var svc = new RssService(NewPaths(temp));
        await svc.UpsertFeedAsync("TV", new RssFeedConfig { Url = "http://a" });
        await svc.UpsertFeedAsync("TV", new RssFeedConfig { Url = "http://b" });

        await svc.RemoveFeedAsync("TV", "http://a");

        var tv = (await svc.GetTreeAsync()).Folders.Single();
        tv.Feeds.Should().ContainSingle().Which.Url.Should().Be("http://b");
    }

    [Fact]
    public async Task MarkRefreshed_updates_timestamp_regardless_of_folder_depth()
    {
        using var temp = new TempDirectory();
        var svc = new RssService(NewPaths(temp));
        await svc.UpsertFeedAsync("TV/Shows", new RssFeedConfig { Url = "http://f" });

        var when = new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Utc);
        await svc.MarkRefreshedAsync("http://f", when);

        var feed = (await svc.GetTreeAsync()).Folders.Single().Folders.Single().Feeds.Single();
        feed.LastRefreshUtc.Should().Be(when);
    }

    [Fact]
    public async Task Mutations_raise_Changed_event()
    {
        using var temp = new TempDirectory();
        var svc = new RssService(NewPaths(temp));
        var raised = 0;
        svc.Changed += (_, _) => raised++;

        await svc.UpsertFolderAsync("X");
        await svc.UpsertFeedAsync("X", new RssFeedConfig { Url = "http://f" });
        await svc.RemoveFeedAsync("X", "http://f");
        await svc.RemoveFolderAsync("X");

        raised.Should().Be(4);
    }

    [Fact]
    public async Task MoveItem_relocates_feed_to_new_parent_folder()
    {
        using var temp = new TempDirectory();
        var svc = new RssService(NewPaths(temp));
        await svc.UpsertFeedAsync("TV", new RssFeedConfig { Url = "http://f/1", Title = "feedA" });

        await svc.MoveItemAsync("TV/feedA", "Movies/feedA");

        var tree = await svc.GetTreeAsync();
        tree.Folders.Single(f => f.Name == "TV").Feeds.Should().BeEmpty();
        tree.Folders.Single(f => f.Name == "Movies").Feeds.Single().Url.Should().Be("http://f/1");
    }

    [Fact]
    public async Task MoveItem_renames_folder_in_place()
    {
        using var temp = new TempDirectory();
        var svc = new RssService(NewPaths(temp));
        await svc.UpsertFolderAsync("TV/Drama");

        await svc.MoveItemAsync("TV/Drama", "TV/Reality");

        var tv = (await svc.GetTreeAsync()).Folders.Single();
        tv.Folders.Should().ContainSingle().Which.Name.Should().Be("Reality");
    }

    [Fact]
    public async Task MoveItem_renames_feed_via_leaf_segment()
    {
        using var temp = new TempDirectory();
        var svc = new RssService(NewPaths(temp));
        await svc.UpsertFeedAsync("", new RssFeedConfig { Url = "http://f/1", Title = "original" });

        await svc.MoveItemAsync("original", "Renamed");

        (await svc.GetTreeAsync()).Feeds.Single().Title.Should().Be("Renamed");
    }

    [Fact]
    public async Task MoveItem_throws_for_missing_source()
    {
        using var temp = new TempDirectory();
        var svc = new RssService(NewPaths(temp));

        Func<Task> act = () => svc.MoveItemAsync("Does/NotExist", "Somewhere");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Missing_feed_url_on_MarkRefreshed_is_a_no_op()
    {
        using var temp = new TempDirectory();
        var svc = new RssService(NewPaths(temp));

        await svc.MarkRefreshedAsync("http://does-not-exist", DateTime.UtcNow);
        // Should not throw; tree still empty.
        (await svc.GetTreeAsync()).Feeds.Should().BeEmpty();
    }

    private static Paths NewPaths(TempDirectory temp)
    {
        var opts = Options.Create(new WinBitCoreOptions { DataRoot = temp.Path });
        return new Paths(opts);
    }
}
