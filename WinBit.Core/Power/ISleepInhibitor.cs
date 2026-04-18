namespace WinBit.Core.Power;

/// <summary>
/// Wraps the OS-level "keep the system awake" primitive. The concrete
/// <see cref="Win32SleepInhibitor"/> maps this onto
/// <c>SetThreadExecutionState</c>; tests supply their own stub to verify the
/// <see cref="PowerManagementService"/> toggling behavior without touching Win32.
/// </summary>
public interface ISleepInhibitor
{
    /// <summary>Whether the inhibitor currently prevents system sleep.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Requests the OS keep the machine awake (<c>true</c>) or releases the previous request
    /// (<c>false</c>). Idempotent — redundant calls are a no-op.
    /// </summary>
    void SetActive(bool active);
}
