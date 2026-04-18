namespace WinBit.Core.WebUi;

/// <summary>
/// In-process Kestrel host for the M10 Web UI. Controllers land in separate deliverables;
/// this interface carries the diagnostic surface needed right now: whether the host is up
/// and which port it actually bound to (important when
/// <c>AppSettings.WebUi.Port</c> is 0 / ephemeral, as in tests).
/// </summary>
public interface IWebUiService
{
    bool IsRunning { get; }

    int? BoundPort { get; }
}
