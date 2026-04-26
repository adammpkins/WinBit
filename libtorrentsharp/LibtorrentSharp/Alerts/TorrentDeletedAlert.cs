// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a torrent's on-disk files have been successfully deleted —
/// follows a <see cref="LibtorrentSession.DetachTorrent(TorrentHandle, Enums.RemoveFlags)"/>
/// with <see cref="Enums.RemoveFlags.DeleteFiles"/> set. Fires from libtorrent's
/// disk thread after the deletion completes; by this point the torrent_handle
/// is invalid, so this alert surfaces the v1 <see cref="InfoHash"/> directly
/// rather than a <c>Subject</c> reference.
/// </summary>
public class TorrentDeletedAlert : Alert
{
    internal TorrentDeletedAlert(Native.NativeEvents.TorrentDeletedAlert alert)
        : base(alert.info)
    {
        InfoHash = new Sha1Hash(alert.info_hash);
    }

    /// <summary>The v1 SHA-1 info-hash of the deleted torrent.</summary>
    public Sha1Hash InfoHash { get; }
}
