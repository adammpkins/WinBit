using FluentAssertions;
using Microsoft.Extensions.Options;
using WinBit.Core.Common;
using WinBit.Core.Hosting;
using WinBit.Core.Persistence;
using WinBit.Core.Sharing;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class ShareLimitOverrideTests
{
    private static readonly TorrentId TorrentA = TorrentId.FromInfoHash("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly TorrentId TorrentB = TorrentId.FromInfoHash("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

    private static Paths MakePaths(TempDirectory temp) =>
        new(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));

    [Fact]
    public async Task Upsert_roundtrips_through_disk()
    {
        using var temp = new TempDirectory();
        var paths = MakePaths(temp);
        var service = new ShareLimitOverrideService(paths);

        await service.UpsertAsync(new PerTorrentShareLimitOverride
        {
            Id = TorrentA,
            RatioLimit = 2.5,
            SeedingTimeLimit = TimeSpan.FromHours(24),
            Action = ShareLimitAction.Remove,
        });

        var reloaded = await new ShareLimitOverrideService(paths).GetAsync(TorrentA);
        reloaded.Should().NotBeNull();
        reloaded!.RatioLimit.Should().Be(2.5);
        reloaded.SeedingTimeLimit.Should().Be(TimeSpan.FromHours(24));
        reloaded.Action.Should().Be(ShareLimitAction.Remove);
        reloaded.Mode.Should().Be(ShareLimitsMode.Default);
    }

    [Fact]
    public async Task Upsert_replaces_existing_entry()
    {
        using var temp = new TempDirectory();
        var service = new ShareLimitOverrideService(MakePaths(temp));

        await service.UpsertAsync(new PerTorrentShareLimitOverride { Id = TorrentA, RatioLimit = 1.0 });
        await service.UpsertAsync(new PerTorrentShareLimitOverride { Id = TorrentA, RatioLimit = 3.0 });

        (await service.GetAllAsync()).Should().ContainSingle()
            .Which.RatioLimit.Should().Be(3.0);
    }

    [Fact]
    public async Task Remove_drops_entry_from_disk()
    {
        using var temp = new TempDirectory();
        var paths = MakePaths(temp);
        var service = new ShareLimitOverrideService(paths);

        await service.UpsertAsync(new PerTorrentShareLimitOverride { Id = TorrentA, RatioLimit = 1.0 });
        await service.RemoveAsync(TorrentA);

        (await new ShareLimitOverrideService(paths).GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Effective_returns_global_when_no_override_exists()
    {
        using var temp = new TempDirectory();
        var service = new ShareLimitOverrideService(MakePaths(temp));
        await service.GetAllAsync(); // warm the cache

        var global = new ShareLimits { RatioLimit = 1.5, Action = ShareLimitAction.Stop };
        service.Effective(TorrentA, global).Should().BeSameAs(global);
    }

    [Fact]
    public async Task Effective_uses_override_values_over_global()
    {
        using var temp = new TempDirectory();
        var service = new ShareLimitOverrideService(MakePaths(temp));
        await service.UpsertAsync(new PerTorrentShareLimitOverride
        {
            Id = TorrentA,
            RatioLimit = 5.0,
            Action = ShareLimitAction.Remove,
        });

        var global = new ShareLimits
        {
            RatioLimit = 1.5,
            SeedingTimeLimit = TimeSpan.FromHours(12),
            Action = ShareLimitAction.Stop,
            Mode = ShareLimitsMode.MatchAll,
        };

        var effective = service.Effective(TorrentA, global);
        effective.RatioLimit.Should().Be(5.0);
        effective.Action.Should().Be(ShareLimitAction.Remove);
        // Fields not set on override fall back to the global setting.
        effective.SeedingTimeLimit.Should().Be(TimeSpan.FromHours(12));
        effective.Mode.Should().Be(ShareLimitsMode.MatchAll);
    }

    [Fact]
    public async Task Effective_is_isolated_per_torrent()
    {
        using var temp = new TempDirectory();
        var service = new ShareLimitOverrideService(MakePaths(temp));
        await service.UpsertAsync(new PerTorrentShareLimitOverride { Id = TorrentA, RatioLimit = 5.0 });

        var global = new ShareLimits { RatioLimit = 1.0 };
        service.Effective(TorrentA, global).RatioLimit.Should().Be(5.0);
        service.Effective(TorrentB, global).RatioLimit.Should().Be(1.0);
    }

    [Fact]
    public async Task Upsert_rejects_empty_torrent_id()
    {
        using var temp = new TempDirectory();
        var service = new ShareLimitOverrideService(MakePaths(temp));

        await FluentActions.Invoking(() => service.UpsertAsync(new PerTorrentShareLimitOverride
        {
            Id = new TorrentId(string.Empty),
        })).Should().ThrowAsync<ArgumentException>();
    }
}
