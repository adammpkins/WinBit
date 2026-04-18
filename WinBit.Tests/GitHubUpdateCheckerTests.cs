using FluentAssertions;
using WinBit.Core.Updates;
using Xunit;

namespace WinBit.Tests;

public sealed class GitHubUpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("V1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("1.0", 1, 0, -1)]
    [InlineData("1.2.3-rc1", 1, 2, 3)]
    [InlineData("1.2.3+metadata", 1, 2, 3)]
    public void TryParseVersion_handles_common_tag_shapes(string tag, int major, int minor, int build)
    {
        var v = GitHubUpdateChecker.TryParseVersion(tag);
        v.Should().NotBeNull();
        v!.Major.Should().Be(major);
        v.Minor.Should().Be(minor);
        v.Build.Should().Be(build);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nightly")]
    [InlineData("v-broken")]
    public void TryParseVersion_returns_null_for_unparseable(string? tag)
    {
        GitHubUpdateChecker.TryParseVersion(tag).Should().BeNull();
    }

    [Fact]
    public void Parse_reports_update_when_latest_is_newer_than_current()
    {
        var json = """{ "tag_name": "v1.5.0", "html_url": "https://example/release/1.5.0" }""";
        var info = GitHubUpdateChecker.Parse(new Version(1, 0, 0), json);

        info.HasUpdate.Should().BeTrue();
        info.LatestTag.Should().Be("v1.5.0");
        info.Latest.Should().Be(new Version(1, 5, 0));
        info.ReleaseUrl.Should().Be("https://example/release/1.5.0");
    }

    [Fact]
    public void Parse_reports_no_update_when_tag_matches_current()
    {
        var json = """{ "tag_name": "1.0.0", "html_url": "https://example" }""";
        var info = GitHubUpdateChecker.Parse(new Version(1, 0, 0), json);

        info.HasUpdate.Should().BeFalse();
    }

    [Fact]
    public void Parse_reports_no_update_when_tag_is_older()
    {
        var json = """{ "tag_name": "0.9.0", "html_url": "https://example" }""";
        var info = GitHubUpdateChecker.Parse(new Version(1, 0, 0), json);

        info.HasUpdate.Should().BeFalse();
        info.Latest.Should().Be(new Version(0, 9, 0));
    }

    [Fact]
    public void Parse_handles_missing_tag_gracefully()
    {
        var json = """{ "html_url": "https://example" }""";
        var info = GitHubUpdateChecker.Parse(new Version(1, 0, 0), json);

        info.Latest.Should().BeNull();
        info.HasUpdate.Should().BeFalse();
    }
}
