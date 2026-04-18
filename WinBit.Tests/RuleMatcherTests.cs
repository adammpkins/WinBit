using FluentAssertions;
using WinBit.Core.Rss;
using Xunit;

namespace WinBit.Tests;

/// <summary>
/// Parity fixtures derived from qBittorrent's AutoDownloadRule behavior
/// (<c>qbittorrent/src/base/rss/rss_autodownloadrule.cpp</c>). Each test carries the
/// qBittorrent-expected outcome as the oracle.
/// </summary>
public sealed class RuleMatcherTests
{
    private static RssArticle Article(string title, string feed = "http://feed", DateTime? date = null) =>
        new() { Title = title, FeedUrl = feed, PublishedUtc = date ?? DateTime.UtcNow };

    [Fact]
    public void Disabled_rule_never_matches()
    {
        var rule = new AutoDownloadRule { Enabled = false };
        RuleMatcher.Evaluate(rule, Article("Show.S01E05")).IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Empty_filters_match_every_article()
    {
        var rule = new AutoDownloadRule();
        RuleMatcher.Evaluate(rule, Article("anything goes")).IsMatch.Should().BeTrue();
    }

    // --- Must-contain -------------------------------------------------------

    [Fact]
    public void MustContain_tokens_are_ANDed()
    {
        var rule = new AutoDownloadRule { MustContain = "foo bar" };
        RuleMatcher.Evaluate(rule, Article("foo goes with bar")).IsMatch.Should().BeTrue();
        RuleMatcher.Evaluate(rule, Article("only foo present")).IsMatch.Should().BeFalse();
        RuleMatcher.Evaluate(rule, Article("only bar present")).IsMatch.Should().BeFalse();
    }

    [Fact]
    public void MustContain_is_case_insensitive()
    {
        var rule = new AutoDownloadRule { MustContain = "FOO" };
        RuleMatcher.Evaluate(rule, Article("lowercase foo title")).IsMatch.Should().BeTrue();
    }

    [Fact]
    public void MustContain_pipe_acts_as_OR_between_expressions()
    {
        var rule = new AutoDownloadRule { MustContain = "foo bar | baz" };
        RuleMatcher.Evaluate(rule, Article("foo and bar")).IsMatch.Should().BeTrue();
        RuleMatcher.Evaluate(rule, Article("just baz here")).IsMatch.Should().BeTrue();
        RuleMatcher.Evaluate(rule, Article("just foo here")).IsMatch.Should().BeFalse();
    }

    [Fact]
    public void MustContain_wildcards_expand()
    {
        var rule = new AutoDownloadRule { MustContain = "foo*bar" };
        RuleMatcher.Evaluate(rule, Article("fooXYZbar")).IsMatch.Should().BeTrue();
        RuleMatcher.Evaluate(rule, Article("bar then foo")).IsMatch.Should().BeFalse();
    }

    [Fact]
    public void MustContain_regex_mode_passes_raw_pattern()
    {
        var rule = new AutoDownloadRule { UseRegex = true, MustContain = @"foo\s+bar" };
        RuleMatcher.Evaluate(rule, Article("foo   bar XYZ")).IsMatch.Should().BeTrue();
        RuleMatcher.Evaluate(rule, Article("foobar")).IsMatch.Should().BeFalse();
    }

    // --- Must-not-contain ---------------------------------------------------

    [Fact]
    public void MustNotContain_blocks_match_when_any_expression_hits()
    {
        var rule = new AutoDownloadRule
        {
            MustContain = "",
            MustNotContain = "spam | junk",
        };
        RuleMatcher.Evaluate(rule, Article("clean title")).IsMatch.Should().BeTrue();
        RuleMatcher.Evaluate(rule, Article("contains spam here")).IsMatch.Should().BeFalse();
        RuleMatcher.Evaluate(rule, Article("junk here")).IsMatch.Should().BeFalse();
    }

    // --- Episode filter -----------------------------------------------------

    [Theory]
    [InlineData("Show.S01E05.720p", "1x5", true)]
    [InlineData("Show.S01E05.720p", "1x05", true)]
    [InlineData("Show.S01E05.720p", "1x6", false)]
    [InlineData("Show.1x05.720p", "1x5", true)]
    [InlineData("Show.S02E05.720p", "1x5", false)]
    [InlineData("Show.S01E05.720p", "2x5", false)]
    public void EpisodeFilter_single_season_and_episode(string title, string filter, bool expected)
    {
        var rule = new AutoDownloadRule { EpisodeFilter = filter + ";" };
        RuleMatcher.Evaluate(rule, Article(title)).IsMatch.Should().Be(expected);
    }

    [Theory]
    [InlineData("Show.S01E05.720p", "1x2-8", true)]
    [InlineData("Show.S01E05.720p", "1x6-8", false)]
    [InlineData("Show.S01E10.720p", "1x5-", true)]     // unbounded upper
    [InlineData("Show.S01E03.720p", "1x5-", false)]
    [InlineData("Show.S02E03.720p", "1x5-", true)]     // future season
    public void EpisodeFilter_range(string title, string filter, bool expected)
    {
        var rule = new AutoDownloadRule { EpisodeFilter = filter + ";" };
        RuleMatcher.Evaluate(rule, Article(title)).IsMatch.Should().Be(expected);
    }

    [Fact]
    public void EpisodeFilter_multiple_subrules_are_OR()
    {
        var rule = new AutoDownloadRule { EpisodeFilter = "1x3;7-10;" };
        RuleMatcher.Evaluate(rule, Article("Show.S01E03")).IsMatch.Should().BeTrue();
        RuleMatcher.Evaluate(rule, Article("Show.S01E08")).IsMatch.Should().BeTrue();
        RuleMatcher.Evaluate(rule, Article("Show.S01E05")).IsMatch.Should().BeFalse();
    }

    // --- Smart episode filter ----------------------------------------------

    [Fact]
    public void SmartFilter_accepts_new_episode_and_reports_tag()
    {
        var rule = new AutoDownloadRule { SmartFilter = true };
        var result = RuleMatcher.Evaluate(rule, Article("Show.S01E05.1080p"));

        result.IsMatch.Should().BeTrue();
        result.NewEpisodeTags.Should().ContainSingle().Which.Should().Be("01x05");
    }

    [Fact]
    public void SmartFilter_rejects_already_seen_episode_by_default()
    {
        var rule = new AutoDownloadRule
        {
            SmartFilter = true,
            PreviouslyMatchedEpisodes = new[] { "01x05" },
        };
        RuleMatcher.Evaluate(rule, Article("Show.S01E05.1080p")).IsMatch.Should().BeFalse();
    }

    [Fact]
    public void SmartFilter_lets_REPACK_through_even_when_base_episode_seen()
    {
        var rule = new AutoDownloadRule
        {
            SmartFilter = true,
            PreviouslyMatchedEpisodes = new[] { "01x05" },
        };
        var result = RuleMatcher.Evaluate(rule, Article("Show.S01E05.REPACK.1080p"));

        result.IsMatch.Should().BeTrue();
        result.NewEpisodeTags.Should().Contain("01x05-REPACK");
    }

    [Fact]
    public void SmartFilter_PROPER_REPACK_combo_adds_both_individual_tags()
    {
        var rule = new AutoDownloadRule
        {
            SmartFilter = true,
            PreviouslyMatchedEpisodes = new[] { "01x05" },
        };
        var result = RuleMatcher.Evaluate(rule, Article("Show.S01E05.REPACK.PROPER.1080p"));

        result.IsMatch.Should().BeTrue();
        result.NewEpisodeTags.Should().Contain(new[] { "01x05-REPACK-PROPER", "01x05-REPACK", "01x05-PROPER" });
    }

    [Fact]
    public void SmartFilter_respects_downloadRepacks_disabled()
    {
        var rule = new AutoDownloadRule
        {
            SmartFilter = true,
            PreviouslyMatchedEpisodes = new[] { "01x05" },
        };
        RuleMatcher.Evaluate(rule, Article("Show.S01E05.REPACK"), downloadRepacks: false)
            .IsMatch.Should().BeFalse();
    }

    [Fact]
    public void SmartFilter_rejects_title_without_recognizable_episode()
    {
        var rule = new AutoDownloadRule { SmartFilter = true };
        RuleMatcher.Evaluate(rule, Article("SomeMovie.BluRay.1080p")).IsMatch.Should().BeFalse();
    }

    [Fact]
    public void SmartFilter_recognises_date_format()
    {
        var rule = new AutoDownloadRule { SmartFilter = true };
        var result = RuleMatcher.Evaluate(rule, Article("Daily.Show.2017-01-05.HDTV"));

        result.IsMatch.Should().BeTrue();
        // Format 3 default matches: (\d{4}[.\-]\d{1,2}[.\-]\d{1,2}) → "2017-01-05"
        result.NewEpisodeTags.Should().ContainSingle().Which.Should().Be("2017-01-05");
    }

    // --- Feed scoping -------------------------------------------------------

    [Fact]
    public void Rule_with_AffectedFeeds_ignores_other_feeds()
    {
        var rule = new AutoDownloadRule
        {
            AffectedFeeds = new[] { "http://feed-a" },
        };
        RuleMatcher.Evaluate(rule, Article("anything", feed: "http://feed-a")).IsMatch.Should().BeTrue();
        RuleMatcher.Evaluate(rule, Article("anything", feed: "http://feed-b")).IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Rule_without_AffectedFeeds_matches_anywhere()
    {
        var rule = new AutoDownloadRule();
        RuleMatcher.Evaluate(rule, Article("anything", feed: "http://whatever")).IsMatch.Should().BeTrue();
    }

    // --- Ignore window ------------------------------------------------------

    [Fact]
    public void IgnoreDays_blocks_articles_within_window_since_last_match()
    {
        var rule = new AutoDownloadRule
        {
            IgnoreDays = 3,
            LastMatchUtc = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
        };
        // Within 3 days → blocked.
        RuleMatcher.Evaluate(rule, Article("x", date: new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc)))
            .IsMatch.Should().BeFalse();
        // Past window → allowed.
        RuleMatcher.Evaluate(rule, Article("x", date: new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc)))
            .IsMatch.Should().BeTrue();
    }
}
