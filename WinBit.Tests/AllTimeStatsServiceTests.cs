using FluentAssertions;
using Microsoft.Extensions.Options;
using WinBit.Core.Hosting;
using WinBit.Core.Persistence;
using WinBit.Core.Stats;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class AllTimeStatsServiceTests
{
    private static AllTimeStatsService Build(TempDirectory temp)
    {
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));
        return new AllTimeStatsService(paths);
    }

    [Fact]
    public async Task Tick_establishes_baseline_without_changing_totals()
    {
        using var temp = new TempDirectory();
        var service = Build(temp);
        await service.LoadAsync();

        service.Tick(1_000_000, 500_000);

        service.Current.DownloadedBytes.Should().Be(0);
        service.Current.UploadedBytes.Should().Be(0);
    }

    [Fact]
    public async Task Positive_deltas_accumulate_into_all_time_totals()
    {
        using var temp = new TempDirectory();
        var service = Build(temp);
        await service.LoadAsync();

        service.Tick(1_000, 100);
        service.Tick(5_000, 2_100);

        service.Current.DownloadedBytes.Should().Be(4_000);
        service.Current.UploadedBytes.Should().Be(2_000);
    }

    [Fact]
    public async Task Negative_deltas_are_clamped_to_zero()
    {
        using var temp = new TempDirectory();
        var service = Build(temp);
        await service.LoadAsync();

        service.Tick(10_000, 5_000);
        // Simulate a torrent removal: the session total now dips.
        service.Tick(3_000, 1_000);
        // Another tick above the post-dip baseline.
        service.Tick(4_000, 2_000);

        service.Current.DownloadedBytes.Should().Be(1_000);
        service.Current.UploadedBytes.Should().Be(1_000);
    }

    [Fact]
    public async Task Save_and_load_round_trips_counters()
    {
        using var temp = new TempDirectory();
        var first = Build(temp);
        await first.LoadAsync();
        first.Tick(0, 0);
        first.Tick(9_999, 7_777);
        await first.SaveAsync();

        var second = Build(temp);
        await second.LoadAsync();

        second.Current.DownloadedBytes.Should().Be(9_999);
        second.Current.UploadedBytes.Should().Be(7_777);
    }

    [Fact]
    public async Task Corrupt_stats_file_falls_back_to_zero_without_throwing()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));
        await File.WriteAllTextAsync(paths.AllTimeStatsFile, "{ not json");

        var service = new AllTimeStatsService(paths);
        await service.LoadAsync();

        service.Current.DownloadedBytes.Should().Be(0);
        service.Current.UploadedBytes.Should().Be(0);
    }
}
