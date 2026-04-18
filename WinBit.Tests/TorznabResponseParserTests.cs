using FluentAssertions;
using WinBit.Core.Search.Torznab;
using Xunit;

namespace WinBit.Tests;

public sealed class TorznabResponseParserTests
{
    private const string Xml = """
<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0" xmlns:torznab="http://torznab.com/schemas/2015/feed">
  <channel>
    <title>Test</title>
    <item>
      <title>Ubuntu 24.04 Desktop</title>
      <link>http://example/download/ubuntu.torrent</link>
      <pubDate>Mon, 01 Apr 2024 12:00:00 +0000</pubDate>
      <comments>http://example/details/1</comments>
      <size>2150000000</size>
      <torznab:attr name="size" value="2150000000" />
      <torznab:attr name="seeders" value="500" />
      <torznab:attr name="peers" value="520" />
      <torznab:attr name="magneturl" value="magnet:?xt=urn:btih:abc" />
    </item>
    <item>
      <title>Fedora 40</title>
      <link>http://example/download/fedora.torrent</link>
      <torznab:attr name="seeders" value="42" />
      <torznab:attr name="peers" value="42" />
    </item>
  </channel>
</rss>
""";

    [Fact]
    public void Parses_title_link_seeders_and_magnet_from_torznab_attrs()
    {
        var results = TorznabResponseParser.Parse("test-feed", Xml);
        results.Should().HaveCount(2);

        var ubuntu = results[0];
        ubuntu.Name.Should().Be("Ubuntu 24.04 Desktop");
        ubuntu.PluginName.Should().Be("test-feed");
        ubuntu.SizeBytes.Should().Be(2_150_000_000L);
        ubuntu.Seeders.Should().Be(500);
        ubuntu.Leechers.Should().Be(20);
        ubuntu.MagnetUri.Should().Be("magnet:?xt=urn:btih:abc");
        ubuntu.TorrentUrl.Should().Be("http://example/download/ubuntu.torrent");
        ubuntu.DetailsUrl.Should().Be("http://example/details/1");
        ubuntu.PublishedUtc.Should().NotBeNull();
    }

    [Fact]
    public void Zero_leechers_when_seeders_equals_peers()
    {
        var results = TorznabResponseParser.Parse("feed", Xml);
        results[1].Seeders.Should().Be(42);
        results[1].Leechers.Should().Be(0);
    }

    [Fact]
    public void Malformed_xml_yields_empty_without_throwing()
    {
        TorznabResponseParser.Parse("feed", "<<< not xml").Should().BeEmpty();
        TorznabResponseParser.Parse("feed", string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Missing_channel_yields_empty()
    {
        TorznabResponseParser.Parse("feed", "<?xml version=\"1.0\"?><rss />").Should().BeEmpty();
    }
}
