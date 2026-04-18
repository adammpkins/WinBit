using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using WinBit.Core.Logging;

namespace WinBit.ViewModels.Logs;

/// <summary>
/// A single row in the Execution Log list. Values are derived from an <see cref="LogEntry"/> once
/// at construction; rows are never mutated after being added to the list.
/// </summary>
public sealed class LogRowViewModel : ObservableObject
{
    public LogRowViewModel(LogEntry entry)
    {
        Entry = entry;
        TimestampText = entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture);
        SeverityText = entry.Severity.ToString();
        Message = entry.Message;
    }

    public LogEntry Entry { get; }

    public string TimestampText { get; }

    public string SeverityText { get; }

    public string Message { get; }

    public bool IsWarning => Entry.Severity == LogSeverity.Warning;

    public bool IsCritical => Entry.Severity == LogSeverity.Critical;
}
