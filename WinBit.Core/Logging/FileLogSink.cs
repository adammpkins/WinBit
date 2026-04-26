using System.IO;
using System.Text;
using Microsoft.Extensions.Hosting;
using WinBit.Core.Persistence;

namespace WinBit.Core.Logging;

/// <summary>
/// Tails <see cref="ILogService.MessageLogged"/> into per-day files under
/// <see cref="Paths.LogsDir"/>. Matches the legacy <c>winbit-YYYY-MM-DD.log</c> format:
/// a <c>=== WinBit started TIMESTAMP (pid N) ===</c> header written once at startup,
/// then <c>TIMESTAMP [Severity] message</c> lines for each ring-buffer entry.
/// </summary>
/// <remarks>
/// Flush-per-write + <see cref="FileShare.Read"/> means devs can tail the file while
/// the app runs. Dedup on <see cref="LogEntry.Id"/> keeps startup-race cases
/// (ring-buffer drain intersecting live events) from producing duplicate lines.
/// </remarks>
internal sealed class FileLogSink : IHostedService, IDisposable
{
    private readonly ILogService _log;
    private readonly Paths _paths;
    private readonly object _gate = new();

    private StreamWriter? _writer;
    private DateOnly _openedFor;
    private long _lastWrittenId = -1;

    public FileLogSink(ILogService log, Paths paths)
    {
        _log = log;
        _paths = paths;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.LogsDir);
        OpenForToday(writeHeader: true);

        // Drain anything already in the ring buffer (entries written before the
        // hosted service started up). Subscribing after the drain means a Write
        // racing us might cause a duplicate — the id-based dedup in WriteEntry
        // handles it.
        foreach (var entry in _log.GetMessages())
        {
            WriteEntry(entry);
        }

        _log.MessageLogged += OnMessageLogged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _log.MessageLogged -= OnMessageLogged;
        lock (_gate)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void OnMessageLogged(object? sender, LogEntry entry) => WriteEntry(entry);

    private void WriteEntry(LogEntry entry)
    {
        var localTs = DateTime.SpecifyKind(entry.TimestampUtc, DateTimeKind.Utc).ToLocalTime();
        var formatted = new DateTimeOffset(localTs);
        var dateToday = DateOnly.FromDateTime(localTs);

        lock (_gate)
        {
            if (_writer is null || entry.Id <= _lastWrittenId)
            {
                return;
            }

            if (dateToday != _openedFor)
            {
                OpenForToday(writeHeader: false);
            }

            _writer!.WriteLine($"{formatted:O} [{entry.Severity}] {entry.Message}");
            _writer.Flush();
            _lastWrittenId = entry.Id;
        }
    }

    private void OpenForToday(bool writeHeader)
    {
        var now = DateTimeOffset.Now;
        _openedFor = DateOnly.FromDateTime(now.DateTime);
        var path = Path.Combine(_paths.LogsDir, $"winbit-{_openedFor:yyyy-MM-dd}.log");

        _writer?.Flush();
        _writer?.Dispose();
        _writer = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (writeHeader)
        {
            _writer.WriteLine($"=== WinBit started {now:O} (pid {Environment.ProcessId}) ===");
            _writer.Flush();
        }
    }
}
