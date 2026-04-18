using System.Net;
using FluentAssertions;
using WinBit.Core.Networking;
using Xunit;

namespace WinBit.Tests;

public sealed class PeerGuardianParserTests
{
    private static PeerGuardianParseResult Parse(string body) =>
        PeerGuardianParser.Parse(new StringReader(body));

    [Fact]
    public void Parses_simple_ipv4_range()
    {
        var result = Parse("Badguys:1.0.0.0-1.255.255.255\n");

        result.ErrorCount.Should().Be(0);
        result.Ranges.Should().ContainSingle();
        result.Ranges[0].Start.Should().Be(IPAddress.Parse("1.0.0.0"));
        result.Ranges[0].End.Should().Be(IPAddress.Parse("1.255.255.255"));
    }

    [Fact]
    public void Skips_comment_lines_and_blanks()
    {
        const string body = """
            # this is a comment
            // another comment

            Badguys:10.0.0.0-10.255.255.255
            """;

        var result = Parse(body);

        result.ErrorCount.Should().Be(0);
        result.Ranges.Should().ContainSingle();
    }

    [Fact]
    public void Splits_on_last_colon_so_labels_may_contain_colons()
    {
        // Label contains a colon (IPv6-ish text is a realistic case for abusive labels in the
        // wild). qBittorrent's parser uses the LAST colon as the separator.
        var result = Parse("Some:Org:Label:1.0.0.0-1.0.0.255\n");

        result.ErrorCount.Should().Be(0);
        result.Ranges.Should().ContainSingle();
        result.Ranges[0].Start.Should().Be(IPAddress.Parse("1.0.0.0"));
    }

    [Fact]
    public void Trims_whitespace_around_addresses()
    {
        var result = Parse("Label:  1.2.3.4  -  1.2.3.10  \n");

        result.ErrorCount.Should().Be(0);
        result.Ranges[0].Start.Should().Be(IPAddress.Parse("1.2.3.4"));
        result.Ranges[0].End.Should().Be(IPAddress.Parse("1.2.3.10"));
    }

    [Fact]
    public void Counts_missing_colon_as_an_error()
    {
        var result = Parse("nobody:here\n");
        result.ErrorCount.Should().Be(1);
        result.Ranges.Should().BeEmpty();
    }

    [Fact]
    public void Counts_missing_dash_as_an_error()
    {
        var result = Parse("Label:1.2.3.4\n");
        result.ErrorCount.Should().Be(1);
        result.Ranges.Should().BeEmpty();
    }

    [Fact]
    public void Counts_malformed_addresses_as_errors()
    {
        var result = Parse("Label:not.an.ip-1.2.3.4\n");
        result.ErrorCount.Should().Be(1);
        result.Ranges.Should().BeEmpty();
    }

    [Fact]
    public void Counts_v4_v6_mixed_ranges_as_errors()
    {
        var result = Parse("Label:1.2.3.4-::1\n");
        result.ErrorCount.Should().Be(1);
        result.Ranges.Should().BeEmpty();
    }

    [Fact]
    public void Ipv6_ranges_in_p2p_format_are_inherently_ambiguous()
    {
        // The PeerGuardian .p2p format is IPv4-oriented: "split on the last colon" clashes with
        // IPv6 address syntax (also full of colons). Parity with qBittorrent's
        // parseP2PFilterFile is to skip or mis-parse such lines — we count them as errors.
        var result = Parse("Label:2001:db8::1-2001:db8::ffff\n");
        result.ErrorCount.Should().BeGreaterThan(0);
        result.Ranges.Should().BeEmpty();
    }

    [Fact]
    public void Keeps_parsing_after_errors()
    {
        const string body = """
            garbage
            Good:1.0.0.0-1.0.0.10
            also-bad
            Other:2.0.0.0-2.0.0.5
            """;

        var result = Parse(body);

        result.ErrorCount.Should().Be(2);
        result.Ranges.Should().HaveCount(2);
    }
}
