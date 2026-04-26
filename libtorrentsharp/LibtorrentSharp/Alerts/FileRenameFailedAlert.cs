// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a <see cref="TorrentHandle.RenameFile"/> request fails.
/// <see cref="FileIndex"/> identifies the file that couldn't be renamed;
/// <see cref="ErrorMessage"/> carries the system error text. Pairs with
/// <see cref="FileRenamedAlert"/>, which reports successful renames.
/// </summary>
public class FileRenameFailedAlert : Alert
{
    internal FileRenameFailedAlert(NativeEvents.FileRenameFailedAlert alert, TorrentHandle subject)
        : base(alert.info)
    {
        Subject = subject;
        FileIndex = alert.file_index;
        ErrorCode = alert.error_code;
        InfoHash = new Sha1Hash(alert.info_hash);

        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>The torrent handle this alert fired on. Resolved by the native dispatcher from the alert's torrent association before the typed alert lands on the consumer side.</summary>
    public TorrentHandle Subject { get; }

    /// <summary>Index of the file whose rename failed.</summary>
    public int FileIndex { get; }

    /// <summary>The numeric system / libtorrent error code.</summary>
    public int ErrorCode { get; }

    /// <summary>The v1 info-hash of the torrent the failed rename belonged to — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>Human-readable error text.</summary>
    public string ErrorMessage { get; }
}
