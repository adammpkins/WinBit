// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a <see cref="TorrentHandle.MoveStorage"/> or
/// <see cref="MagnetHandle.MoveStorage"/> request completes
/// successfully. <see cref="Subject"/> is the handle that was moved;
/// <see cref="StoragePath"/> is the new save path and <see cref="OldPath"/>
/// is where the data used to live (both libtorrent-normalized).
/// <see cref="InfoHash"/> is the v1 info-hash — the same identifier the
/// native dispatcher used to route the alert.
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source
/// torrent (MagnetHandle) whose save path was relocated — magnet handles
/// aren't tracked in the session's TorrentHandle map, so the dispatcher
/// can't resolve a managed TorrentHandle to attribute the move to.
/// libtorrent fires this alert for magnets even before metadata arrival
/// (the move is a save-path-only update at that point — there are no
/// files to physically relocate yet). Callers tracking magnet moves
/// should use <see cref="InfoHash"/> as the routing key.
/// </para>
/// </summary>
public class StorageMovedAlert : Alert
{
    internal StorageMovedAlert(NativeEvents.StorageMovedAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        InfoHash = new Sha1Hash(alert.info_hash);

        // Both string fields are dispatcher-owned; the constructor copies into
        // managed memory before the callback returns.
        StoragePath = alert.storage_path == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.storage_path) ?? string.Empty;
        OldPath = alert.old_path == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.old_path) ?? string.Empty;
    }

    /// <summary>The torrent that was moved. May be null for magnet-source moves — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>The v1 info-hash of the moved torrent — surfaces the same identifier the native dispatcher used to route the alert, useful for callers tracking magnet-source moves where Subject is null.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>The new save path the torrent now lives at.</summary>
    public string StoragePath { get; }

    /// <summary>The save path the torrent was moved from.</summary>
    public string OldPath { get; }
}
