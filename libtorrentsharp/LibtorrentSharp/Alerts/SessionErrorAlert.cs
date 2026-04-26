// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a session-level operation fails catastrophically — typically
/// signals the session is in an unusable state and should be restarted.
/// Session-level (no torrent association).
/// </summary>
public class SessionErrorAlert : Alert
{
    internal SessionErrorAlert(NativeEvents.SessionErrorAlert alert)
        : base(alert.info)
    {
        ErrorCode = alert.error_code;

        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>The numeric system / libtorrent error code.</summary>
    public int ErrorCode { get; }

    /// <summary>Human-readable error text.</summary>
    public string ErrorMessage { get; }
}
