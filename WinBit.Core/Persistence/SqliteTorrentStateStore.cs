using System.Text;
using Microsoft.Data.Sqlite;
using WinBit.Core.Common;

namespace WinBit.Core.Persistence;

/// <summary>
/// SQLite-backed torrent state store. Opens the database in WAL mode, runs embedded
/// migrations, and serializes writes through a single connection + semaphore. Readers open
/// fresh pooled connections and proceed in parallel under WAL. Exposes both the generic
/// <see cref="ExecuteWriteAsync"/> / <see cref="ExecuteReadAsync{T}"/> helpers and the typed
/// <see cref="ITorrentStateStore"/> surface used by the engine.
/// </summary>
public sealed class SqliteTorrentStateStore : ITorrentStateStore, IAsyncDisposable
{
    private const int CurrentSchemaVersion = 1;

    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Lazy<Task> _initTask;
    private SqliteConnection? _writeConnection;

    public SqliteTorrentStateStore(Paths paths)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.StateDatabase,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ConnectionString;

        _initTask = new Lazy<Task>(InitializeInternalAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Opens the database (WAL mode, pragmas, migrations). Safe to call repeatedly.</summary>
    public Task InitializeAsync() => _initTask.Value;

    private async Task InitializeInternalAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await ExecuteNonQueryAsync(connection, "PRAGMA journal_mode = WAL;").ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "PRAGMA synchronous = NORMAL;").ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "PRAGMA foreign_keys = ON;").ConfigureAwait(false);

        await MigrateAsync(connection).ConfigureAwait(false);

        _writeConnection = connection;
    }

    /// <summary>Runs <paramref name="action"/> under the single writer lock on the shared write connection.</summary>
    public async Task ExecuteWriteAsync(Func<SqliteConnection, CancellationToken, Task> action, CancellationToken ct = default)
    {
        await InitializeAsync().ConfigureAwait(false);
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await action(_writeConnection!, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Runs <paramref name="action"/> on a fresh pooled read connection.</summary>
    public async Task<T> ExecuteReadAsync<T>(Func<SqliteConnection, CancellationToken, Task<T>> action, CancellationToken ct = default)
    {
        await InitializeAsync().ConfigureAwait(false);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return await action(conn, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_initTask.IsValueCreated)
        {
            await Task.WhenAny(_initTask.Value).ConfigureAwait(false);
        }

        if (_writeConnection is not null)
        {
            await _writeConnection.DisposeAsync().ConfigureAwait(false);
            _writeConnection = null;
        }

        SqliteConnection.ClearAllPools();
        _writeLock.Dispose();
    }

    public Task UpsertTorrentAsync(TorrentStateRecord record, CancellationToken ct = default) =>
        ExecuteWriteAsync(async (conn, inner) =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO torrent (info_hash, name, save_path, category, tags, added_utc, completed_utc)
                                VALUES (@hash, @name, @path, @category, @tags, @added, @completed)
                                ON CONFLICT(info_hash) DO UPDATE SET
                                    name          = excluded.name,
                                    save_path     = excluded.save_path,
                                    category      = excluded.category,
                                    tags          = excluded.tags,
                                    completed_utc = excluded.completed_utc;";
            cmd.Parameters.AddWithValue("@hash", record.Id.Value);
            cmd.Parameters.AddWithValue("@name", record.Name);
            cmd.Parameters.AddWithValue("@path", record.SavePath);
            cmd.Parameters.AddWithValue("@category", (object?)record.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tags", (object?)record.Tags ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@added", record.AddedUtc.ToString("O"));
            cmd.Parameters.AddWithValue("@completed", (object?)record.CompletedUtc?.ToString("O") ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(inner).ConfigureAwait(false);
        }, ct);

    public Task RemoveTorrentAsync(TorrentId id, CancellationToken ct = default) =>
        ExecuteWriteAsync(async (conn, inner) =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM torrent WHERE info_hash = @hash;";
            cmd.Parameters.AddWithValue("@hash", id.Value);
            await cmd.ExecuteNonQueryAsync(inner).ConfigureAwait(false);
        }, ct);

    public Task SaveFastResumeAsync(TorrentId id, byte[] blob, int version, CancellationToken ct = default) =>
        ExecuteWriteAsync(async (conn, inner) =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE torrent
                                SET fast_resume = @blob, resume_ver = @ver
                                WHERE info_hash = @hash;";
            cmd.Parameters.AddWithValue("@hash", id.Value);
            cmd.Parameters.AddWithValue("@blob", blob);
            cmd.Parameters.AddWithValue("@ver", version);
            await cmd.ExecuteNonQueryAsync(inner).ConfigureAwait(false);
        }, ct);

    public Task<byte[]?> LoadFastResumeAsync(TorrentId id, int expectedVersion, CancellationToken ct = default) =>
        ExecuteReadAsync<byte[]?>(async (conn, inner) =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT fast_resume, resume_ver FROM torrent WHERE info_hash = @hash;";
            cmd.Parameters.AddWithValue("@hash", id.Value);
            await using var reader = await cmd.ExecuteReaderAsync(inner).ConfigureAwait(false);
            if (!await reader.ReadAsync(inner).ConfigureAwait(false))
            {
                return null;
            }
            if (reader.IsDBNull(0))
            {
                return null;
            }
            if (reader.GetInt32(1) != expectedVersion)
            {
                return null;
            }
            var blob = (byte[])reader.GetValue(0);
            return blob;
        }, ct);

    public Task<IReadOnlyList<TorrentStateRecord>> GetAllAsync(CancellationToken ct = default) =>
        ExecuteReadAsync<IReadOnlyList<TorrentStateRecord>>(async (conn, inner) =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT info_hash, name, save_path, category, tags, added_utc, completed_utc
                                FROM torrent
                                ORDER BY added_utc;";
            var result = new List<TorrentStateRecord>();
            await using var reader = await cmd.ExecuteReaderAsync(inner).ConfigureAwait(false);
            while (await reader.ReadAsync(inner).ConfigureAwait(false))
            {
                result.Add(new TorrentStateRecord
                {
                    Id = TorrentId.FromInfoHash(reader.GetString(0)),
                    Name = reader.GetString(1),
                    SavePath = reader.GetString(2),
                    Category = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Tags = reader.IsDBNull(4) ? null : reader.GetString(4),
                    AddedUtc = DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    CompletedUtc = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind),
                });
            }
            return result;
        }, ct);

    private static async Task ExecuteNonQueryAsync(SqliteConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<int> GetSchemaVersionAsync(SqliteConnection conn)
    {
        await using var check = conn.CreateCommand();
        check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_version';";
        var exists = await check.ExecuteScalarAsync().ConfigureAwait(false);
        if (exists is null)
        {
            return 0;
        }

        await using var read = conn.CreateCommand();
        read.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var result = await read.ExecuteScalarAsync().ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    private static async Task MigrateAsync(SqliteConnection conn)
    {
        var version = await GetSchemaVersionAsync(conn).ConfigureAwait(false);
        if (version >= CurrentSchemaVersion)
        {
            return;
        }

        var sql = LoadEmbeddedMigration("001_init.sql");
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static string LoadEmbeddedMigration(string name)
    {
        var resourceName = $"WinBit.Core.Persistence.SqlMigrations.{name}";
        var assembly = typeof(SqliteTorrentStateStore).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration '{resourceName}' not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
