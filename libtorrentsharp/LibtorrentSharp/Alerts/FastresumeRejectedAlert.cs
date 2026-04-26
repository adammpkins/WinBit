// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when libtorrent rejects resume data passed to <see cref="LibtorrentSession.Add"/>
/// — malformed bencoding, info-hash mismatch, or a deeper semantic failure
/// during apply. The torrent itself may still attach using the fallback
/// source; this alert just flags that the resume portion was discarded and
/// the torrent will recheck from scratch. <see cref="ErrorMessage"/> carries
/// the system error text.
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source /
/// resume-source torrent (MagnetHandle) — magnet handles aren't tracked
/// in the session's TorrentHandle map, so the dispatcher can't resolve a
/// managed TorrentHandle to attribute the rejection to. Callers handling
/// fastresume rejections for magnet sources should use <see cref="InfoHash"/>
/// as the routing key and treat null Subject as "magnet/resume-source —
/// correlate by InfoHash".
/// </para>
/// </summary>
public class FastresumeRejectedAlert : Alert
{
    internal FastresumeRejectedAlert(NativeEvents.FastresumeRejectedAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        ErrorCode = alert.error_code;
        InfoHash = new Sha1Hash(alert.info_hash);

        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>The torrent whose resume data was rejected. May be null for magnet/resume-source rejections — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>The numeric system / libtorrent error code.</summary>
    public int ErrorCode { get; }

    /// <summary>The v1 info-hash of the torrent whose resume data was rejected — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>Human-readable error text.</summary>
    public string ErrorMessage { get; }
}
