// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired after every <c>add_torrent</c> / <c>async_add_torrent</c> call
/// with the outcome of the add operation. <see cref="ErrorCode"/> is zero
/// on success and non-zero on failure (duplicate torrent, malformed
/// resume data, etc.) — callers observing this alert can surface add
/// failures without polling. Success-side dispatch also resolves
/// <see cref="Subject"/> via the session's attached-manager map; when the
/// add failed <see cref="Subject"/> is null because the handle is invalid.
/// </summary>
public class AddTorrentAlert : Alert
{
    internal AddTorrentAlert(NativeEvents.AddTorrentAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        InfoHash = new Sha1Hash(alert.info_hash);
        ErrorCode = alert.error_code;
        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>
    /// The managed handle for the added torrent, or null when the add
    /// failed or the torrent wasn't registered in the attached-manager
    /// map (e.g. magnet-only handles).
    /// </summary>
    public TorrentHandle? Subject { get; }

    /// <summary>
    /// The v1 info-hash of the torrent that was added. Zero-filled when
    /// the add failed before libtorrent could construct a valid handle.
    /// </summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>Libtorrent's error code — zero on success.</summary>
    public int ErrorCode { get; }

    /// <summary>Human-readable error text; empty on success.</summary>
    public string ErrorMessage { get; }

    /// <summary>True when the add completed successfully.</summary>
    public bool IsSuccess => ErrorCode == 0;
}
