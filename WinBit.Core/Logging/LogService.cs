using System.Collections.Concurrent;

namespace WinBit.Core.Logging;

/// <summary>
/// Lock-free ring buffer holding the most recent <see cref="Capacity"/> entries.
/// Readers never block writers.
/// </summary>
public sealed class LogService : ILogService
{
    public const int Capacity = 20_000;

    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private long _nextId;

    public event EventHandler<LogEntry>? MessageLogged;

    public void Write(string message, LogSeverity severity = LogSeverity.Normal)
    {
        var entry = new LogEntry(Interlocked.Increment(ref _nextId), DateTime.UtcNow, severity, message);
        _entries.Enqueue(entry);
        while (_entries.Count > Capacity && _entries.TryDequeue(out _))
        {
        }

        // Mirror to the debugger's Output window so devs can tail the log without an
        // in-app Logs page (that UI lands in M7).
        System.Diagnostics.Debug.WriteLine($"[WinBit {severity}] {message}");

        MessageLogged?.Invoke(this, entry);
    }

    public IReadOnlyList<LogEntry> GetMessages(long afterId = -1, LogSeverity filter = LogSeverity.All)
    {
        var snapshot = _entries.ToArray();
        var result = new List<LogEntry>(snapshot.Length);
        foreach (var entry in snapshot)
        {
            if (entry.Id <= afterId)
            {
                continue;
            }

            if ((filter & entry.Severity) == 0)
            {
                continue;
            }

            result.Add(entry);
        }

        return result;
    }
}
