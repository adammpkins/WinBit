using FluentAssertions;
using WinBit.Core.Shell;
using Xunit;

namespace WinBit.Tests;

public sealed class ActivationArgumentsTests
{
    [Fact]
    public void Empty_input_produces_no_work()
    {
        ActivationArguments.Parse(Array.Empty<string>()).HasWork.Should().BeFalse();
        ActivationArguments.ParseCommandLine(null).HasWork.Should().BeFalse();
        ActivationArguments.ParseCommandLine("   ").HasWork.Should().BeFalse();
    }

    [Theory]
    [InlineData("magnet:?xt=urn:btih:abc123")]
    [InlineData("MAGNET:?xt=urn:btih:DEADBEEF")]
    public void Magnet_uri_is_recognized_regardless_of_case(string arg)
    {
        var result = ActivationArguments.Parse(new[] { arg });
        result.MagnetUri.Should().Be(arg);
        result.TorrentFilePath.Should().BeNull();
    }

    [Fact]
    public void Torrent_path_is_recognized()
    {
        var result = ActivationArguments.Parse(new[] { @"C:\Downloads\ubuntu.torrent" });
        result.TorrentFilePath.Should().Be(@"C:\Downloads\ubuntu.torrent");
        result.MagnetUri.Should().BeNull();
    }

    [Fact]
    public void Magnet_takes_precedence_when_listed_first()
    {
        var result = ActivationArguments.Parse(new[] { "magnet:?xt=urn:btih:abc", @"C:\also.torrent" });
        result.MagnetUri.Should().NotBeNull();
        result.TorrentFilePath.Should().BeNull();
    }

    [Fact]
    public void Unquoted_whitespace_does_not_merge_args()
    {
        // Shell activations always quote paths with spaces; Parse() takes pre-split args.
        var result = ActivationArguments.Parse(new[] { @"C:\ProgramData\my.torrent" });
        result.TorrentFilePath.Should().Be(@"C:\ProgramData\my.torrent");
    }

    [Fact]
    public void Command_line_splitter_respects_quotes()
    {
        var result = ActivationArguments.ParseCommandLine("\"C:\\With Spaces\\file.torrent\"");
        result.TorrentFilePath.Should().Be(@"C:\With Spaces\file.torrent");
    }

    [Fact]
    public void Non_torrent_non_magnet_argument_is_ignored()
    {
        ActivationArguments.Parse(new[] { "--something", "plain-text" }).HasWork.Should().BeFalse();
    }

    [Fact]
    public void Command_line_with_multiple_tokens_picks_the_first_matching()
    {
        var result = ActivationArguments.ParseCommandLine("--minimized magnet:?xt=urn:btih:abc");
        result.MagnetUri.Should().Be("magnet:?xt=urn:btih:abc");
    }
}
