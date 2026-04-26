// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired once when the initial DHT bootstrap finishes — the session's DHT
/// node is now considered capable of serving lookups. Session-level
/// (no torrent association). Useful as a readiness signal before issuing
/// <see cref="LibtorrentSession.DhtPutImmutable"/> /
/// <see cref="LibtorrentSession.DhtGetImmutable"/> or before relying on DHT
/// peer discovery for magnet-only swarms.
/// </summary>
public class DhtBootstrapAlert : Alert
{
    internal DhtBootstrapAlert(NativeEvents.DhtBootstrapAlert alert)
        : base(alert.info)
    {
    }
}
