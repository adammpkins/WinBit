using System.Runtime.InteropServices;
using WinBit.Core.Logging;

namespace WinBit.Core.Power;

/// <summary>
/// Uses <c>SetThreadExecutionState</c> on the calling thread to block system sleep while active
/// transfers are in progress. ES_CONTINUOUS + ES_SYSTEM_REQUIRED prevents the idle sleep timer
/// from counting down; dropping back to ES_CONTINUOUS alone releases the block. The display is
/// deliberately *not* kept awake — users expect the monitor to sleep independently.
/// </summary>
public sealed class Win32SleepInhibitor : ISleepInhibitor
{
    [Flags]
    private enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

    private readonly ILogService _log;

    public Win32SleepInhibitor(ILogService log) => _log = log;

    public bool IsActive { get; private set; }

    public void SetActive(bool active)
    {
        if (active == IsActive)
        {
            return;
        }

        var flags = active
            ? ExecutionState.Continuous | ExecutionState.SystemRequired
            : ExecutionState.Continuous;
        var result = SetThreadExecutionState(flags);
        if (result == 0)
        {
            _log.Write($"SetThreadExecutionState failed for active={active}.", LogSeverity.Warning);
            return;
        }

        IsActive = active;
    }
}
