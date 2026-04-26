// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a v1+v2 hybrid torrent downloads metadata that collides
/// with an already-running torrent in the session. Both torrents enter a
/// <c>duplicate_torrent</c> error state. The canonical resolution is to
/// remove both torrents and re-add via a clean source — the metadata
/// <c>shared_ptr</c> carried by the underlying libtorrent alert is
/// deliberately not surfaced through the binding; callers that want to
/// inspect the conflicting metadata can do so via
/// <see cref="LibtorrentSession"/>'s handle map.
/// </summary>
public class TorrentConflictAlert : Alert
{
    internal TorrentConflictAlert(
        NativeEvents.TorrentConflictAlert alert,
        TorrentHandle? subject,
        TorrentHandle? conflictingSubject)
        : base(alert.info)
    {
        Subject = subject;
        InfoHash = new Sha1Hash(alert.info_hash);

        ConflictingSubject = conflictingSubject;
        ConflictingInfoHash = new Sha1Hash(alert.conflicting_info_hash);
    }

    /// <summary>
    /// The torrent that downloaded the offending metadata. May be null if
    /// the handle isn't in the session's attached-manager map.
    /// </summary>
    public TorrentHandle? Subject { get; }

    /// <summary>Info-hash of the torrent that downloaded the metadata.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>
    /// The torrent the metadata collided with. May be null for the same
    /// reason as <see cref="Subject"/>.
    /// </summary>
    public TorrentHandle? ConflictingSubject { get; }

    /// <summary>Info-hash of the torrent the metadata collided with.</summary>
    public Sha1Hash ConflictingInfoHash { get; }
}
