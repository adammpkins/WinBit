using System;
using System.Linq;
using System.Net;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Round-trips an IP filter through the native session: build a filter on the
/// managed side, set it, read it back via GetIpFilter, assert the rules survived.
/// Acts as the smoke test for the Phase F session::set_ip_filter binding.
/// </summary>
public sealed class IpFilterSmokeTests
{
    [Fact]
    public void IpFilter_addsAndExposesRules()
    {
        var filter = new IpFilter();
        filter.AddRule(IPAddress.Parse("10.0.0.0"), IPAddress.Parse("10.255.255.255"), IpFilterAccess.Blocked);
        filter.AddRule(new IpFilterRule(IPAddress.Parse("::1"), IPAddress.Parse("::1"), IpFilterAccess.Blocked));

        Assert.Equal(2, filter.Rules.Count);
        Assert.Equal(IpFilterAccess.Blocked, filter.Rules[0].Access);
    }

    [Fact]
    public void AddRule_throwsOnNullEndpoints()
    {
        var filter = new IpFilter();
        Assert.Throws<ArgumentNullException>(() =>
            filter.AddRule(new IpFilterRule(null!, IPAddress.Parse("10.0.0.1"), IpFilterAccess.Blocked)));
        Assert.Throws<ArgumentNullException>(() =>
            filter.AddRule(new IpFilterRule(IPAddress.Parse("10.0.0.1"), null!, IpFilterAccess.Blocked)));
    }

    [Fact]
    public void Clear_removesAllRules()
    {
        var filter = new IpFilter();
        filter.AddRule(IPAddress.Parse("10.0.0.0"), IPAddress.Parse("10.255.255.255"), IpFilterAccess.Blocked);
        filter.Clear();
        Assert.Empty(filter.Rules);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void SetIpFilter_thenGetIpFilter_roundTripsBlockedV4Range()
    {
        using var session = new LibtorrentSession();

        var filter = new IpFilter();
        filter.AddRule(IPAddress.Parse("10.0.0.0"), IPAddress.Parse("10.255.255.255"), IpFilterAccess.Blocked);
        filter.AddRule(IPAddress.Parse("192.168.0.0"), IPAddress.Parse("192.168.255.255"), IpFilterAccess.Blocked);
        session.SetIpFilter(filter);

        var roundTripped = session.GetIpFilter();
        // libtorrent's ip_filter merges adjacent ranges with the same flag, so we
        // assert the rules we put in are reachable rather than insisting on count.
        var blockedV4 = roundTripped.Rules
            .Where(r => r.Access == IpFilterAccess.Blocked && r.Start.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .ToList();
        Assert.NotEmpty(blockedV4);
        Assert.Contains(blockedV4, r =>
            Equals(r.Start, IPAddress.Parse("10.0.0.0")) && Equals(r.End, IPAddress.Parse("10.255.255.255")));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void SetIpFilter_withEmptyFilter_clearsPreviousRules()
    {
        using var session = new LibtorrentSession();

        var initial = new IpFilter();
        initial.AddRule(IPAddress.Parse("10.0.0.0"), IPAddress.Parse("10.255.255.255"), IpFilterAccess.Blocked);
        session.SetIpFilter(initial);

        session.SetIpFilter(new IpFilter());
        var afterClear = session.GetIpFilter();
        Assert.DoesNotContain(afterClear.Rules, r => r.Access == IpFilterAccess.Blocked);
    }
}
