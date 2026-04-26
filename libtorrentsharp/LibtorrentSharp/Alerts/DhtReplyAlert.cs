// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a DHT node returns peers in response to a torrent's info_hash
/// lookup. <see cref="NumPeers"/> reflects only the count contained in this
/// specific packet — a single lookup typically produces multiple alerts as
/// responses trickle in from multiple nodes, so callers that want a session
/// total should accumulate across the torrent's lifetime.
/// </summary>
public class DhtReplyAlert : Alert
{
    internal DhtReplyAlert(NativeEvents.DhtReplyAlert alert, TorrentHandle subject)
        : base(alert.info)
    {
        Subject = subject;
        NumPeers = alert.num_peers;
    }

    /// <summary>The torrent whose DHT lookup yielded peers.</summary>
    public TorrentHandle Subject { get; }

    /// <summary>Count of peers contained in this single DHT response packet.</summary>
    public int NumPeers { get; }
}
