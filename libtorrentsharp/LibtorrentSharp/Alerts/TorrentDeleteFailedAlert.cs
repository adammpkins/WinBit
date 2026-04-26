// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Runtime.InteropServices;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when deletion of a torrent's on-disk files fails during a
/// <see cref="LibtorrentSession.DetachTorrent(TorrentHandle, Enums.RemoveFlags)"/>
/// with <see cref="Enums.RemoveFlags.DeleteFiles"/>. Per-torrent, not per-file
/// — libtorrent aggregates any per-file errors into one alert carrying the
/// first error. Like <see cref="TorrentDeletedAlert"/>, this surfaces the
/// v1 <see cref="InfoHash"/> directly (the handle is invalid by this point).
/// </summary>
public class TorrentDeleteFailedAlert : Alert
{
    internal TorrentDeleteFailedAlert(Native.NativeEvents.TorrentDeleteFailedAlert alert)
        : base(alert.info)
    {
        InfoHash = new Sha1Hash(alert.info_hash);
        ErrorCode = alert.error_code;

        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>The v1 SHA-1 info-hash of the torrent whose deletion failed.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>The numeric system / libtorrent error code.</summary>
    public int ErrorCode { get; }

    /// <summary>Human-readable error text.</summary>
    public string ErrorMessage { get; }
}
