using Microsoft.Data.Sqlite;
using WinBit.Core.Persistence;

namespace WinBit.Core.Rss;

public sealed class SqliteRssReadStore : IRssReadStore
{
    private readonly SqliteTorrentStateStore _db;

    public SqliteRssReadStore(SqliteTorrentStateStore db) => _db = db;

    public Task<IReadOnlyList<(string FeedUrl, string ArticleId)>> LoadAllAsync(CancellationToken ct = default) =>
        _db.ExecuteReadAsync<IReadOnlyList<(string, string)>>(async (conn, c) =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT feed_url, article_id FROM rss_read;";
            await using var reader = await cmd.ExecuteReaderAsync(c).ConfigureAwait(false);
            var list = new List<(string, string)>();
            while (await reader.ReadAsync(c).ConfigureAwait(false))
            {
                list.Add((reader.GetString(0), reader.GetString(1)));
            }
            return list;
        }, ct);

    public Task MarkAsync(string feedUrl, string articleId, CancellationToken ct = default) =>
        _db.ExecuteWriteAsync(async (conn, c) =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO rss_read (feed_url, article_id) VALUES ($feed, $id);";
            cmd.Parameters.Add(new SqliteParameter("$feed", feedUrl));
            cmd.Parameters.Add(new SqliteParameter("$id", articleId));
            await cmd.ExecuteNonQueryAsync(c).ConfigureAwait(false);
        }, ct);

    public Task MarkManyAsync(string feedUrl, IReadOnlyCollection<string> articleIds, CancellationToken ct = default)
    {
        if (articleIds.Count == 0)
        {
            return Task.CompletedTask;
        }
        return _db.ExecuteWriteAsync(async (conn, c) =>
        {
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(c).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR IGNORE INTO rss_read (feed_url, article_id) VALUES ($feed, $id);";
            var feedParam = cmd.Parameters.Add(new SqliteParameter("$feed", feedUrl));
            var idParam = cmd.Parameters.Add(new SqliteParameter("$id", ""));
            foreach (var id in articleIds)
            {
                idParam.Value = id;
                await cmd.ExecuteNonQueryAsync(c).ConfigureAwait(false);
            }
            await tx.CommitAsync(c).ConfigureAwait(false);
        }, ct);
    }
}
