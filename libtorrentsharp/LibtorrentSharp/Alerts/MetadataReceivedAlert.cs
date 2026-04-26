// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a magnet-added torrent finishes fetching its metadata (info
/// dict) from the swarm — the transition from "downloading metadata" to
/// "actual torrent" is complete. Surfaces the v1 <see cref="InfoHash"/>
/// directly rather than a <c>Subject</c> reference, since the alert's
/// handle is typically a magnet-side handle that isn't tracked in the
/// attached-manager map.
/// </summary>
public class MetadataReceivedAlert : Alert
{
    internal MetadataReceivedAlert(Native.NativeEvents.MetadataReceivedAlert alert)
        : base(alert.info)
    {
        InfoHash = new Sha1Hash(alert.info_hash);
    }

    /// <summary>The v1 SHA-1 info-hash of the torrent whose metadata just arrived.</summary>
    public Sha1Hash InfoHash { get; }
}
