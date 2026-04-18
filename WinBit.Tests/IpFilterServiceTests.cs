using System.Net;
using FluentAssertions;
using WinBit.Core.Networking;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class IpFilterServiceTests
{
    [Fact]
    public void Empty_filter_never_blocks()
    {
        var service = new IpFilterService();
        service.IsBlocked(IPAddress.Parse("1.2.3.4")).Should().BeFalse();
        service.RuleCount.Should().Be(0);
    }

    [Fact]
    public void Hits_inside_loaded_range()
    {
        var service = new IpFilterService();
        service.Replace(new[]
        {
            new IpRange(IPAddress.Parse("10.0.0.0"), IPAddress.Parse("10.255.255.255")),
        });

        service.IsBlocked(IPAddress.Parse("10.0.0.1")).Should().BeTrue();
        service.IsBlocked(IPAddress.Parse("10.128.0.0")).Should().BeTrue();
        service.IsBlocked(IPAddress.Parse("10.255.255.255")).Should().BeTrue();
        service.IsBlocked(IPAddress.Parse("9.255.255.255")).Should().BeFalse();
        service.IsBlocked(IPAddress.Parse("11.0.0.0")).Should().BeFalse();
    }

    [Fact]
    public void Binary_search_picks_correct_range_among_many()
    {
        var service = new IpFilterService();
        service.Replace(new[]
        {
            new IpRange(IPAddress.Parse("1.0.0.0"), IPAddress.Parse("1.0.0.255")),
            new IpRange(IPAddress.Parse("5.0.0.0"), IPAddress.Parse("5.0.0.10")),
            new IpRange(IPAddress.Parse("100.0.0.0"), IPAddress.Parse("100.255.255.255")),
            new IpRange(IPAddress.Parse("200.1.2.3"), IPAddress.Parse("200.1.2.3")),
        });

        service.IsBlocked(IPAddress.Parse("5.0.0.5")).Should().BeTrue();
        service.IsBlocked(IPAddress.Parse("5.0.0.11")).Should().BeFalse();
        service.IsBlocked(IPAddress.Parse("100.50.50.50")).Should().BeTrue();
        service.IsBlocked(IPAddress.Parse("200.1.2.3")).Should().BeTrue();
        service.IsBlocked(IPAddress.Parse("200.1.2.4")).Should().BeFalse();
    }

    [Fact]
    public void V4_and_v6_ranges_are_searched_independently()
    {
        var service = new IpFilterService();
        service.Replace(new[]
        {
            new IpRange(IPAddress.Parse("10.0.0.0"), IPAddress.Parse("10.255.255.255")),
            new IpRange(IPAddress.Parse("2001:db8::"), IPAddress.Parse("2001:db8::ffff")),
        });

        service.IsBlocked(IPAddress.Parse("10.0.0.1")).Should().BeTrue();
        service.IsBlocked(IPAddress.Parse("2001:db8::10")).Should().BeTrue();
        service.IsBlocked(IPAddress.Parse("2001:db9::")).Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_round_trips_p2p_file_into_rule_count()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "list.p2p");
        await File.WriteAllTextAsync(path,
            "Block 1:1.0.0.0-1.0.0.255\n" +
            "# comment\n" +
            "Block 2:5.5.5.5-5.5.5.10\n");

        var service = new IpFilterService();
        await service.LoadAsync(path);

        service.RuleCount.Should().Be(2);
        service.IsBlocked(IPAddress.Parse("1.0.0.100")).Should().BeTrue();
        service.IsBlocked(IPAddress.Parse("5.5.5.7")).Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_missing_file_clears_rules()
    {
        var service = new IpFilterService();
        service.Replace(new[]
        {
            new IpRange(IPAddress.Parse("1.0.0.0"), IPAddress.Parse("1.0.0.10")),
        });

        await service.LoadAsync(Path.Combine(Path.GetTempPath(), "absent-" + Guid.NewGuid() + ".p2p"));

        service.RuleCount.Should().Be(0);
        service.IsBlocked(IPAddress.Parse("1.0.0.5")).Should().BeFalse();
    }

    [Fact]
    public void Clear_drops_every_rule()
    {
        var service = new IpFilterService();
        service.Replace(new[]
        {
            new IpRange(IPAddress.Parse("10.0.0.0"), IPAddress.Parse("10.0.0.10")),
        });
        service.Clear();
        service.RuleCount.Should().Be(0);
        service.IsBlocked(IPAddress.Parse("10.0.0.1")).Should().BeFalse();
    }
}
