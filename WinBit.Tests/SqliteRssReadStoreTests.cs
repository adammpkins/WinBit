using FluentAssertions;
using Microsoft.Extensions.Options;
using WinBit.Core.Hosting;
using WinBit.Core.Logging;
using WinBit.Core.Persistence;
using WinBit.Core.Rss;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class SqliteRssReadStoreTests
{
    [Fact]
    public async Task Mark_then_LoadAll_round_trips()
    {
        using var temp = new TempDirectory();
        await using var db = MakeStore(temp);
        var store = new SqliteRssReadStore(db);

        await store.MarkAsync("http://feed/a", "id1");
        await store.MarkAsync("http://feed/a", "id2");
        await store.MarkAsync("http://feed/b", "xyz");

        var rows = await store.LoadAllAsync();
        rows.Should().BeEquivalentTo(new[]
        {
            ("http://feed/a", "id1"),
            ("http://feed/a", "id2"),
            ("http://feed/b", "xyz"),
        });
    }

    [Fact]
    public async Task Mark_is_idempotent_on_duplicate_pair()
    {
        using var temp = new TempDirectory();
        await using var db = MakeStore(temp);
        var store = new SqliteRssReadStore(db);

        await store.MarkAsync("http://f", "A");
        await store.MarkAsync("http://f", "A");

        (await store.LoadAllAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task MarkMany_writes_batch_under_transaction()
    {
        using var temp = new TempDirectory();
        await using var db = MakeStore(temp);
        var store = new SqliteRssReadStore(db);

        await store.MarkManyAsync("http://f", new[] { "A", "B", "C" });
        (await store.LoadAllAsync()).Select(r => r.ArticleId)
            .Should().BeEquivalentTo(new[] { "A", "B", "C" });
    }

    [Fact]
    public async Task State_survives_reopening_the_database()
    {
        using var temp = new TempDirectory();

        var db1 = MakeStore(temp);
        var store1 = new SqliteRssReadStore(db1);
        await store1.MarkAsync("http://f", "persisted");
        await db1.DisposeAsync();

        await using var db2 = MakeStore(temp);
        var store2 = new SqliteRssReadStore(db2);

        (await store2.LoadAllAsync()).Should().ContainSingle()
            .Which.Should().Be(("http://f", "persisted"));
    }

    private static SqliteTorrentStateStore MakeStore(TempDirectory temp)
    {
        var opts = Options.Create(new WinBitCoreOptions { DataRoot = temp.Path });
        return new SqliteTorrentStateStore(new Paths(opts), new LogService());
    }
}
