using System.Collections.Concurrent;
using WinBit.Core.Common;

namespace WinBit.Core.BitTorrent;

public enum TorrentCreatorTaskState
{
    Queued,
    Running,
    Finished,
    Failed,
}

public sealed record TorrentCreatorTaskStatus
{
    public required string TaskId { get; init; }
    public required TorrentCreatorTaskState State { get; init; }
    public required TorrentCreateRequest Request { get; init; }
    public double Progress { get; init; }
    public DateTime TimeAddedUtc { get; init; }
    public DateTime? TimeStartedUtc { get; init; }
    public DateTime? TimeFinishedUtc { get; init; }
    public string? Error { get; init; }
    public string? OutputPath { get; init; }
}

public interface ITorrentCreatorQueue
{
    string AddTask(TorrentCreateRequest request);
    IReadOnlyList<TorrentCreatorTaskStatus> GetStatus();
    TorrentCreatorTaskStatus? GetStatus(string taskId);
    byte[]? GetResult(string taskId);
    bool DeleteTask(string taskId);

    /// <summary>Test helper — resolves when the task reaches a terminal state.</summary>
    Task<TorrentCreatorTaskStatus?> WaitForTaskAsync(string taskId, CancellationToken ct = default);
}

public sealed class TorrentCreatorQueue : ITorrentCreatorQueue, IAsyncDisposable
{
    private readonly ITorrentCreatorService _creator;
    private readonly ConcurrentDictionary<string, TaskRecord> _tasks = new(StringComparer.Ordinal);

    public TorrentCreatorQueue(ITorrentCreatorService creator) => _creator = creator;

    public string AddTask(TorrentCreateRequest request)
    {
        var effective = request;
        if (string.IsNullOrWhiteSpace(effective.OutputPath))
        {
            effective = effective with
            {
                OutputPath = Path.Combine(Path.GetTempPath(),
                    $"winbit-tc-{Guid.NewGuid():N}.torrent"),
            };
        }

        var taskId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var record = new TaskRecord
        {
            TaskId = taskId,
            Request = effective,
            TimeAddedUtc = now,
        };

        _tasks[taskId] = record;

        record.Completion = Task.Run(async () =>
        {
            record.State = TorrentCreatorTaskState.Running;
            record.TimeStartedUtc = DateTime.UtcNow;

            var progress = new Progress<TorrentCreateProgress>(p => record.Progress = p.OverallCompletion);
            var result = await _creator.CreateAsync(effective, progress, CancellationToken.None)
                .ConfigureAwait(false);

            record.TimeFinishedUtc = DateTime.UtcNow;
            if (result.IsSuccess)
            {
                record.State = TorrentCreatorTaskState.Finished;
                record.Progress = 1.0;
            }
            else
            {
                record.State = TorrentCreatorTaskState.Failed;
                record.Error = result.Error;
            }
        });

        return taskId;
    }

    public IReadOnlyList<TorrentCreatorTaskStatus> GetStatus() =>
        _tasks.Values.Select(Project).OrderBy(s => s.TimeAddedUtc).ToArray();

    public TorrentCreatorTaskStatus? GetStatus(string taskId) =>
        _tasks.TryGetValue(taskId, out var r) ? Project(r) : null;

    public byte[]? GetResult(string taskId)
    {
        if (!_tasks.TryGetValue(taskId, out var r))
        {
            return null;
        }
        if (r.State != TorrentCreatorTaskState.Finished ||
            string.IsNullOrEmpty(r.Request.OutputPath) ||
            !File.Exists(r.Request.OutputPath))
        {
            return null;
        }
        return File.ReadAllBytes(r.Request.OutputPath);
    }

    public bool DeleteTask(string taskId)
    {
        if (!_tasks.TryRemove(taskId, out var r))
        {
            return false;
        }
        if (!string.IsNullOrEmpty(r.Request.OutputPath) && File.Exists(r.Request.OutputPath))
        {
            try { File.Delete(r.Request.OutputPath); } catch { /* best-effort */ }
        }
        return true;
    }

    public async Task<TorrentCreatorTaskStatus?> WaitForTaskAsync(string taskId, CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var r))
        {
            return null;
        }
        if (r.Completion is Task t)
        {
            await t.WaitAsync(ct).ConfigureAwait(false);
        }
        return Project(r);
    }

    public async ValueTask DisposeAsync()
    {
        var pending = _tasks.Values.Select(r => r.Completion).OfType<Task>().ToArray();
        if (pending.Length > 0)
        {
            try { await Task.WhenAll(pending).ConfigureAwait(false); } catch { /* swallow */ }
        }
    }

    private static TorrentCreatorTaskStatus Project(TaskRecord r) => new()
    {
        TaskId = r.TaskId,
        State = r.State,
        Request = r.Request,
        Progress = r.Progress,
        TimeAddedUtc = r.TimeAddedUtc,
        TimeStartedUtc = r.TimeStartedUtc,
        TimeFinishedUtc = r.TimeFinishedUtc,
        Error = r.Error,
        OutputPath = r.Request.OutputPath,
    };

    private sealed class TaskRecord
    {
        public required string TaskId { get; init; }
        public required TorrentCreateRequest Request { get; init; }
        public required DateTime TimeAddedUtc { get; init; }
        public DateTime? TimeStartedUtc { get; set; }
        public DateTime? TimeFinishedUtc { get; set; }
        public TorrentCreatorTaskState State { get; set; } = TorrentCreatorTaskState.Queued;
        public double Progress { get; set; }
        public string? Error { get; set; }
        public Task? Completion { get; set; }
    }
}
