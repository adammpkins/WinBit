namespace LibtorrentSharp.Enums;

/// <summary>Transport protocol for a port-mapped listener.</summary>
public enum PortMappingProtocol : byte
{
    /// <summary>TCP listener.</summary>
    Tcp = 0,
    /// <summary>UDP listener.</summary>
    Udp = 1
}
