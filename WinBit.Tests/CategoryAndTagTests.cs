using FluentAssertions;
using Microsoft.Extensions.Options;
using WinBit.Core.Categories;
using WinBit.Core.Hosting;
using WinBit.Core.Persistence;
using WinBit.Core.Tags;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class CategoryAndTagTests
{
    [Fact]
    public async Task CategoryService_upsert_roundtrips_through_disk()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));

        var service = new CategoryService(paths);
        await service.UpsertAsync(new Category { Name = "linux", SavePath = @"D:\linux" });
        await service.UpsertAsync(new Category { Name = "music" });

        var reloaded = new CategoryService(paths);
        var all = await reloaded.GetAllAsync();
        all.Should().HaveCount(2);
        (await reloaded.GetAsync("linux"))!.SavePath.Should().Be(@"D:\linux");
        (await reloaded.GetAsync("MUSIC"))!.Name.Should().Be("music", "lookup is case-insensitive");
    }

    [Fact]
    public async Task CategoryService_upsert_replaces_existing_entry()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));
        var service = new CategoryService(paths);

        await service.UpsertAsync(new Category { Name = "archive", SavePath = @"C:\a" });
        await service.UpsertAsync(new Category { Name = "archive", SavePath = @"D:\b" });

        var category = await service.GetAsync("archive");
        category.Should().NotBeNull();
        category!.SavePath.Should().Be(@"D:\b");
        (await service.GetAllAsync()).Should().HaveCount(1);
    }

    [Fact]
    public async Task CategoryService_remove_drops_entry_from_disk()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));
        var service = new CategoryService(paths);

        await service.UpsertAsync(new Category { Name = "ephemeral" });
        await service.RemoveAsync("ephemeral");

        (await new CategoryService(paths).GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task CategoryService_rejects_empty_name()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));
        var service = new CategoryService(paths);

        await FluentActions.Invoking(() => service.UpsertAsync(new Category { Name = "  " }))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TagService_add_dedupes_and_sorts_case_insensitively()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));
        var service = new TagService(paths);

        await service.AddAsync("iso");
        await service.AddAsync("ISO");
        await service.AddAsync("archive");

        var tags = await service.GetAllAsync();
        tags.Should().HaveCount(2).And.ContainInOrder("archive", "iso");
    }

    [Fact]
    public async Task TagService_remove_persists_through_reload()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));
        var service = new TagService(paths);

        await service.AddAsync("alpha");
        await service.AddAsync("beta");
        await service.RemoveAsync("alpha");

        var reloaded = await new TagService(paths).GetAllAsync();
        reloaded.Should().ContainSingle().Which.Should().Be("beta");
    }

    [Fact]
    public async Task TagService_rejects_empty_tag()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));
        var service = new TagService(paths);

        await FluentActions.Invoking(() => service.AddAsync(""))
            .Should().ThrowAsync<ArgumentException>();
    }
}
