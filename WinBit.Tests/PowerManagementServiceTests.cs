using FluentAssertions;
using WinBit.Core.BitTorrent;
using WinBit.Core.Power;
using WinBit.Core.Settings;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class PowerManagementServiceTests
{
    [Fact]
    public void Inhibits_when_any_torrent_has_nonzero_rate()
    {
        var ctx = Build();
        ctx.Service.Absorb(new[] { Snap(rateBps: 0), Snap(rateBps: 12_000) });
        ctx.Inhibitor.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Releases_when_all_torrents_are_idle()
    {
        var ctx = Build();
        ctx.Service.Absorb(new[] { Snap(rateBps: 50_000) });
        ctx.Inhibitor.IsActive.Should().BeTrue();

        ctx.Service.Absorb(new[] { Snap(rateBps: 0), Snap(rateBps: 0) });
        ctx.Inhibitor.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Does_not_inhibit_when_setting_is_disabled()
    {
        var ctx = Build(enabled: false);
        ctx.Service.Absorb(new[] { Snap(rateBps: 999_999) });
        ctx.Inhibitor.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Disabling_setting_mid_session_releases_immediately()
    {
        var ctx = Build();
        await ctx.Service.StartAsync(CancellationToken.None);

        ctx.Service.Absorb(new[] { Snap(rateBps: 50_000) });
        ctx.Inhibitor.IsActive.Should().BeTrue();

        await ctx.Settings.UpdateAsync(s => s.Behavior.PreventSleepWhileActive = false);
        ctx.Inhibitor.IsActive.Should().BeFalse();

        await ctx.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Stopping_releases_the_block()
    {
        var ctx = Build();
        await ctx.Service.StartAsync(CancellationToken.None);
        ctx.Service.Absorb(new[] { Snap(rateBps: 50_000) });
        ctx.Inhibitor.IsActive.Should().BeTrue();

        await ctx.Service.StopAsync(CancellationToken.None);
        ctx.Inhibitor.IsActive.Should().BeFalse();
    }

    private static TestContext Build(bool enabled = true)
    {
        var settings = new InMemorySettings();
        settings.Current.Behavior.PreventSleepWhileActive = enabled;
        var session = new StubTorrentSession();
        var inhibitor = new FakeInhibitor();
        var service = new PowerManagementService(session, settings, inhibitor);
        return new TestContext(service, inhibitor, session, settings);
    }

    private static TorrentSnapshot Snap(long rateBps) => new()
    {
        Id = Core.Common.TorrentId.FromInfoHash(new string('a', 40)),
        State = TorrentState.Downloading,
        DownloadSpeedBps = rateBps,
    };

    private sealed record TestContext(
        PowerManagementService Service,
        FakeInhibitor Inhibitor,
        StubTorrentSession Session,
        InMemorySettings Settings);

    private sealed class FakeInhibitor : ISleepInhibitor
    {
        public bool IsActive { get; private set; }
        public int Calls { get; private set; }
        public void SetActive(bool active)
        {
            Calls++;
            IsActive = active;
        }
    }

    private sealed class InMemorySettings : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(Current);
        public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Action<AppSettings> mutate, CancellationToken ct = default)
        {
            mutate(Current);
            Changed?.Invoke(this, Current);
            return Task.CompletedTask;
        }
        public event EventHandler<AppSettings>? Changed;
    }
}
