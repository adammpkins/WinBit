using FluentAssertions;
using WinBit.Core.Rss;
using Xunit;

namespace WinBit.Tests;

public sealed class RssFeedParserTests
{
    private const string FeedUrl = "http://example.com/feed";

    [Fact]
    public void Parses_minimal_rss_2_0_feed()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0">
              <channel>
                <title>Example Tracker</title>
                <link>https://example.com/</link>
                <item>
                  <title>Show.S01E05.1080p</title>
                  <link>https://example.com/t/42</link>
                  <enclosure url="https://example.com/t/42.torrent" type="application/x-bittorrent" />
                  <pubDate>Mon, 07 Apr 2026 12:00:00 GMT</pubDate>
                </item>
                <item>
                  <title>Movie.2026.BluRay</title>
                  <enclosure url="https://example.com/t/43.torrent" type="application/x-bittorrent" />
                  <pubDate>Tue, 08 Apr 2026 13:30:00 +0000</pubDate>
                </item>
              </channel>
            </rss>
            """;

        var doc = RssFeedParser.Parse(xml, FeedUrl);

        doc.FeedUrl.Should().Be(FeedUrl);
        doc.Title.Should().Be("Example Tracker");
        doc.Link.Should().Be("https://example.com/");
        doc.Articles.Should().HaveCount(2);

        var first = doc.Articles[0];
        first.Title.Should().Be("Show.S01E05.1080p");
        first.TorrentUrl.Should().Be("https://example.com/t/42.torrent");
        first.PublishedUtc.Should().Be(new DateTime(2026, 4, 7, 12, 0, 0, DateTimeKind.Utc));
        first.FeedUrl.Should().Be(FeedUrl);
    }

    [Fact]
    public void Rss_falls_back_to_link_when_no_enclosure()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0">
              <channel>
                <title>Magnet Feed</title>
                <item>
                  <title>Some Magnet</title>
                  <link>magnet:?xt=urn:btih:abc</link>
                </item>
              </channel>
            </rss>
            """;

        var doc = RssFeedParser.Parse(xml, FeedUrl);

        doc.Articles.Should().ContainSingle()
            .Which.TorrentUrl.Should().Be("magnet:?xt=urn:btih:abc");
    }

    [Fact]
    public void Parses_minimal_atom_feed()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Atom Tracker</title>
              <link rel="alternate" href="https://atom.example/" />
              <entry>
                <title>Atom.Show.S02E10</title>
                <link rel="enclosure" href="https://atom.example/t/1.torrent" type="application/x-bittorrent" />
                <published>2026-04-07T12:00:00Z</published>
              </entry>
              <entry>
                <title>Atom.Movie</title>
                <link rel="alternate" href="https://atom.example/article/2" />
                <updated>2026-04-08T13:30:00+00:00</updated>
              </entry>
            </feed>
            """;

        var doc = RssFeedParser.Parse(xml, FeedUrl);

        doc.Title.Should().Be("Atom Tracker");
        doc.Link.Should().Be("https://atom.example/");
        doc.Articles.Should().HaveCount(2);

        doc.Articles[0].Title.Should().Be("Atom.Show.S02E10");
        doc.Articles[0].TorrentUrl.Should().Be("https://atom.example/t/1.torrent");
        doc.Articles[0].PublishedUtc.Should().Be(new DateTime(2026, 4, 7, 12, 0, 0, DateTimeKind.Utc));

        // Second entry has no enclosure → fall back to alternate link.
        doc.Articles[1].TorrentUrl.Should().Be("https://atom.example/article/2");
    }

    [Fact]
    public void Empty_input_returns_empty_document()
    {
        var doc = RssFeedParser.Parse("", FeedUrl);
        doc.Articles.Should().BeEmpty();
        doc.FeedUrl.Should().Be(FeedUrl);
    }

    [Fact]
    public void Malformed_xml_returns_empty_document_without_throwing()
    {
        var doc = RssFeedParser.Parse("<rss><unterminated>", FeedUrl);
        doc.Articles.Should().BeEmpty();
    }

    [Fact]
    public void Unknown_root_element_returns_empty_document()
    {
        var doc = RssFeedParser.Parse("<html><body>no feed here</body></html>", FeedUrl);
        doc.Articles.Should().BeEmpty();
    }

    [Fact]
    public void Rss_date_parsing_tolerates_wrong_day_of_week()
    {
        // 2026-04-07 is actually a Tuesday; real feeds regularly mislabel the day.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0">
              <channel>
                <item>
                  <title>x</title>
                  <enclosure url="https://x/t.torrent" />
                  <pubDate>Mon, 07 Apr 2026 12:00:00 GMT</pubDate>
                </item>
              </channel>
            </rss>
            """;

        var doc = RssFeedParser.Parse(xml, FeedUrl);
        doc.Articles[0].PublishedUtc.Should().Be(new DateTime(2026, 4, 7, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Missing_pubdate_yields_default_datetime_but_title_still_parses()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0">
              <channel>
                <title>No Date Feed</title>
                <item>
                  <title>Mystery</title>
                  <enclosure url="https://x/t.torrent" />
                </item>
              </channel>
            </rss>
            """;

        var doc = RssFeedParser.Parse(xml, FeedUrl);
        doc.Articles.Should().ContainSingle();
        doc.Articles[0].Title.Should().Be("Mystery");
        doc.Articles[0].PublishedUtc.Should().Be(default);
    }
}
