using System.Net;
using FluentAssertions;
using WinBit.Core.BitTorrent;
using Xunit;

namespace WinBit.Tests;

public sealed class DhtBootstrapSeederTests
{
    [Fact]
    public void EncodeCompactNode_produces_26_bytes_with_correct_ip_and_port()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("192.0.2.1"), 6881);

        var bytes = DhtBootstrapSeeder.EncodeCompactNode(endpoint);

        bytes.Should().HaveCount(26);
        bytes[20..24].Should().Equal(0xC0, 0x00, 0x02, 0x01);
        bytes[24..26].Should().Equal(0x1A, 0xE1);
    }

    [Fact]
    public void EncodeCompactNode_encodes_nonstandard_port_big_endian()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("203.0.113.42"), 25401);

        var bytes = DhtBootstrapSeeder.EncodeCompactNode(endpoint);

        bytes[20..24].Should().Equal(0xCB, 0x00, 0x71, 0x2A);
        bytes[24..26].Should().Equal(0x63, 0x39);
    }

    [Fact]
    public void EncodeCompactNode_fills_node_id_with_random_bytes()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("192.0.2.1"), 6881);

        var a = DhtBootstrapSeeder.EncodeCompactNode(endpoint);
        var b = DhtBootstrapSeeder.EncodeCompactNode(endpoint);

        a[..20].Should().NotEqual(b[..20], "NodeIds are random; two encodes of the same endpoint must differ in the first 20 bytes");
    }

    [Fact]
    public void EncodeCompactNode_rejects_ipv6()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("2001:db8::1"), 6881);

        var act = () => DhtBootstrapSeeder.EncodeCompactNode(endpoint);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("router.example.com", "router.example.com", 6881)]
    [InlineData("router.example.com:6881", "router.example.com", 6881)]
    [InlineData("dht.libtorrent.org:25401", "dht.libtorrent.org", 25401)]
    [InlineData("  padded.example:1337  ", "padded.example", 1337)]
    public void TryParseHostSpec_accepts_valid_specs(string spec, string expectedHost, int expectedPort)
    {
        DhtBootstrapSeeder.TryParseHostSpec(spec, out var host, out var port).Should().BeTrue();
        host.Should().Be(expectedHost);
        port.Should().Be(expectedPort);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(":6881")]
    [InlineData("host:")]
    [InlineData("host:abc")]
    [InlineData("host:0")]
    [InlineData("host:65536")]
    [InlineData("host:-1")]
    public void TryParseHostSpec_rejects_invalid_specs(string? spec)
    {
        DhtBootstrapSeeder.TryParseHostSpec(spec, out _, out _).Should().BeFalse();
    }
}
