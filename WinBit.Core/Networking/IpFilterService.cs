using System.Net;
using System.Net.Sockets;

namespace WinBit.Core.Networking;

public sealed class IpFilterService : IIpFilterService
{
    // Two sorted ranges arrays per family — binary search on Start, then check End. v4 and v6
    // are kept separate because their byte-length compare differs; splitting avoids family
    // checks on the hot path.
    private IpRange[] _v4Ranges = Array.Empty<IpRange>();
    private IpRange[] _v6Ranges = Array.Empty<IpRange>();

    public int RuleCount => _v4Ranges.Length + _v6Ranges.Length;

    public async Task LoadAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Clear();
            return;
        }

        using var stream = new StreamReader(path);
        var text = await stream.ReadToEndAsync(ct).ConfigureAwait(false);
        using var reader = new StringReader(text);

        var result = PeerGuardianParser.Parse(reader);
        Replace(result.Ranges);
    }

    public void Clear()
    {
        _v4Ranges = Array.Empty<IpRange>();
        _v6Ranges = Array.Empty<IpRange>();
    }

    public bool IsBlocked(IPAddress address)
    {
        var ranges = address.AddressFamily switch
        {
            AddressFamily.InterNetwork => _v4Ranges,
            AddressFamily.InterNetworkV6 => _v6Ranges,
            _ => Array.Empty<IpRange>(),
        };

        if (ranges.Length == 0)
        {
            return false;
        }

        // Binary-search for the right-most range whose Start <= address. Then check that
        // address <= End. Ranges are expected to be non-overlapping (PeerGuardian files rarely
        // overlap; if they do, a match on the earliest containing range is still correct).
        var lo = 0;
        var hi = ranges.Length - 1;
        var candidate = -1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            var cmp = IpRange.Compare(ranges[mid].Start, address);
            if (cmp == 0)
            {
                return true;
            }
            if (cmp < 0)
            {
                candidate = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return candidate >= 0 && IpRange.Compare(address, ranges[candidate].End) <= 0;
    }

    /// <summary>Exposed for tests that want to bypass file IO.</summary>
    public void Replace(IEnumerable<IpRange> ranges)
    {
        var v4 = new List<IpRange>();
        var v6 = new List<IpRange>();
        foreach (var r in ranges)
        {
            (r.Start.AddressFamily == AddressFamily.InterNetwork ? v4 : v6).Add(r);
        }

        v4.Sort(static (a, b) => IpRange.Compare(a.Start, b.Start));
        v6.Sort(static (a, b) => IpRange.Compare(a.Start, b.Start));
        _v4Ranges = v4.ToArray();
        _v6Ranges = v6.ToArray();
    }
}
