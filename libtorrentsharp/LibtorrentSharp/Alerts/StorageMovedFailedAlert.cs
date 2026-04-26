// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a <see cref="TorrentHandle.MoveStorage"/> request fails.
/// <see cref="FilePath"/> names the file the move couldn't complete;
/// <see cref="ErrorMessage"/> is the system error text so callers can
/// surface a diagnostic without round-tripping <see cref="ErrorCode"/>
/// through their own error registry.
/// </summary>
public class StorageMovedFailedAlert : Alert
{
    internal StorageMovedFailedAlert(NativeEvents.StorageMovedFailedAlert alert, TorrentHandle subject)
        : base(alert.info)
    {
        Subject = subject;
        ErrorCode = alert.error_code;
        InfoHash = new Sha1Hash(alert.info_hash);

        FilePath = alert.file_path == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.file_path) ?? string.Empty;
        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>The torrent handle this alert fired on. Resolved by the native dispatcher from the alert's torrent association before the typed alert lands on the consumer side.</summary>
    public TorrentHandle Subject { get; }

    /// <summary>The numeric system error code returned by the OS.</summary>
    public int ErrorCode { get; }

    /// <summary>The v1 info-hash of the torrent the failed move belonged to — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>The file the move could not complete (libtorrent-normalized).</summary>
    public string FilePath { get; }

    /// <summary>Human-readable error text from the OS.</summary>
    public string ErrorMessage { get; }
}
