namespace LibtorrentSharp.Enums;

/// <summary>Router protocol used to establish a port mapping.</summary>
public enum PortMappingTransport : byte
{
    /// <summary>NAT-PMP (RFC 6886).</summary>
    NatPmp = 0,
    /// <summary>UPnP Internet Gateway Device protocol.</summary>
    Upnp = 1
}
