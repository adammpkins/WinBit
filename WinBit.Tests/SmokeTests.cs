using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WinBit.Core.BitTorrent;
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
