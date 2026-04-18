using FluentAssertions;
using WinBit.Core.Categories;
using Xunit;

namespace WinBit.Tests;

/// <summary>
/// Parity tests for <see cref="TmmPathResolver"/>. Fixtures are derived from
/// <c>qbittorrent/src/base/bittorrent/sessionimpl.cpp</c> — <c>SessionImpl::categorySavePath</c>.
/// </summary>
public sealed class TmmPathResolverTests
{
    private const string Global = @"D:\Downloads";

    private static Func<string, Category?> Map(params Category[] categories)
    {
        var dict = categories.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        return name => dict.TryGetValue(name, out var c) ? c : null;
    }

    [Fact]
    public void No_category_returns_global_save_path()
    {
        TmmPathResolver.Resolve(Global, null, _ => null).Should().Be(Global);
        TmmPathResolver.Resolve(Global, string.Empty, _ => null).Should().Be(Global);
    }

    [Fact]
    public void Absolute_save_path_is_returned_verbatim()
    {
        var lookup = Map(new Category { Name = "linux", SavePath = @"E:\ISOs\Linux" });
        TmmPathResolver.Resolve(Global, "linux", lookup).Should().Be(@"E:\ISOs\Linux");
    }

    [Fact]
    public void Relative_save_path_is_combined_with_global_default()
    {
        var lookup = Map(new Category { Name = "linux", SavePath = "Linux" });
        TmmPathResolver.Resolve(Global, "linux", lookup)
            .Should().Be(Path.Combine(Global, "Linux"));
    }

    [Fact]
    public void Empty_save_path_appends_category_name_to_global()
    {
        var lookup = Map(new Category { Name = "music", SavePath = null });
        TmmPathResolver.Resolve(Global, "music", lookup)
            .Should().Be(Path.Combine(Global, "music"));
    }

    [Fact]
    public void Unknown_category_behaves_like_empty_save_path()
    {
        TmmPathResolver.Resolve(Global, "ghost", _ => null)
            .Should().Be(Path.Combine(Global, "ghost"));
    }

    [Fact]
    public void Nested_category_with_empty_save_path_resolves_through_parent()
    {
        var lookup = Map(
            new Category { Name = "movies", SavePath = @"F:\Media\Movies" },
            new Category { Name = "movies/4k", SavePath = null });

        TmmPathResolver.Resolve(Global, "movies/4k", lookup)
            .Should().Be(Path.Combine(@"F:\Media\Movies", "4k"));
    }

    [Fact]
    public void Nested_category_with_relative_save_path_combines_with_global_base()
    {
        // qBittorrent parity: a non-empty relative savePath is combined with the *global*
        // base, not with the parent's resolved path — parent chaining only kicks in when
        // the child's savePath is empty (see sessionimpl.cpp:942–956).
        var lookup = Map(
            new Category { Name = "movies", SavePath = @"F:\Media\Movies" },
            new Category { Name = "movies/4k", SavePath = "UHD" });

        TmmPathResolver.Resolve(Global, "movies/4k", lookup)
            .Should().Be(Path.Combine(Global, "UHD"));
    }

    [Fact]
    public void Nested_category_with_absolute_save_path_ignores_parent()
    {
        var lookup = Map(
            new Category { Name = "movies", SavePath = @"F:\Media\Movies" },
            new Category { Name = "movies/4k", SavePath = @"X:\UHD" });

        TmmPathResolver.Resolve(Global, "movies/4k", lookup).Should().Be(@"X:\UHD");
    }

    [Fact]
    public void Deeply_nested_empty_paths_chain_all_the_way_up()
    {
        var lookup = Map(
            new Category { Name = "a", SavePath = null },
            new Category { Name = "a/b", SavePath = null },
            new Category { Name = "a/b/c", SavePath = null });

        TmmPathResolver.Resolve(Global, "a/b/c", lookup)
            .Should().Be(Path.Combine(Global, "a", "b", "c"));
    }

    [Fact]
    public void SubcategoryName_returns_leaf_segment()
    {
        TmmPathResolver.SubcategoryName("a/b/c").Should().Be("c");
        TmmPathResolver.SubcategoryName("solo").Should().Be("solo");
    }

    [Fact]
    public void ParentCategoryName_returns_prefix_or_empty()
    {
        TmmPathResolver.ParentCategoryName("a/b/c").Should().Be("a/b");
        TmmPathResolver.ParentCategoryName("solo").Should().Be(string.Empty);
    }
}
