using System.Text;
using Microsoft.Data.Sqlite;

namespace WinBit.Core.Persistence;

/// <summary>
/// SQLite-backed torrent state store. Opens the database in WAL mode, runs embedded
/// migrations, and serializes writes through a single connection + semaphore. Readers open
/// fresh pooled connections and proceed in parallel under WAL. Subsequent milestones layer
/// typed upsert/query helpers on top of <see cref="ExecuteWriteAsync"/> /
/// <see cref="ExecuteReadAsync{T}"/>.
/// </summary>
public sealed class SqliteTorrentStateStore : IAsyncDisposable
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
