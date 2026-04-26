// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a specific file I/O operation fails — read, write, open, etc.
/// Transient: libtorrent may retry. Compare with <see cref="TorrentErrorAlert"/>,
/// which fires when an error is sticky enough to pause the whole torrent.
/// <see cref="Operation"/> identifies which I/O step failed (typed
/// <see cref="OperationType"/>; see libtorrent's <c>operation_t</c> for the
/// authoritative source of the mapping).
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source
/// torrent (MagnetHandle) that hit a transient disk error after metadata
/// arrival — magnet handles aren't tracked in the session's TorrentHandle
/// map, so the dispatcher can't resolve a managed TorrentHandle to
/// attribute the failure to. Callers tracking magnet I/O failures should
/// use <see cref="InfoHash"/> as the routing key.
/// </para>
/// </summary>
public class FileErrorAlert : Alert
{
    internal FileErrorAlert(NativeEvents.FileErrorAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        ErrorCode = alert.error_code;
        Operation = (OperationType)alert.op;
        InfoHash = new Sha1Hash(alert.info_hash);

        Filename = alert.filename == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.filename) ?? string.Empty;
        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>The torrent the failed I/O belonged to. May be null for magnet-source failures — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>The numeric system / libtorrent error code.</summary>
    public int ErrorCode { get; }

    /// <summary>The I/O operation that failed.</summary>
    public OperationType Operation { get; }

    /// <summary>The v1 info-hash of the torrent the failed I/O belonged to — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>Path of the file the failed I/O targeted.</summary>
    public string Filename { get; }

    /// <summary>Human-readable error text.</summary>
    public string ErrorMessage { get; }
}
