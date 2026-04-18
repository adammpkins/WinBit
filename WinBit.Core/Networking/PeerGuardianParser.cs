using System.Net;

namespace WinBit.Core.Networking;

/// <summary>
/// Parser for the PeerGuardian <c>.p2p</c> blocklist format. Ported from
/// <c>FilterParserThread::parseP2PFilterFile</c> in
/// <c>qbittorrent/src/base/bittorrent/filterparserthread.cpp</c>. Each non-comment line must
/// look like <c>Organization name:1.0.0.0-1.255.255.255</c>. The organization name may contain
/// ':' characters — we split on the *last* colon. Comments begin with '#' or '//'. Start and
/// end addresses must be the same family (both v4 or both v6). Malformed lines are counted
/// into <see cref="PeerGuardianParseResult.ErrorCount"/>; the parser keeps going.
/// </summary>
public static class PeerGuardianParser
{
    public static PeerGuardianParseResult Parse(TextReader reader)
    {
        var ranges = new List<IpRange>();
        var errors = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0
                || trimmed[0] == '#'
                || (trimmed.Length >= 2 && trimmed[0] == '/' && trimmed[1] == '/'))
            {
                continue;
            }

            // Split on the LAST colon — org labels may contain ':' themselves.
            var colon = line.LastIndexOf(':');
            if (colon < 0)
            {
                errors++;
                continue;
            }

            var rangePart = line[(colon + 1)..];
            var dash = rangePart.IndexOf('-');
            if (dash < 0)
            {
                errors++;
                continue;
            }

            var startText = rangePart[..dash].Trim();
            var endText = rangePart[(dash + 1)..].Trim();

            if (!IPAddress.TryParse(startText, out var startAddr)
                || !IPAddress.TryParse(endText, out var endAddr))
            {
                errors++;
                continue;
            }

            if (startAddr.AddressFamily != endAddr.AddressFamily)
            {
                errors++;
                continue;
            }

            if (IpRange.Compare(startAddr, endAddr) > 0)
            {
                errors++;
                continue;
            }

            ranges.Add(new IpRange(startAddr, endAddr));
        }

        return new PeerGuardianParseResult(ranges, errors);
    }
}

public sealed record PeerGuardianParseResult(IReadOnlyList<IpRange> Ranges, int ErrorCount);
