using FluentAssertions;
using WinBit.Core.BitTorrent;
using Xunit;

namespace WinBit.Tests;

public sealed class FolderRenameHelperTests
{
    private static TorrentFileEntry MakeEntry(int index, string relativePath) => new()
    {
        Index = index,
        Name = relativePath.Split('/')[^1],
        RelativePath = relativePath,
        SizeBytes = 1024,
        Priority = FileDownloadPriority.Normal,
    };

    [Fact]
    public void Basic_rename_rewrites_file_under_top_level_folder()
    {
        var files = new[] { MakeEntry(0, "docs/readme.md") };

        var result = FolderRenameHelper.BuildRenamedPaths(files, "docs", "manual").ToList();

        result.Should().ContainSingle()
            .Which.Should().Be((0, "manual/readme.md"));
    }

    [Fact]
    public void Nested_rename_rewrites_file_under_sub_folder()
    {
        var files = new[] { MakeEntry(0, "docs/api/ref.md") };

        var result = FolderRenameHelper.BuildRenamedPaths(files, "docs/api", "docs/reference").ToList();

        result.Should().ContainSingle()
            .Which.Should().Be((0, "docs/reference/ref.md"));
    }

    [Fact]
    public void Sibling_exclusion_only_renames_files_under_old_folder()
    {
        var files = new[]
        {
            MakeEntry(0, "docs/a.txt"),
            MakeEntry(1, "src/b.txt"),
        };

        var result = FolderRenameHelper.BuildRenamedPaths(files, "docs", "manual").ToList();

        result.Should().ContainSingle()
            .Which.Should().Be((0, "manual/a.txt"));
    }

    [Fact]
    public void Root_level_file_with_no_slash_is_excluded()
    {
        var files = new[] { MakeEntry(0, "readme.md") };

        var result = FolderRenameHelper.BuildRenamedPaths(files, "docs", "manual").ToList();

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("docs/")]
    [InlineData("docs")]
    public void Trailing_slash_on_old_folder_path_is_tolerated(string oldFolderPath)
    {
        var files = new[] { MakeEntry(0, "docs/readme.md") };

        var result = FolderRenameHelper.BuildRenamedPaths(files, oldFolderPath, "manual").ToList();

        result.Should().ContainSingle()
            .Which.Should().Be((0, "manual/readme.md"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_or_whitespace_oldFolderPath_throws_ArgumentException(string? oldFolderPath)
    {
        var files = new[] { MakeEntry(0, "docs/readme.md") };

        var act = () => FolderRenameHelper.BuildRenamedPaths(files, oldFolderPath!, "manual").ToList();

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_or_whitespace_newFolderPath_throws_ArgumentException(string newFolderPath)
    {
        var files = new[] { MakeEntry(0, "docs/readme.md") };

        var act = () => FolderRenameHelper.BuildRenamedPaths(files, "docs", newFolderPath).ToList();

        act.Should().Throw<ArgumentException>();
    }
}
