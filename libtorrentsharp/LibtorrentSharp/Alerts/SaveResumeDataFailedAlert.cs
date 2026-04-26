// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a <see cref="TorrentHandle.RequestResumeData"/> /
/// <see cref="LibtorrentSession.RequestResumeData(MagnetHandle)"/> request
/// fails — typically because the handle is invalid (torrent was removed
/// during the operation) or the torrent has no metadata yet (magnet that
/// hasn't fetched its info dict). Pairs with <see cref="ResumeDataReadyAlert"/>,
/// which reports successful resume-data capture. <see cref="ErrorMessage"/>
/// carries the system error text.
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source
/// torrent (MagnetHandle) — magnet handles aren't tracked in the
/// session's TorrentHandle map, so the dispatcher can't resolve a managed
/// TorrentHandle to attribute the failure to. Callers handling magnet
/// resume failures should use <see cref="InfoHash"/> as the routing key
/// and treat null Subject as "magnet-source — correlate by InfoHash".
/// </para>
/// </summary>
public class SaveResumeDataFailedAlert : Alert
{
    internal SaveResumeDataFailedAlert(NativeEvents.SaveResumeDataFailedAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        ErrorCode = alert.error_code;
        InfoHash = new Sha1Hash(alert.info_hash);

        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>The torrent whose resume-data request failed. May be null for magnet-source failures — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>The numeric system / libtorrent error code.</summary>
    public int ErrorCode { get; }

    /// <summary>The v1 info-hash of the torrent whose resume-data request failed — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>Human-readable error text.</summary>
    public string ErrorMessage { get; }
}
