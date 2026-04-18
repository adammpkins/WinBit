using FluentAssertions;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Logging;
using WinBit.Core.Notifications;
using WinBit.Core.Settings;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class SlowDownloadNotifierTests
{
    [Fact]
    public async Task Does_not_fire_when_feature_is_disabled()
    {
        var ctx = Build(enabled: false);
        var id = TorrentId.FromInfoHash(new string('a', 40));

        ctx.Notifier.Absorb(new[] { Snap(id, rateBps: 0) });
        ctx.Clock.Advance(TimeSpan.FromHours(48));
        ctx.Notifier.Absorb(new[] { Snap(id, rateBps: 0) });

        await Task.Delay(30);
        ctx.Recorder.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Does_not_fire_before_the_minimum_age()
    {
        var ctx = Build();
        var id = TorrentId.FromInfoHash(new string('a', 40));

        ctx.Notifier.Absorb(new[] { Snap(id, rateBps: 0) });
        ctx.Clock.Advance(TimeSpan.FromMinutes(10));
        ctx.Notifier.Absorb(new[] { Snap(id, rateBps: 0) });

        await Task.Delay(30);
        ctx.Recorder.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Does_not_fire_when_rate_is_above_the_floor()
    {
        var ctx = Build();
        var id = TorrentId.FromInfoHash(new string('a', 40));

        ctx.Notifier.Absorb(new[] { Snap(id, rateBps: 50_000) });
        ctx.Clock.Advance(TimeSpan.FromHours(48));
        ctx.Notifier.Absorb(new[] { Snap(id, rateBps: 50_000) });

        await Task.Delay(30);
        ctx.Recorder.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Fires_once_after_minimum_age_and_low_rate()
    {
        var ctx = Build();
        var id = TorrentId.FromInfoHash(new string('a', 40));
        ctx.Session.Names[id.Value] = "big.iso";

        ctx.Notifier.Absorb(new[] { Snap(id, rateBps: 0) });
        ctx.Clock.Advance(TimeSpan.FromHours(25));
        ctx.Notifier.Absorb(new[] { Snap(id, rateBps: 500) });
        ctx.Notifier.Absorb(new[] { Snap(id, rateBps: 300) });

        await Task.Delay(30);
        ctx.Recorder.Calls.Should().ContainSingle();
        ctx.Recorder.Calls[0].Name.Should().Be("big.iso");
        ctx.Recorder.Calls[0].Rate.Should().Be(500);
    }

    [Fact]
    public async Task Rearms_after_torrent_leaves_downloading_and_returns()
    {
        var ctx = Build();
        var id = TorrentId.FromInfoHash(new string('a', 40));

        ctx.Notifier.Absorb(new[] { Snap(id, rateBps: 0) });
        ctx.Clock.Advance(TimeSpan.FromHours(25));
        ctx.Notifier.Absorb(new[] { Snap(id, rateBps: 0) });

        // User pauses, then resumes — the timer must restart.
        ctx.Notifier.Absorb(new[] { Snap(id, rateBps: 0, state: TorrentState.Paused) });
        ctx.Notifier.Absorb(new[] { Snap(id, rateBps: 0) });
        ctx.Clock.Advance(TimeSpan.FromMinutes(10));
        ctx.Notifier.Absorb(new[] { Snap(id, rateBps: 0) });

        await Task.Delay(30);
        // One from first cycle; none from partial second cycle (below min age).
        ctx.Recorder.Calls.Should().ContainSingle();

        ctx.Clock.Advance(TimeSpan.FromHours(25));
        ctx.Notifier.Absorb(new[] { Snap(id, rateBps: 0) });

        await Task.Delay(30);
        ctx.Recorder.Calls.Should().HaveCount(2);
    }

    private static TestContext Build(bool enabled = true)
    {
        var settings = new InMemorySettings();
        settings.Current.Behavior.SlowDownloadWarningEnabled = enabled;
        settings.Current.Behavior.SlowDownloadWarningAfterMinutes = 60 * 24;
        settings.Current.Behavior.SlowDownloadWarningRateBps = 10 * 1024;

        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var recorder = new RecordingNotificationService();
        var session = new StubTorrentSession();
        var notifier = new SlowDownloadNotifier(session, recorder, settings, new NoopLog(), clock);

        return new TestContext(notifier, recorder, session, clock, settings);
    }

    private static TorrentSnapshot Snap(TorrentId id, long rateBps, TorrentState state = TorrentState.Downloading) => new()
    {
        Id = id,
        State = state,
        Progress = 0.5,
        DownloadSpeedBps = rateBps,
    };

    private sealed record TestContext(
        SlowDownloadNotifier Notifier,
        RecordingNotificationService Recorder,
        StubTorrentSession Session,
        FakeClock Clock,
        InMemorySettings Settings);

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public List<(string Name, long Rate)> Calls { get; } = new();

        public Task NotifyTorrentCompletedAsync(string name, string savePath, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task NotifyTorrentErrorAsync(string name, string? errorMessage, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task NotifyDownloadRateLowAsync(string name, long currentRateBps, CancellationToken ct = default)
        {
            lock (Calls) Calls.Add((name, currentRateBps));
            return Task.CompletedTask;
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

    private sealed class NoopLog : ILogService
    {
        public IReadOnlyList<LogEntry> GetMessages(long afterId = -1, LogSeverity filter = LogSeverity.All) => Array.Empty<LogEntry>();
        public void Write(string message, LogSeverity severity = LogSeverity.Normal) { }
        public event EventHandler<LogEntry>? MessageLogged { add { } remove { } }
    }
}
