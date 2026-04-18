using System.Text;
using System.Text.RegularExpressions;

namespace WinBit.Core.Rss;

/// <summary>
/// Pure port of qBittorrent's <c>AutoDownloadRule::matches</c> /
/// <c>matchesEpisodeFilterExpression</c> / <c>matchesSmartEpisodeFilter</c> from
/// <c>qbittorrent/src/base/rss/rss_autodownloadrule.cpp</c>. Kept stateless so the caller
/// owns <see cref="AutoDownloadRule.PreviouslyMatchedEpisodes"/> persistence.
/// </summary>
public static class RuleMatcher
{
    // See AutoDownloader::smartEpisodeFilters() — the four formats qBittorrent ships with.
    // Joined via computeSmartFilterRegex() into the single anchored-on-word-boundary pattern.
    private const string SmartEpisodePattern =
        @"(?:_|\b)(?:s(\d+)e(\d+))|(?:(\d+)x(\d+))|(?:(\d{4}[.\-]\d{1,2}[.\-]\d{1,2}))|(?:(\d{1,2}[.\-]\d{1,2}[.\-]\d{4}))(?:_|\b)";

    private static readonly Regex SmartEpisodeRegex = new(SmartEpisodePattern,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EpisodeFilterParser = new(@"(^\d{1,4})x(.*;$)",
        RegexOptions.Compiled);

    public sealed record Result(bool IsMatch, IReadOnlyList<string> NewEpisodeTags)
    {
        public static readonly Result NoMatch = new(false, Array.Empty<string>());
    }

    /// <summary>Default qBittorrent behavior: repacks/propers bypass "already matched".</summary>
    public static Result Evaluate(AutoDownloadRule rule, RssArticle article, bool downloadRepacks = true)
    {
        if (!rule.Enabled)
        {
            return Result.NoMatch;
        }

        if (rule.AffectedFeeds.Count > 0 &&
            !rule.AffectedFeeds.Any(f => string.Equals(f, article.FeedUrl, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.NoMatch;
        }

        if (rule.IgnoreDays > 0 && rule.LastMatchUtc is DateTime lastMatch)
        {
            if (article.PublishedUtc < lastMatch.AddDays(rule.IgnoreDays))
            {
                return Result.NoMatch;
            }
        }

        var title = article.Title ?? "";

        if (!MustContainPasses(title, rule.MustContain, rule.UseRegex))
        {
            return Result.NoMatch;
        }
        if (MustNotContainBlocks(title, rule.MustNotContain, rule.UseRegex))
        {
            return Result.NoMatch;
        }
        if (!EpisodeFilterPasses(title, rule.EpisodeFilter))
        {
            return Result.NoMatch;
        }

        if (rule.SmartFilter)
        {
            return EvaluateSmartEpisode(rule, title, downloadRepacks);
        }

        return new Result(true, Array.Empty<string>());
    }

    private static bool MustContainPasses(string title, string raw, bool useRegex)
    {
        var expressions = ParseExpressions(raw, useRegex);
        if (expressions.Count == 0)
        {
            return true;
        }
        // OR over expressions — any one passing is enough.
        return expressions.Any(expr => ExpressionMatches(title, expr, useRegex));
    }

    private static bool MustNotContainBlocks(string title, string raw, bool useRegex)
    {
        var expressions = ParseExpressions(raw, useRegex);
        if (expressions.Count == 0)
        {
            return false;
        }
        return expressions.Any(expr => ExpressionMatches(title, expr, useRegex));
    }

    private static IReadOnlyList<string> ParseExpressions(string raw, bool useRegex)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return Array.Empty<string>();
        }
        // Faithful port of setMustContain / setMustNotContain: regex mode = single expression,
        // wildcard mode = split on '|'. A single empty result collapses to "no condition".
        var split = useRegex ? new[] { raw } : raw.Split('|');
        if (split.Length == 1 && split[0].Length == 0)
        {
            return Array.Empty<string>();
        }
        return split;
    }

    private static bool ExpressionMatches(string title, string expression, bool useRegex)
    {
        if (expression.Length == 0)
        {
            // qBittorrent: "A regex of the form 'expr|' will always match, so do the same for wildcards"
            return true;
        }

        if (useRegex)
        {
            try
            {
                return Regex.IsMatch(title, expression, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        // Wildcard mode: tokens split on whitespace, all tokens must match anywhere in the title.
        var tokens = expression.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var pattern = WildcardToRegex(token);
            if (!Regex.IsMatch(title, pattern, RegexOptions.IgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Minimal faithful substitute for Qt's <c>wildcardToRegularExpression(pattern,
    /// UnanchoredWildcardConversion | NonPathWildcardConversion)</c>: <c>*</c> → <c>.*</c>,
    /// <c>?</c> → <c>.</c>, other characters are regex-escaped. Produced pattern is unanchored.
    /// </summary>
    internal static string WildcardToRegex(string wildcard)
    {
        var sb = new StringBuilder(wildcard.Length * 2);
        foreach (var c in wildcard)
        {
            switch (c)
            {
                case '*': sb.Append(".*"); break;
                case '?': sb.Append('.'); break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        return sb.ToString();
    }

    private static bool EpisodeFilterPasses(string title, string filter)
    {
        if (string.IsNullOrEmpty(filter))
        {
            return true;
        }

        var parser = EpisodeFilterParser.Match(filter);
        if (!parser.Success)
        {
            return false;
        }

        if (!int.TryParse(parser.Groups[1].Value, out var seasonOurs))
        {
            return false;
        }

        var episodes = parser.Groups[2].Value.Split(';');
        foreach (var rawEpisode in episodes)
        {
            var episode = TrimLeadingZeros(rawEpisode);
            if (episode.Length == 0)
            {
                continue;
            }

            if (episode.Contains('-'))
            {
                // Range: "5-8" or "5-"
                if (TryMatchTitleEpisode(title, out var seasonTheirs, out var episodeTheirs))
                {
                    if (episode.EndsWith("-", StringComparison.Ordinal))
                    {
                        var episodeOurs = int.Parse(episode[..^1]);
                        if ((seasonTheirs == seasonOurs && episodeTheirs >= episodeOurs) ||
                            seasonTheirs > seasonOurs)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        var range = episode.Split('-');
                        if (range.Length != 2 ||
                            !int.TryParse(range[0], out var first) ||
                            !int.TryParse(range[1], out var last))
                        {
                            continue;
                        }
                        if (first > last)
                        {
                            continue;
                        }
                        if (seasonTheirs == seasonOurs && episodeTheirs >= first && episodeTheirs <= last)
                        {
                            return true;
                        }
                    }
                }
            }
            else
            {
                // Single episode: e.g. "2" or "12"
                var pattern = $@"\b(?:s0?{seasonOurs}[ \-_\.]?e0?{episode}|{seasonOurs}x0?{episode})(?:\D|\b)";
                if (Regex.IsMatch(title, pattern, RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool TryMatchTitleEpisode(string title, out int season, out int episode)
    {
        // qBittorrent tries S01E05 first, then 01x05.
        var m = Regex.Match(title, @"\bs0?(\d{1,4})[ \-_\.]?e(0?\d{1,4})(?:\D|\b)", RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            m = Regex.Match(title, @"\b(\d{1,4})x(0?\d{1,4})(?:\D|\b)", RegexOptions.IgnoreCase);
        }
        if (m.Success &&
            int.TryParse(m.Groups[1].Value, out season) &&
            int.TryParse(m.Groups[2].Value, out episode))
        {
            return true;
        }
        season = 0;
        episode = 0;
        return false;
    }

    private static string TrimLeadingZeros(string ep)
    {
        var i = 0;
        while (i < ep.Length - 1 && ep[i] == '0')
        {
            i++;
        }
        return ep[i..];
    }

    private static Result EvaluateSmartEpisode(AutoDownloadRule rule, string title, bool downloadRepacks)
    {
        var episodeStr = ComputeEpisodeName(title);
        if (string.IsNullOrEmpty(episodeStr))
        {
            return Result.NoMatch;
        }

        if (!rule.PreviouslyMatchedEpisodes.Contains(episodeStr, StringComparer.OrdinalIgnoreCase))
        {
            return new Result(true, new[] { episodeStr });
        }

        if (!downloadRepacks)
        {
            return Result.NoMatch;
        }

        var isRepack = title.Contains("REPACK", StringComparison.OrdinalIgnoreCase);
        var isProper = title.Contains("PROPER", StringComparison.OrdinalIgnoreCase);
        if (!isRepack && !isProper)
        {
            return Result.NoMatch;
        }

        var fullEpisode = episodeStr
            + (isRepack ? "-REPACK" : "")
            + (isProper ? "-PROPER" : "");

        if (rule.PreviouslyMatchedEpisodes.Contains(fullEpisode, StringComparer.OrdinalIgnoreCase))
        {
            return Result.NoMatch;
        }

        var added = new List<string> { fullEpisode };
        if (isRepack && isProper)
        {
            added.Add(episodeStr + "-REPACK");
            added.Add(episodeStr + "-PROPER");
        }
        return new Result(true, added);
    }

    internal static string ComputeEpisodeName(string title)
    {
        var match = SmartEpisodeRegex.Match(title);
        if (!match.Success)
        {
            return "";
        }
        // Join all non-empty capture groups (excluding group 0) with 'x' — the qBittorrent
        // behavior: for s01e05 → "01x05"; for 2017.01.01 → "2017.01.01".
        var parts = new List<string>();
        for (var i = 1; i <= match.Groups.Count - 1; i++)
        {
            var cap = match.Groups[i].Value;
            if (!string.IsNullOrEmpty(cap))
            {
                parts.Add(cap);
            }
        }
        return string.Join("x", parts);
    }
}
