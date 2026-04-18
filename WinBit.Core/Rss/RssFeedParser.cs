using System.Globalization;
using System.Xml.Linq;

namespace WinBit.Core.Rss;

/// <summary>
/// Parses RSS 2.0 and Atom 1.0 feed XML into <see cref="RssFeedDocument"/>. Torrent-specific
/// feeds typically use RSS 2.0 with <c>&lt;enclosure&gt;</c> pointing at the <c>.torrent</c>
/// URL; Atom feeds use <c>&lt;link rel="enclosure"&gt;</c>. Falls back to the entry's main
/// link when no enclosure is present so magnet-only feeds still expose something to click.
/// </summary>
public static class RssFeedParser
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    public static RssFeedDocument Parse(string xml, string feedUrl)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Empty(feedUrl);
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (Exception)
        {
            return Empty(feedUrl);
        }

        var root = doc.Root;
        if (root is null)
        {
            return Empty(feedUrl);
        }

        // RSS 2.0: <rss><channel>...<item>...</item></channel></rss>
        if (root.Name.LocalName.Equals("rss", StringComparison.OrdinalIgnoreCase))
        {
            return ParseRss(root, feedUrl);
        }

        // Atom: <feed xmlns="http://www.w3.org/2005/Atom">
        if (root.Name == Atom + "feed" || root.Name.LocalName.Equals("feed", StringComparison.OrdinalIgnoreCase))
        {
            return ParseAtom(root, feedUrl);
        }

        return Empty(feedUrl);
    }

    private static RssFeedDocument Empty(string feedUrl) =>
        new() { FeedUrl = feedUrl, Articles = Array.Empty<RssArticle>() };

    private static RssFeedDocument ParseRss(XElement rss, string feedUrl)
    {
        var channel = rss.Element("channel");
        if (channel is null)
        {
            return Empty(feedUrl);
        }

        var articles = new List<RssArticle>();
        foreach (var item in channel.Elements("item"))
        {
            var title = item.Element("title")?.Value.Trim() ?? string.Empty;
            var enclosure = item.Element("enclosure")?.Attribute("url")?.Value;
            var link = item.Element("link")?.Value;
            var torrentUrl = FirstNonEmpty(enclosure, link);

            var published = ParseRssDate(item.Element("pubDate")?.Value);

            articles.Add(new RssArticle
            {
                FeedUrl = feedUrl,
                Title = title,
                TorrentUrl = torrentUrl,
                PublishedUtc = published,
            });
        }

        return new RssFeedDocument
        {
            FeedUrl = feedUrl,
            Title = channel.Element("title")?.Value.Trim(),
            Link = channel.Element("link")?.Value.Trim(),
            Articles = articles,
        };
    }

    private static RssFeedDocument ParseAtom(XElement feed, string feedUrl)
    {
        var articles = new List<RssArticle>();
        foreach (var entry in feed.Elements().Where(e => e.Name.LocalName == "entry"))
        {
            var title = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value.Trim() ?? string.Empty;
            var torrentUrl = PickAtomLink(entry);
            var published = ParseIso8601(
                entry.Elements().FirstOrDefault(e => e.Name.LocalName == "published")?.Value
                ?? entry.Elements().FirstOrDefault(e => e.Name.LocalName == "updated")?.Value);

            articles.Add(new RssArticle
            {
                FeedUrl = feedUrl,
                Title = title,
                TorrentUrl = torrentUrl,
                PublishedUtc = published,
            });
        }

        return new RssFeedDocument
        {
            FeedUrl = feedUrl,
            Title = feed.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value.Trim(),
            Link = PickAtomLink(feed),
            Articles = articles,
        };
    }

    private static string? PickAtomLink(XElement parent)
    {
        // Prefer rel="enclosure" — torrent feeds stash the .torrent URL there.
        var links = parent.Elements().Where(e => e.Name.LocalName == "link").ToArray();
        var enclosure = links.FirstOrDefault(l =>
            string.Equals(l.Attribute("rel")?.Value, "enclosure", StringComparison.OrdinalIgnoreCase));
        if (enclosure is not null)
        {
            return enclosure.Attribute("href")?.Value;
        }

        var alternate = links.FirstOrDefault(l =>
            string.Equals(l.Attribute("rel")?.Value, "alternate", StringComparison.OrdinalIgnoreCase));
        if (alternate is not null)
        {
            return alternate.Attribute("href")?.Value ?? alternate.Value;
        }

        var first = links.FirstOrDefault();
        return first?.Attribute("href")?.Value ?? first?.Value;
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c))
            {
                return c.Trim();
            }
        }
        return null;
    }

    private static DateTime ParseRssDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }
        var trimmed = raw.Trim();

        // Real-world feeds regularly send a wrong day-of-week prefix. Strip it before parsing
        // so we don't reject otherwise-valid dates.
        var commaIdx = trimmed.IndexOf(", ", StringComparison.Ordinal);
        if (commaIdx > 0 && commaIdx <= 4)
        {
            trimmed = trimmed[(commaIdx + 2)..];
        }

        var formats = new[]
        {
            "d MMM yyyy HH:mm:ss 'GMT'",
            "d MMM yyyy HH:mm:ss zzz",
            "d MMM yyyy HH:mm 'GMT'",
            "d MMM yyyy HH:mm zzz",
        };
        if (DateTimeOffset.TryParseExact(trimmed, formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
        {
            return dto.UtcDateTime;
        }
        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dto))
        {
            return dto.UtcDateTime;
        }
        return default;
    }

    private static DateTime ParseIso8601(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            return dto.UtcDateTime;
        }
        return default;
    }
}
