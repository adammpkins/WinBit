namespace WinBit.Core.Logging;

public sealed record LogEntry(long Id, DateTime TimestampUtc, LogSeverity Severity, string Message);
