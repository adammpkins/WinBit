namespace WinBit.Core.Logging;

/// <summary>
/// Ring-buffer of banned-peer records. <c>TorrentSessionService</c> writes an entry whenever
/// the IP filter (or any future rule) rejects a peer. The Peer log UI reads <see cref="Recent"/>
/// on load and subscribes to <see cref="EntryAdded"/> for live updates.
/// </summary>
public interface IPeerLogService
{
    IReadOnlyList<PeerLogEntry> Recent { get; }

    void Record(string peerEndpoint, string reason);

    event EventHandler<PeerLogEntry>? EntryAdded;
}
