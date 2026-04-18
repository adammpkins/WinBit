using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Hosting;
using WinBit.Core.Logging;
using WinBit.Core.Persistence;
using WinBit.Core.Settings;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class SmokeTests
{
    [Fact]
    public async Task AddWinBitCore_composes_without_throwing()
    {
        using var temp = new TempDirectory();

        var services = new ServiceCollection();
        services.AddWinBitCore(opts => opts.DataRoot = temp.Path);

        await using var provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Paths>().Should().NotBeNull();
        provider.GetRequiredService<ILogService>().Should().NotBeNull();
        provider.GetRequiredService<ISettingsService>().Should().NotBeNull();
        provider.GetRequiredService<SqliteTorrentStateStore>().Should().NotBeNull();
        provider.GetRequiredService<ITorrentSessionService>().Should().NotBeNull();
    }

    [Fact]
    public async Task Settings_roundtrip_through_json_store()
    {
        using var temp = new TempDirectory();

        var services = new ServiceCollection();
        services.AddWinBitCore(opts => opts.DataRoot = temp.Path);
        await using var provider = services.BuildServiceProvider();

        var settings = provider.GetRequiredService<ISettingsService>();
        await settings.UpdateAsync(s => s.UiState.Theme = "Dark");
        await settings.SaveAsync();

        await using var provider2 = new ServiceCollection()
            .AddWinBitCore(opts => opts.DataRoot = temp.Path)
            .BuildServiceProvider();
        var settings2 = provider2.GetRequiredService<ISettingsService>();
        var loaded = await settings2.LoadAsync();

        loaded.UiState.Theme.Should().Be("Dark");
    }

    [Fact]
    public void Paths_materializes_data_root_tree_eagerly()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.Path, "first-run");

        var paths = new Paths(Microsoft.Extensions.Options.Options.Create(new WinBitCoreOptions { DataRoot = root }));

        Directory.Exists(paths.Root).Should().BeTrue();
        Directory.Exists(paths.RssDir).Should().BeTrue();
        Directory.Exists(paths.LogsDir).Should().BeTrue();
        paths.Root.Should().Be(root);
    }

    [Fact]
    public async Task TransfersGridLayout_round_trips_through_JsonSettingsStore()
    {
        using var temp = new TempDirectory();

        var services = new ServiceCollection();
        services.AddWinBitCore(opts => opts.DataRoot = temp.Path);
        await using var provider = services.BuildServiceProvider();

        var settings = provider.GetRequiredService<ISettingsService>();
        await settings.UpdateAsync(s =>
        {
            s.UiState.TransfersGrid.Columns["name"] = new TransferColumnState
            {
                Width = 317,
                Order = 0,
                SortDirection = "Ascending",
            };
            s.UiState.TransfersGrid.Columns["size"] = new TransferColumnState
            {
                Width = 88,
                Order = 2,
                SortDirection = null,
            };
        });
        await settings.SaveAsync();

        await using var provider2 = new ServiceCollection()
            .AddWinBitCore(opts => opts.DataRoot = temp.Path)
            .BuildServiceProvider();
        var reloaded = await provider2.GetRequiredService<ISettingsService>().LoadAsync();

        reloaded.UiState.TransfersGrid.Columns.Should().HaveCount(2);
        reloaded.UiState.TransfersGrid.Columns["name"].Width.Should().Be(317);
        reloaded.UiState.TransfersGrid.Columns["name"].Order.Should().Be(0);
        reloaded.UiState.TransfersGrid.Columns["name"].SortDirection.Should().Be("Ascending");
        reloaded.UiState.TransfersGrid.Columns["size"].Width.Should().Be(88);
        reloaded.UiState.TransfersGrid.Columns["size"].Order.Should().Be(2);
        reloaded.UiState.TransfersGrid.Columns["size"].SortDirection.Should().BeNull();
    }

    [Fact]
    public void RecentPathsHelper_dedupes_prepends_and_caps()
    {
        var list = new List<string>();

        RecentPathsHelper.PushMru(list, @"C:\a");
        RecentPathsHelper.PushMru(list, @"C:\b");
        RecentPathsHelper.PushMru(list, @"C:\c");
        list.Should().Equal(@"C:\c", @"C:\b", @"C:\a");

        // Pushing an existing entry moves it to the front, no duplicates.
        RecentPathsHelper.PushMru(list, @"c:\A");
        list.Should().Equal(@"c:\A", @"C:\c", @"C:\b");

        // Cap trims the oldest.
        for (int i = 0; i < 20; i++)
        {
            RecentPathsHelper.PushMru(list, $@"C:\pad-{i}", cap: 5);
        }
        list.Should().HaveCount(5);
        list[0].Should().Be(@"C:\pad-19");

        // Whitespace / empty is ignored.
        var before = list.Count;
        RecentPathsHelper.PushMru(list, "   ");
        RecentPathsHelper.PushMru(list, "");
        list.Count.Should().Be(before);
    }

    [Fact]
    public async Task BehaviorSettings_close_to_tray_defaults_off_and_round_trips()
    {
        using var temp = new TempDirectory();

        var services = new ServiceCollection();
        services.AddWinBitCore(opts => opts.DataRoot = temp.Path);
        await using var provider = services.BuildServiceProvider();

        var settings = provider.GetRequiredService<ISettingsService>();
        settings.Current.Behavior.CloseToTray.Should().BeFalse();

        await settings.UpdateAsync(s => s.Behavior.CloseToTray = true);
        await settings.SaveAsync();

        await using var provider2 = new ServiceCollection()
            .AddWinBitCore(opts => opts.DataRoot = temp.Path)
            .BuildServiceProvider();
        var reloaded = await provider2.GetRequiredService<ISettingsService>().LoadAsync();

        reloaded.Behavior.CloseToTray.Should().BeTrue();
    }

    [Fact]
    public async Task RecentSavePaths_round_trip_through_JsonSettingsStore()
    {
        using var temp = new TempDirectory();

        var services = new ServiceCollection();
        services.AddWinBitCore(opts => opts.DataRoot = temp.Path);
        await using var provider = services.BuildServiceProvider();

        var settings = provider.GetRequiredService<ISettingsService>();
        await settings.UpdateAsync(s =>
        {
            RecentPathsHelper.PushMru(s.UiState.RecentSavePaths, @"D:\downloads");
            RecentPathsHelper.PushMru(s.UiState.RecentSavePaths, @"D:\archive");
        });
        await settings.SaveAsync();

        await using var provider2 = new ServiceCollection()
            .AddWinBitCore(opts => opts.DataRoot = temp.Path)
            .BuildServiceProvider();
        var reloaded = await provider2.GetRequiredService<ISettingsService>().LoadAsync();

        reloaded.UiState.RecentSavePaths.Should().Equal(@"D:\archive", @"D:\downloads");
    }

    [Fact]
    public async Task ShareLimits_round_trip_through_JsonSettingsStore()
    {
        using var temp = new TempDirectory();

        var services = new ServiceCollection();
        services.AddWinBitCore(opts => opts.DataRoot = temp.Path);
        await using var provider = services.BuildServiceProvider();

        var settings = provider.GetRequiredService<ISettingsService>();
        await settings.UpdateAsync(s => s.BitTorrent.GlobalShareLimits = new WinBit.Core.Sharing.ShareLimits
        {
            RatioLimit = 2.5,
            SeedingTimeLimit = TimeSpan.FromHours(24),
            InactiveSeedingTimeLimit = TimeSpan.FromMinutes(90),
            Mode = WinBit.Core.Sharing.ShareLimitsMode.MatchAll,
            Action = WinBit.Core.Sharing.ShareLimitAction.RemoveWithContent,
        });
        await settings.SaveAsync();

        await using var provider2 = new ServiceCollection()
            .AddWinBitCore(opts => opts.DataRoot = temp.Path)
            .BuildServiceProvider();
        var reloaded = await provider2.GetRequiredService<ISettingsService>().LoadAsync();

        var limits = reloaded.BitTorrent.GlobalShareLimits;
        limits.RatioLimit.Should().Be(2.5);
        limits.SeedingTimeLimit.Should().Be(TimeSpan.FromHours(24));
        limits.InactiveSeedingTimeLimit.Should().Be(TimeSpan.FromMinutes(90));
        limits.Mode.Should().Be(WinBit.Core.Sharing.ShareLimitsMode.MatchAll);
        limits.Action.Should().Be(WinBit.Core.Sharing.ShareLimitAction.RemoveWithContent);
    }

    [Fact]
    public void LogService_ring_buffer_returns_entries()
    {
        var log = new LogService();
        log.Write("hello");
        log.Write("world");

        var entries = log.GetMessages();
        entries.Should().HaveCount(2);
        entries[0].Message.Should().Be("hello");
        entries[1].Message.Should().Be("world");
    }

    [Fact]
    public async Task JsonSettingsStore_debounces_and_flushes_last_value()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Microsoft.Extensions.Options.Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));

        await using var store = new JsonSettingsStore(paths, TimeSpan.FromMilliseconds(50));

        for (var i = 0; i < 10; i++)
        {
            await store.SaveAsync(new AppSettings { UiState = { Theme = $"Theme-{i}" } });
        }

        File.Exists(paths.SettingsFile).Should().BeFalse("debounced writes have not fired yet");

        await store.FlushAsync();

        File.Exists(paths.SettingsFile).Should().BeTrue();
        var reloaded = await store.LoadAsync();
        reloaded!.UiState.Theme.Should().Be("Theme-9");
    }

    [Fact]
    public async Task JsonSettingsStore_atomic_save_leaves_no_tmp_file()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Microsoft.Extensions.Options.Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));

        await using var store = new JsonSettingsStore(paths, TimeSpan.FromMilliseconds(1));
        await store.SaveAsync(new AppSettings { UiState = { Theme = "Dark" } });
        await store.FlushAsync();

        File.Exists(paths.SettingsFile).Should().BeTrue();
        File.Exists(paths.SettingsFile + ".tmp").Should().BeFalse("tmp sibling must be renamed away, not left on disk");
    }

    [Fact]
    public async Task JsonSettingsStore_recovers_from_stale_tmp_left_by_prior_crash()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Microsoft.Extensions.Options.Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));

        await using (var first = new JsonSettingsStore(paths, TimeSpan.FromMilliseconds(1)))
        {
            await first.SaveAsync(new AppSettings { UiState = { Theme = "Light" } });
            await first.FlushAsync();
        }

        await File.WriteAllTextAsync(paths.SettingsFile + ".tmp", "{ partial garbage from a crashed write");

        await using (var second = new JsonSettingsStore(paths, TimeSpan.FromMilliseconds(1)))
        {
            await second.SaveAsync(new AppSettings { UiState = { Theme = "Dark" } });
            await second.FlushAsync();

            var reloaded = await second.LoadAsync();
            reloaded!.UiState.Theme.Should().Be("Dark");
        }

        File.Exists(paths.SettingsFile + ".tmp").Should().BeFalse("successful save must replace any stale tmp file");
    }

    [Fact]
    public void BitTorrent_wire_types_compose_with_required_members()
    {
        var id = TorrentId.FromInfoHash("a".PadRight(40, '0'));

        var handle = new TorrentHandle
        {
            Id = id,
            Name = "example.iso",
            SavePath = @"D:\downloads",
            TotalSize = 1024 * 1024 * 1024,
            Category = "linux",
            Tags = new[] { "iso", "archive" },
            AddedUtc = DateTime.UtcNow,
        };
        handle.Tags.Should().HaveCount(2);

        var snapshot = new TorrentSnapshot
        {
            Id = id,
            State = TorrentState.Downloading,
            Progress = 0.42,
            BytesDownloaded = 100,
            DownloadSpeedBps = 1_000_000,
            Seeds = 5,
            Peers = 12,
            Eta = TimeSpan.FromMinutes(3),
        };
        snapshot.State.Should().Be(TorrentState.Downloading);

        var add = new AddTorrentParams
        {
            Source = "magnet:?xt=urn:btih:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            SavePath = @"D:\downloads",
        };
        add.Tags.Should().BeEmpty();
        add.StartImmediately.Should().BeTrue();

        var peer = new PeerInfo { Address = "203.0.113.5:51413", Client = "qBittorrent 4.6.0", Progress = 1.0, IsSeeder = true };
        peer.IsSeeder.Should().BeTrue();

        var tracker = new TrackerInfo { Url = new Uri("http://tracker.example/announce"), Status = TrackerStatus.Working, Seeds = 1 };
        tracker.Status.Should().Be(TrackerStatus.Working);
    }

    [Fact]
    public async Task TorrentSessionService_starts_and_stops_a_real_engine()
    {
        using var temp = new TempDirectory();

        var services = new ServiceCollection();
        services.AddWinBitCore(opts => opts.DataRoot = temp.Path);
        await using var provider = services.BuildServiceProvider();

        var session = provider.GetRequiredService<ITorrentSessionService>();
        session.IsRunning.Should().BeFalse();

        await session.StartAsync();
        session.IsRunning.Should().BeTrue();
        Directory.Exists(Path.Combine(temp.Path, "engine")).Should().BeTrue();
        session.Torrents.Should().BeEmpty("no torrents added yet");

        await session.StopAsync();
        session.IsRunning.Should().BeTrue("engine still alive after StopAllAsync; DisposeAsync tears it down");

        await session.DisposeAsync();
        session.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Session_adds_and_removes_a_magnet_uri()
    {
        using var temp = new TempDirectory();

        var services = new ServiceCollection();
        services.AddWinBitCore(opts => opts.DataRoot = temp.Path);
        await using var provider = services.BuildServiceProvider();

        var session = provider.GetRequiredService<ITorrentSessionService>();
        await session.StartAsync();

        const string magnet = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=winbit-test";
        var add = await session.AddAsync(new AddTorrentParams
        {
            Source = magnet,
            SavePath = temp.Path,
            StartImmediately = false,
        });

        add.IsSuccess.Should().BeTrue(add.Error ?? string.Empty);
        session.Torrents.Should().ContainSingle().Which.Value.Should().Be(add.Value.Value);

        var remove = await session.RemoveAsync(add.Value);
        remove.IsSuccess.Should().BeTrue();
        session.Torrents.Should().BeEmpty();

        await session.DisposeAsync();
    }

    [Fact]
    public async Task Session_speed_limit_surface_fails_cleanly_for_unknown_ids()
    {
        using var temp = new TempDirectory();
        var services = new ServiceCollection();
        services.AddWinBitCore(opts => opts.DataRoot = temp.Path);
        await using var provider = services.BuildServiceProvider();

        var session = provider.GetRequiredService<ITorrentSessionService>();
        await session.StartAsync();

        var ghost = TorrentId.FromInfoHash("1111".PadRight(40, '0'));
        session.GetSpeedLimits(ghost).Should().BeNull();
        (await session.SetSpeedLimitsAsync(ghost, 500_000, 50_000)).IsSuccess.Should().BeFalse();

        await session.DisposeAsync();
    }

    [Fact]
    public async Task Session_per_torrent_commands_fail_cleanly_for_unknown_ids()
    {
        using var temp = new TempDirectory();
        var services = new ServiceCollection();
        services.AddWinBitCore(opts => opts.DataRoot = temp.Path);
        await using var provider = services.BuildServiceProvider();

        var session = provider.GetRequiredService<ITorrentSessionService>();
        await session.StartAsync();

        var ghost = TorrentId.FromInfoHash("deadbeef".PadRight(40, '0'));

        (await session.PauseAsync(ghost)).IsSuccess.Should().BeFalse();
        (await session.ResumeAsync(ghost)).IsSuccess.Should().BeFalse();
        (await session.ForceRecheckAsync(ghost)).IsSuccess.Should().BeFalse();
        (await session.ForceReannounceAsync(ghost)).IsSuccess.Should().BeFalse();
        session.GetMagnetUri(ghost).Should().BeNull();
        session.GetSavePath(ghost).Should().BeNull();

        await session.DisposeAsync();
    }

    [Fact]
    public async Task Session_stats_are_zero_before_engine_starts_and_non_negative_after()
    {
        using var temp = new TempDirectory();
        var services = new ServiceCollection();
        services.AddWinBitCore(opts => opts.DataRoot = temp.Path);
        await using var provider = services.BuildServiceProvider();

        var session = provider.GetRequiredService<ITorrentSessionService>();

        var pre = session.GetSessionStats();
        pre.GlobalDownloadBps.Should().Be(0);
        pre.GlobalUploadBps.Should().Be(0);
        pre.OpenConnections.Should().Be(0);
        pre.DhtNodes.Should().Be(0);

        await session.StartAsync();
        var post = session.GetSessionStats();
        post.GlobalDownloadBps.Should().BeGreaterThanOrEqualTo(0);
        post.GlobalUploadBps.Should().BeGreaterThanOrEqualTo(0);
        post.OpenConnections.Should().BeGreaterThanOrEqualTo(0);
        post.DhtNodes.Should().BeGreaterThanOrEqualTo(0);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task Session_add_failure_returns_Result_Failure()
    {
        using var temp = new TempDirectory();

        var services = new ServiceCollection();
        services.AddWinBitCore(opts => opts.DataRoot = temp.Path);
        await using var provider = services.BuildServiceProvider();

        var session = provider.GetRequiredService<ITorrentSessionService>();
        await session.StartAsync();

        var add = await session.AddAsync(new AddTorrentParams
        {
            Source = "/does/not/exist.torrent",
            SavePath = temp.Path,
        });

        add.IsSuccess.Should().BeFalse();
        add.Error.Should().Contain("Unknown torrent source");

        await session.DisposeAsync();
    }

    [Fact]
    public async Task StatusPollingLoop_emits_snapshot_for_an_added_magnet()
    {
        using var temp = new TempDirectory();

        var services = new ServiceCollection();
        services.AddWinBitCore(opts => opts.DataRoot = temp.Path);
        await using var provider = services.BuildServiceProvider();

        var session = provider.GetRequiredService<ITorrentSessionService>();
        await session.StartAsync();

        const string magnet = "magnet:?xt=urn:btih:aabbccddeeff00112233445566778899aabbccdd&dn=winbit-poll";
        var add = await session.AddAsync(new AddTorrentParams
        {
            Source = magnet,
            SavePath = temp.Path,
            StartImmediately = false,
        });
        add.IsSuccess.Should().BeTrue(add.Error ?? string.Empty);

        TorrentSnapshot? captured = null;
        session.TorrentUpdated += (_, batch) =>
        {
            foreach (var s in batch)
            {
                if (s.Id.Value == add.Value.Value)
                {
                    captured = s;
                }
            }
        };

        var loop = provider.GetServices<IHostedService>().OfType<StatusPollingLoop>().Single();
        using var cts = new CancellationTokenSource();
        await loop.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(1500));
        await loop.StopAsync(cts.Token);
        await session.DisposeAsync();

        captured.Should().NotBeNull("polling loop must include the added torrent in at least one batch");
        captured!.Value.Id.Value.Should().Be(add.Value.Value);
    }

    [Fact]
    public async Task WinBitHostedService_starts_and_stops_the_session()
    {
        using var temp = new TempDirectory();

        var services = new ServiceCollection();
        services.AddWinBitCore(opts => opts.DataRoot = temp.Path);
        await using var provider = services.BuildServiceProvider();

        var session = provider.GetRequiredService<ITorrentSessionService>();
        var hosted = provider.GetServices<IHostedService>().OfType<WinBitHostedService>().Single();

        using var cts = new CancellationTokenSource();
        await hosted.StartAsync(cts.Token);
        session.IsRunning.Should().BeTrue("StartAsync must bring the engine up");

        await hosted.StopAsync(cts.Token);
        session.IsRunning.Should().BeFalse("StopAsync flushes and disposes the session");
    }

    [Fact]
    public async Task StatusPollingLoop_ticks_at_1_Hz_and_raises_batched_TorrentUpdated()
    {
        using var temp = new TempDirectory();

        var services = new ServiceCollection();
        services.AddWinBitCore(opts => opts.DataRoot = temp.Path);
        await using var provider = services.BuildServiceProvider();

        var session = provider.GetRequiredService<ITorrentSessionService>();
        await session.StartAsync();

        var ticks = 0;
        IReadOnlyList<TorrentSnapshot>? lastBatch = null;
        session.TorrentUpdated += (_, snapshots) =>
        {
            Interlocked.Increment(ref ticks);
            lastBatch = snapshots;
        };

        var loop = provider.GetServices<IHostedService>().OfType<StatusPollingLoop>().Single();
        using var cts = new CancellationTokenSource();
        await loop.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(2200));

        await loop.StopAsync(cts.Token);
        await session.DisposeAsync();

        ticks.Should().BeGreaterOrEqualTo(1, "PeriodicTimer at 1 Hz should fire at least once inside 2.2 s");
        lastBatch.Should().NotBeNull();
        lastBatch!.Should().BeEmpty("no torrents added yet, so each snapshot batch is empty");
    }

    [Fact]
    public async Task JsonSettingsStore_dispose_flushes_pending_write()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(Microsoft.Extensions.Options.Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));

        var store = new JsonSettingsStore(paths, TimeSpan.FromSeconds(10));
        await store.SaveAsync(new AppSettings { UiState = { Theme = "FlushOnDispose" } });

        File.Exists(paths.SettingsFile).Should().BeFalse();

        await store.DisposeAsync();

        await using var verifier = new JsonSettingsStore(paths, TimeSpan.FromMilliseconds(1));
        var reloaded = await verifier.LoadAsync();
        reloaded!.UiState.Theme.Should().Be("FlushOnDispose");
    }
}
