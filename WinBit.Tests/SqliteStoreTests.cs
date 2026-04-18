using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using WinBit.Core.Hosting;
using WinBit.Core.Persistence;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class SqliteStoreTests
{
    [Fact]
    public async Task Store_opens_in_WAL_mode()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));

        await using var store = new SqliteTorrentStateStore(paths);

        var journalMode = await store.ExecuteReadAsync(async (conn, ct) =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode;";
            return (string)(await cmd.ExecuteScalarAsync(ct))!;
        });

        journalMode.Should().BeEquivalentTo("wal");
    }

    [Fact]
    public async Task Store_applies_initial_migration()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));

        await using var store = new SqliteTorrentStateStore(paths);
        await store.InitializeAsync();

        File.Exists(paths.StateDatabase).Should().BeTrue();

        var version = await store.ExecuteReadAsync(async (conn, ct) =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        });
        // Bumped when 002_rss_read.sql landed in the M10 Web UI rollout.
        version.Should().Be(2);

        var tables = await store.ExecuteReadAsync(async (conn, ct) =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
            var names = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                names.Add(reader.GetString(0));
            }
            return names;
        });

        tables.Should().Contain(new[] { "log_entry", "peer_log", "schema_version", "torrent" });
    }

    [Fact]
    public async Task Store_migration_is_idempotent_across_reopens()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));

        await using (var store = new SqliteTorrentStateStore(paths))
        {
            await store.InitializeAsync();
        }

        await using var reopened = new SqliteTorrentStateStore(paths);
        var version = await reopened.ExecuteReadAsync(async (conn, ct) =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM schema_version;";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        });

        version.Should().Be(1, "migration must not re-insert the schema_version row on reopen");
    }

    [Fact]
    public async Task Store_upserts_and_removes_torrent_rows_through_the_writer_queue()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));

        await using var store = new SqliteTorrentStateStore(paths);

        async Task UpsertAsync(string infoHash, string name, string savePath) =>
            await store.ExecuteWriteAsync(async (conn, ct) =>
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO torrent (info_hash, name, save_path, added_utc)
                                    VALUES (@hash, @name, @path, @added)
                                    ON CONFLICT(info_hash) DO UPDATE SET
                                        name = excluded.name,
                                        save_path = excluded.save_path;";
                cmd.Parameters.AddWithValue("@hash", infoHash);
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@path", savePath);
                cmd.Parameters.AddWithValue("@added", DateTime.UtcNow.ToString("O"));
                await cmd.ExecuteNonQueryAsync(ct);
            });

        await UpsertAsync("aaaa", "original", "C:/a");
        await UpsertAsync("aaaa", "renamed", "C:/b");
        await UpsertAsync("bbbb", "second", "C:/c");

        var (name, path) = await store.ExecuteReadAsync(async (conn, ct) =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name, save_path FROM torrent WHERE info_hash = 'aaaa';";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return (reader.GetString(0), reader.GetString(1));
        });
        name.Should().Be("renamed");
        path.Should().Be("C:/b");

        await store.ExecuteWriteAsync(async (conn, ct) =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM torrent WHERE info_hash = @hash;";
            cmd.Parameters.AddWithValue("@hash", "aaaa");
            await cmd.ExecuteNonQueryAsync(ct);
        });

        var remaining = await store.ExecuteReadAsync(async (conn, ct) =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT info_hash FROM torrent ORDER BY info_hash;";
            var hashes = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                hashes.Add(reader.GetString(0));
            }
            return hashes;
        });
        remaining.Should().ContainSingle().Which.Should().Be("bbbb");
    }

    [Fact]
    public async Task Store_concurrent_upserts_on_same_row_land_exactly_once()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));

        await using var store = new SqliteTorrentStateStore(paths);

        async Task UpsertAsync(int n) =>
            await store.ExecuteWriteAsync(async (conn, ct) =>
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO torrent (info_hash, name, save_path, added_utc)
                                    VALUES ('same-hash', @name, 'C:/x', @added)
                                    ON CONFLICT(info_hash) DO UPDATE SET name = excluded.name;";
                cmd.Parameters.AddWithValue("@name", $"iter-{n}");
                cmd.Parameters.AddWithValue("@added", DateTime.UtcNow.ToString("O"));
                await cmd.ExecuteNonQueryAsync(ct);
            });

        await Task.WhenAll(Enumerable.Range(0, 32).Select(UpsertAsync));

        var count = await store.ExecuteReadAsync(async (conn, ct) =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM torrent;";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        });
        count.Should().Be(1, "32 concurrent upserts on the same info_hash must collapse into a single row");
    }

    [Fact]
    public async Task FastResume_round_trips_when_version_matches()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));
        await using var store = new SqliteTorrentStateStore(paths);

        var id = WinBit.Core.Common.TorrentId.FromInfoHash("a".PadRight(40, '0'));
        await ((ITorrentStateStore)store).UpsertTorrentAsync(new TorrentStateRecord
        {
            Id = id,
            Name = "example.iso",
            SavePath = @"D:\downloads",
            AddedUtc = DateTime.UtcNow,
        });

        var blob = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x12, 0x34 };
        await ((ITorrentStateStore)store).SaveFastResumeAsync(id, blob, version: 1);

        var reloaded = await ((ITorrentStateStore)store).LoadFastResumeAsync(id, expectedVersion: 1);
        reloaded.Should().NotBeNull().And.Equal(blob);
    }

    [Fact]
    public async Task FastResume_returns_null_on_version_mismatch_so_caller_rechecks()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));
        await using var store = new SqliteTorrentStateStore(paths);

        var id = WinBit.Core.Common.TorrentId.FromInfoHash("b".PadRight(40, '0'));
        await ((ITorrentStateStore)store).UpsertTorrentAsync(new TorrentStateRecord
        {
            Id = id,
            Name = "example.iso",
            SavePath = @"D:\downloads",
            AddedUtc = DateTime.UtcNow,
        });
        await ((ITorrentStateStore)store).SaveFastResumeAsync(id, new byte[] { 1, 2, 3 }, version: 1);

        var mismatch = await ((ITorrentStateStore)store).LoadFastResumeAsync(id, expectedVersion: 2);
        mismatch.Should().BeNull("version bump must discard the stale blob and force a re-check");
    }

    [Fact]
    public async Task FastResume_returns_null_when_no_row_exists()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));
        await using var store = new SqliteTorrentStateStore(paths);

        var missing = await ((ITorrentStateStore)store).LoadFastResumeAsync(
            WinBit.Core.Common.TorrentId.FromInfoHash("c".PadRight(40, '0')),
            expectedVersion: 1);
        missing.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_and_RemoveTorrentAsync_reflect_current_state()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));
        await using var store = new SqliteTorrentStateStore(paths);

        var a = WinBit.Core.Common.TorrentId.FromInfoHash("aa".PadRight(40, '0'));
        var b = WinBit.Core.Common.TorrentId.FromInfoHash("bb".PadRight(40, '0'));
        var now = DateTime.UtcNow;

        await ((ITorrentStateStore)store).UpsertTorrentAsync(new TorrentStateRecord { Id = a, Name = "A", SavePath = "/a", AddedUtc = now });
        await ((ITorrentStateStore)store).UpsertTorrentAsync(new TorrentStateRecord { Id = b, Name = "B", SavePath = "/b", AddedUtc = now.AddSeconds(1) });

        (await ((ITorrentStateStore)store).GetAllAsync()).Select(r => r.Id).Should().ContainInOrder(a, b);

        await ((ITorrentStateStore)store).RemoveTorrentAsync(a);
        var remaining = await ((ITorrentStateStore)store).GetAllAsync();
        remaining.Should().ContainSingle().Which.Id.Should().Be(b);
    }

    [Fact]
    public async Task FastResume_blob_round_trips_from_MonoTorrent_through_store_back_into_FastResume()
    {
        // End-to-end: MonoTorrent FastResume → Encode → ITorrentStateStore →
        // LoadFastResumeAsync → FastResume.TryLoad → matching InfoHashes.
        // MonoTorrent's LoadFastResumeAsync trusts a TryLoad-valid FastResume and skips the
        // hash check (docs/torrent-engine.md "Fast-resume"), so a clean round-trip here
        // proves the "no re-check" path.
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));
        await using var store = new SqliteTorrentStateStore(paths);

        var sha1 = new byte[20];
        new Random(42).NextBytes(sha1);
        var infoHashes = MonoTorrent.InfoHashes.FromV1(new MonoTorrent.InfoHash(sha1));

        var bitfield = new MonoTorrent.ReadOnlyBitField(8);
        var original = new MonoTorrent.Client.FastResume(infoHashes, bitfield, bitfield);
        byte[] blob = original.Encode();
        blob.Length.Should().BeGreaterThan(0);

        var id = WinBit.Core.Common.TorrentId.FromInfoHash(infoHashes.V1!.ToHex());

        ITorrentStateStore typed = store;
        await typed.UpsertTorrentAsync(new TorrentStateRecord
        {
            Id = id,
            Name = "fast-resume-test",
            SavePath = temp.Path,
            AddedUtc = DateTime.UtcNow,
        });
        await typed.SaveFastResumeAsync(id, blob, version: 1);

        var reloaded = await typed.LoadFastResumeAsync(id, expectedVersion: 1);
        reloaded.Should().NotBeNull().And.Equal(blob);

        using var stream = new MemoryStream(reloaded!);
        MonoTorrent.Client.FastResume.TryLoad(stream, out var rehydrated).Should().BeTrue("round-tripped bytes must parse back into a FastResume");
        rehydrated.Should().NotBeNull();
        rehydrated!.InfoHashes.V1!.ToHex().Should().Be(infoHashes.V1!.ToHex(), "the rehydrated FastResume must carry the same info-hash MonoTorrent keys by");
    }

    [Fact]
    public async Task Store_serializes_concurrent_writes_through_the_writer_queue()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));

        await using var store = new SqliteTorrentStateStore(paths);

        async Task InsertAsync(int id) =>
            await store.ExecuteWriteAsync(async (conn, ct) =>
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO log_entry (ts_utc, severity, message) VALUES (@ts, 0, @msg);";
                cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("O"));
                cmd.Parameters.AddWithValue("@msg", $"row-{id}");
                await cmd.ExecuteNonQueryAsync(ct);
            });

        await Task.WhenAll(Enumerable.Range(0, 32).Select(InsertAsync));

        var count = await store.ExecuteReadAsync(async (conn, ct) =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM log_entry;";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        });

        count.Should().Be(32);
    }
}
