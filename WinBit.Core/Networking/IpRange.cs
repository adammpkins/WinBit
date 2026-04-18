using System.Net;

namespace WinBit.Core.Networking;

/// <summary>
/// Inclusive IP address range used by the peer filter. Start and End must be the same family
/// (both IPv4 or both IPv6). Comparison uses the unsigned byte-string order — natural for v4
/// and defined (by <c>Compare</c>) for v6.
/// </summary>
public readonly record struct IpRange
{
    public IPAddress Start { get; }

    public IPAddress End { get; }

    public IpRange(IPAddress start, IPAddress end)
    {
        if (start.AddressFamily != end.AddressFamily)
        {
            throw new ArgumentException("Start and end must be the same address family.", nameof(end));
        }
        if (Compare(start, end) > 0)
        {
            throw new ArgumentException("Start must be <= end.", nameof(end));
        }
        Start = start;
        End = end;
    }

    public bool Contains(IPAddress address)
    {
        if (address.AddressFamily != Start.AddressFamily)
        {
            return false;
        }
        return Compare(address, Start) >= 0 && Compare(address, End) <= 0;
    }

    /// <summary>Byte-lexicographic compare. Sufficient for both v4 and v6.</summary>
    public static int Compare(IPAddress a, IPAddress b)
    {
        var x = a.GetAddressBytes();
        var y = b.GetAddressBytes();
        if (x.Length != y.Length)
        {
            return x.Length.CompareTo(y.Length);
        }
        for (var i = 0; i < x.Length; i++)
        {
            var c = x[i].CompareTo(y[i]);
            if (c != 0)
            {
                return c;
            }
        }
        return 0;
    }
}
