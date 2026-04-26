#nullable enable
using System;
using System.Collections.Generic;
using System.Net;

namespace LibtorrentSharp;

/// <summary>
/// libtorrent IP-filter access verdict for an address range. Mirrors libtorrent's
/// <c>ip_filter</c> flag bitmask — currently only the blocked bit is defined,
/// but the underlying surface is a <c>uint32</c> to keep room for future flags.
/// </summary>
[Flags]
public enum IpFilterAccess : uint
{
    /// <summary>No restriction — the default when a range matches no rule.</summary>
    Allowed = 0,

    /// <summary>Block inbound and outbound traffic to this range.</summary>
    Blocked = 1,
}

/// <summary>
/// One contiguous IP range with an access verdict. <see cref="Start"/> and
/// <see cref="End"/> may be IPv4 or IPv6 (libtorrent keeps v4 / v6 rules in
/// separate internal tables, but callers can mix both in a single <see cref="IpFilter"/>).
/// Ranges are inclusive at both ends.
/// </summary>
public readonly record struct IpFilterRule(IPAddress Start, IPAddress End, IpFilterAccess Access);

/// <summary>
/// A mutable set of <see cref="IpFilterRule"/>s to apply to a session via
/// <see cref="LibtorrentSession.SetIpFilter"/>. Passing an empty filter clears
/// the session's current filter; later rules override earlier rules where ranges
/// overlap (libtorrent semantics).
/// </summary>
public sealed class IpFilter
{
    private readonly List<IpFilterRule> _rules = new();

    /// <summary>Snapshot of the current rule set in insertion order.</summary>
    public IReadOnlyList<IpFilterRule> Rules => _rules;

    /// <summary>Shortcut for <see cref="AddRule(IpFilterRule)"/> from raw components.</summary>
    public void AddRule(IPAddress start, IPAddress end, IpFilterAccess access)
        => AddRule(new IpFilterRule(start, end, access));

    public void AddRule(IpFilterRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule.Start);
        ArgumentNullException.ThrowIfNull(rule.End);
        _rules.Add(rule);
    }

    public void Clear() => _rules.Clear();
}
