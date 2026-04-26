// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a <see cref="TorrentHandle.RenameFile"/> or
/// <see cref="MagnetHandle.RenameFile"/> request completes successfully.
/// <see cref="FileIndex"/> identifies which file was renamed;
/// <see cref="NewName"/> is the resolved path libtorrent stored (which may
/// differ from the requested name after normalization). Pairs with
/// <see cref="FileRenameFailedAlert"/>, which reports failures.
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source
/// torrent (MagnetHandle) that received metadata and was then renamed —
/// magnet handles aren't tracked in the session's TorrentHandle map, so
/// the dispatcher can't resolve a managed TorrentHandle to attribute the
/// rename to. Note that <c>MagnetHandle.RenameFile</c> only fires this
/// alert after metadata arrival (libtorrent has no per-file knowledge
/// pre-metadata, so the rename is silently no-oped). Callers tracking
/// magnet renames should use <see cref="InfoHash"/> as the routing key.
/// </para>
/// </summary>
public class FileRenamedAlert : Alert
{
    internal FileRenamedAlert(NativeEvents.FileRenamedAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        FileIndex = alert.file_index;
        InfoHash = new Sha1Hash(alert.info_hash);

        NewName = alert.new_name == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.new_name) ?? string.Empty;
    }

    /// <summary>The torrent whose file was renamed. May be null for magnet-source renames — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>Index of the file that was renamed (same value passed to <see cref="TorrentHandle.RenameFile"/>).</summary>
    public int FileIndex { get; }

    /// <summary>The v1 info-hash of the torrent the renamed file belongs to — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>The resolved new path libtorrent stored for the file.</summary>
    public string NewName { get; }
}
