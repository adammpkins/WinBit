// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a torrent enters a sticky error state — typically a disk I/O
/// error libtorrent can't recover from on its own. The torrent is paused
/// and needs manual intervention (clear_error + resume) to restart.
/// <see cref="Filename"/> may be empty when the error isn't file-specific.
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source
/// torrent (MagnetHandle) that hit a sticky error after metadata arrival —
/// magnet handles aren't tracked in the session's TorrentHandle map, so
/// the dispatcher can't resolve a managed TorrentHandle to attribute the
/// error to. Callers tracking magnet error states should use
/// <see cref="InfoHash"/> as the routing key.
/// </para>
/// </summary>
public class TorrentErrorAlert : Alert
{
    internal TorrentErrorAlert(NativeEvents.TorrentErrorAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        ErrorCode = alert.error_code;
        InfoHash = new Sha1Hash(alert.info_hash);

        Filename = alert.filename == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.filename) ?? string.Empty;
        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>The torrent that entered the error state. May be null for magnet-source errors — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>The numeric system / libtorrent error code.</summary>
    public int ErrorCode { get; }

    /// <summary>The v1 info-hash of the torrent that entered the error state — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>Path of the file that triggered the error, or empty if not file-specific.</summary>
    public string Filename { get; }

    /// <summary>Human-readable error text.</summary>
    public string ErrorMessage { get; }
}
