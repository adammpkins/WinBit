namespace WinBit.Core.Logging;

/// <summary>
/// Application-wide ring-buffer log. Feeds the Execution Log page and is useful from day one.
/// </summary>
public interface ILogService
{
    IReadOnlyList<LogEntry> GetMessages(long afterId = -1, LogSeverity filter = LogSeverity.All);
    void Write(string message, LogSeverity severity = LogSeverity.Normal);
    event EventHandler<LogEntry>? MessageLogged;
}
