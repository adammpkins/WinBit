using System.Globalization;
using System.Xml.Linq;

namespace WinBit.Core.Search.Torznab;

/// <summary>
/// Parses a Torznab response (RSS 2.0 superset with <c>torznab:attr</c> extension elements) into
/// plain <see cref="SearchResult"/>s. Ported from Jackett's schema — size / seeders / peers /
/// magneturl live under torznab:attr name="…" value="…" children of each item.
/// </summary>
public static class TorznabResponseParser
{
    private static readonly XNamespace TorznabNs = "http://torznab.com/schemas/2015/feed";

    public static IReadOnlyList<SearchResult> Parse(string pluginName, string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Array.Empty<SearchResult>();
        }
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return Array.Empty<SearchResult>();
        }

        var channel = doc.Root?.Element("channel");
        if (channel is null)
        {
            return Array.Empty<SearchResult>();
        }

        var results = new List<SearchResult>();
        foreach (var item in channel.Elements("item"))
        {
            var attrs = item.Elements(TorznabNs + "attr")
                .Where(e => e.Attribute("name") is not null)
                .ToDictionary(
                    e => e.Attribute("name")!.Value,
                    e => e.Attribute("value")?.Value ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);

            results.Add(new SearchResult(
                PluginName: pluginName,
                Name: item.Element("title")?.Value ?? string.Empty,
                SizeBytes: ParseLong(item.Element("size")?.Value) ?? ParseLong(attrs.GetValueOrDefault("size")),
                Seeders: ParseInt(attrs.GetValueOrDefault("seeders")),
                Leechers: ParseInt(attrs.GetValueOrDefault("peers")) is { } peers
                    ? Math.Max(0, peers - (ParseInt(attrs.GetValueOrDefault("seeders")) ?? 0))
                    : ParseInt(attrs.GetValueOrDefault("leechers")),
                MagnetUri: attrs.GetValueOrDefault("magneturl") is { Length: > 0 } m ? m : null,
                TorrentUrl: item.Element("link")?.Value is { Length: > 0 } link ? link : item.Element("enclosure")?.Attribute("url")?.Value,
                DetailsUrl: item.Element("comments")?.Value is { Length: > 0 } c ? c : item.Element("guid")?.Value,
                PublishedUtc: ParseDate(item.Element("pubDate")?.Value)));
        }
        return results;
    }

    private static long? ParseLong(string? s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static int? ParseInt(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
        {
            return dt;
        }
        return null;
    }
}
