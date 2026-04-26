using FluentAssertions;
using WinBit.Core.Common;
using WinBit.Core.Persistence;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests.Persistence;

public sealed class JsonCustomNameStoreTests
{
    private static readonly TorrentId TorrentA = TorrentId.FromInfoHash("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly TorrentId TorrentB = TorrentId.FromInfoHash("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

    [Fact]
    public void GetName_ReturnsNull_WhenNotSet()
    {
        using var temp = new TempDirectory();
        var store = new JsonCustomNameStore(TestPaths.ForTemp(temp));

        store.GetName(TorrentA).Should().BeNull();
    }

    [Fact]
    public async Task SetNameAsync_PersistsToFile()
    {
        using var temp = new TempDirectory();
        var paths = TestPaths.ForTemp(temp);
        var store = new JsonCustomNameStore(paths);

        await store.SetNameAsync(TorrentA, "My Custom Name");

        store.GetName(TorrentA).Should().Be("My Custom Name");
        File.Exists(paths.CustomNamesFile).Should().BeTrue();
    }

    [Fact]
    public async Task SetNameAsync_UpdatesExistingName()
    {
        using var temp = new TempDirectory();
        var store = new JsonCustomNameStore(TestPaths.ForTemp(temp));

        await store.SetNameAsync(TorrentA, "First Name");
        await store.SetNameAsync(TorrentA, "Second Name");

        store.GetName(TorrentA).Should().Be("Second Name");
    }

    [Fact]
    public async Task RemoveNameAsync_ClearsName()
    {
        using var temp = new TempDirectory();
        var store = new JsonCustomNameStore(TestPaths.ForTemp(temp));

        await store.SetNameAsync(TorrentA, "To Be Removed");
        await store.RemoveNameAsync(TorrentA);

        store.GetName(TorrentA).Should().BeNull();
    }

    [Fact]
    public async Task Load_RestoresNamesFromFile()
    {
        using var temp = new TempDirectory();
        var paths = TestPaths.ForTemp(temp);

        // Write names via a first instance.
        var writer = new JsonCustomNameStore(paths);
        await writer.SetNameAsync(TorrentA, "Persisted Name");
        await writer.SetNameAsync(TorrentB, "Another Name");

        // A second instance with no in-memory state must reload from disk.
        // Any async call triggers EnsureLoadedAsync, so we use RemoveNameAsync for
        // a torrent that doesn't exist (no-op) purely to warm the cache.
        var reader = new JsonCustomNameStore(paths);
        await reader.RemoveNameAsync(TorrentId.FromInfoHash("cccccccccccccccccccccccccccccccccccccccc"));

        reader.GetName(TorrentA).Should().Be("Persisted Name");
        reader.GetName(TorrentB).Should().Be("Another Name");
    }
}
