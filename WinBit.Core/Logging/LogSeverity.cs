namespace WinBit.Core.Logging;

[Flags]
public enum LogSeverity
{
    None = 0,
    Normal = 1 << 0,
    Info = 1 << 1,
    Warning = 1 << 2,
    Critical = 1 << 3,
    All = Normal | Info | Warning | Critical,
}
