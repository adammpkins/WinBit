// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Verbose session-level log line emitted by libtorrent — things like
/// "DHT bootstrap complete", "uTP socket warning: ...", "starting
/// session listen on port X". Sibling to <see cref="TorrentLogAlert"/>
/// but session-scoped (no torrent association — these messages aren't
/// tied to a specific torrent or connection).
/// <para>
/// Useful for diagnosing session-wide issues (DHT bootstrap failures,
/// listen socket problems, session-config warnings) when the structured
/// session-level alerts don't carry enough context.
/// </para>
/// <para>
/// <b>Requires opt-in:</b> consumers must include
/// <see cref="LibtorrentSharp.Enums.AlertCategories.SessionLog"/> in
/// <see cref="LibtorrentSessionConfig.AlertCategories"/>. The default
/// <c>RequiredAlertCategories</c> mask intentionally omits SessionLog
/// because the alerts in that category are high-volume and
/// debug-tier — typically only useful when actively debugging a
/// session-wide problem.
/// </para>
/// </summary>
public class LogAlert : Alert
{
    internal LogAlert(NativeEvents.LogAlert alert)
        : base(alert.info)
    {
        LogMessage = alert.log_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.log_message) ?? string.Empty;
    }

    /// <summary>The session-level log message text emitted by libtorrent's internal logging.</summary>
    public string LogMessage { get; }
}
