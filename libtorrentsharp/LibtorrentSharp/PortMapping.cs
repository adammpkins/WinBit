using LibtorrentSharp.Enums;

namespace LibtorrentSharp;

/// <summary>
/// Snapshot of a port-forwarding mapping the session has registered.
/// libtorrent 2.x exposes these only via alerts, so the binding accumulates
/// state across the session's lifetime; a mapping stays in the snapshot
/// (with <see cref="HasError"/> set) until the session is disposed.
/// </summary>
/// <param name="Mapping">libtorrent's internal mapping handle (stable per mapping).</param>
/// <param name="ExternalPort">External-facing port the router assigned. -1 when the mapping errored before establishing.</param>
/// <param name="Protocol">Transport protocol (TCP or UDP).</param>
/// <param name="Transport">Router protocol used (NAT-PMP or UPnP).</param>
/// <param name="HasError">True when the most recent alert for this mapping was an error.</param>
/// <param name="ErrorMessage">Error text from the router or libtorrent; empty when <see cref="HasError"/> is false.</param>
public record PortMapping(
    int Mapping,
    int ExternalPort,
    PortMappingProtocol Protocol,
    PortMappingTransport Transport,
    bool HasError,
    string ErrorMessage);