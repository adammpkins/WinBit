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
        version.Should().Be(1);

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
