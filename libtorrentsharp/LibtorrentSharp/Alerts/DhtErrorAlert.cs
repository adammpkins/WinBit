// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a DHT-subsystem operation fails — bootstrap, lookups, puts,
/// etc. Session-level (no torrent association). <see cref="Operation"/>
/// identifies which DHT op failed (typed <see cref="OperationType"/>).
/// </summary>
public class DhtErrorAlert : Alert
{
    internal DhtErrorAlert(NativeEvents.DhtErrorAlert alert)
        : base(alert.info)
    {
        Operation = (OperationType)alert.operation;
        ErrorCode = alert.error_code;

        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>The DHT operation that failed.</summary>
    public OperationType Operation { get; }

    /// <summary>The numeric system / libtorrent error code.</summary>
    public int ErrorCode { get; }

    /// <summary>Human-readable error text.</summary>
    public string ErrorMessage { get; }
}
