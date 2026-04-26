// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when an I2P-router operation fails — typically the SAM bridge
/// rejecting a session, the I2P daemon being unreachable, or
/// destination resolution failing. Session-level (no torrent
/// association — the I2P transport itself is the failing resource).
/// <para>
/// Smaller field set than <see cref="Socks5Alert"/> because libtorrent's
/// <c>i2p_alert</c> exposes only the error_code — I2P destinations are
/// opaque hashes (not IP endpoints), and the failing operation is
/// implicit in the error code itself.
/// </para>
/// <para>
/// Useful for I2P-routing consumers diagnosing why anonymous traffic
/// isn't getting through to the I2P network.
/// </para>
/// </summary>
public class I2pAlert : Alert
{
    internal I2pAlert(NativeEvents.I2pAlert alert)
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
