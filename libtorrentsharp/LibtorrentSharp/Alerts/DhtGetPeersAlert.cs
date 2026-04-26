// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when another peer sends a <c>get_peers</c> query to our DHT node.
/// Session-level — the info-hash is what the remote peer is asking about
/// and does not have to correspond to one of our own torrents.
/// </summary>
public class DhtGetPeersAlert : Alert
{
    internal DhtGetPeersAlert(NativeEvents.DhtGetPeersAlert alert)
        : base(alert.info)
    {
        InfoHash = new Sha1Hash(alert.info_hash);
    }

    /// <summary>The info-hash the remote peer is requesting peers for.</summary>
    public Sha1Hash InfoHash { get; }
}
