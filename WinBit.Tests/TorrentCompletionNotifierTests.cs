using FluentAssertions;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Logging;
using WinBit.Core.Notifications;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class TorrentCompletionNotifierTests
{
    [Fact]
    public async Task Fires_once_when_progress_crosses_from_under_one_to_one()
    {
        var notifier = BuildNotifier(out var recorder, out var session, name: "ubuntu.iso", savePath: @"D:\Downloads");

        var id = TorrentId.FromInfoHash(new string('a', 40));
        notifier.Absorb(new[] { Snap(id, 0.5) });
        notifier.Absorb(new[] { Snap(id, 0.9) });
        notifier.Absorb(new[] { Snap(id, 1.0) });

        // Flush fire-and-forget task.
        await Task.Delay(50);

        recorder.Calls.Should().ContainSingle();
        recorder.Calls[0].Name.Should().Be("ubuntu.iso");
        recorder.Calls[0].SavePath.Should().Be(@"D:\Downloads");
    }

    [Fact]
    public async Task Does_not_fire_on_subsequent_ticks_after_completion()
    {
        var notifier = BuildNotifier(out var recorder, out _);

        var id = TorrentId.FromInfoHash(new string('b', 40));
        notifier.Absorb(new[] { Snap(id, 0.5) });
        notifier.Absorb(new[] { Snap(id, 1.0) });
        notifier.Absorb(new[] { Snap(id, 1.0) });
        notifier.Absorb(new[] { Snap(id, 1.0) });

        await Task.Delay(50);

        recorder.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Does_not_fire_for_torrents_already_complete_on_first_observation()
    {
        var notifier = BuildNotifier(out var recorder, out _);

        var id = TorrentId.FromInfoHash(new string('c', 40));
        // First tick sees the torrent already at 1.0 — this is the fast-resume / restart case.
        notifier.Absorb(new[] { Snap(id, 1.0) });
        notifier.Absorb(new[] { Snap(id, 1.0) });

        await Task.Delay(50);

        recorder.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Tracks_multiple_torrents_independently()
    {
        var notifier = BuildNotifier(out var recorder, out var session);
        var a = TorrentId.FromInfoHash(new string('a', 40));
        var b = TorrentId.FromInfoHash(new string('b', 40));
        session.Names[a.Value] = "alpha";
        session.Names[b.Value] = "beta";

        notifier.Absorb(new[] { Snap(a, 0.5), Snap(b, 0.2) });
        notifier.Absorb(new[] { Snap(a, 1.0), Snap(b, 0.8) });
        notifier.Absorb(new[] { Snap(a, 1.0), Snap(b, 1.0) });

        await Task.Delay(50);

        recorder.Calls.Select(c => c.Name).Should().BeEquivalentTo(new[] { "alpha", "beta" });
    }

    private static TorrentCompletionNotifier BuildNotifier(
        out RecordingNotificationService recorder,
        out StubTorrentSession session,
        string? name = null,
        string? savePath = null)
    {
        recorder = new RecordingNotificationService();
        session = new StubTorrentSession();
        if (name is not null)
        {
            session.Names[new string('a', 40)] = name;
        }
        if (savePath is not null)
        {
            session.SavePaths[new string('a', 40)] = savePath;
        }
        return new TorrentCompletionNotifier(session, recorder, new NoopLog());
    }

    private static TorrentSnapshot Snap(TorrentId id, double progress) => new()
    {
        Id = id,
        Progress = progress,
        State = progress >= 1.0 ? TorrentState.Seeding : TorrentState.Downloading,
    };

    private sealed class RecordingNotificationService : INotificationService
    {
        public List<(string Name, string SavePath)> Calls { get; } = new();

        public Task NotifyTorrentCompletedAsync(string name, string savePath, CancellationToken ct = default)
        {
            lock (Calls) Calls.Add((name, savePath));
            return Task.CompletedTask;
        }

        public Task NotifyTorrentErrorAsync(string name, string? errorMessage, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopLog : ILogService
    {
        public IReadOnlyList<LogEntry> GetMessages(long afterId = -1, LogSeverity filter = LogSeverity.All) => Array.Empty<LogEntry>();
        public void Write(string message, LogSeverity severity = LogSeverity.Normal) { }
        public event EventHandler<LogEntry>? MessageLogged { add { } remove { } }
    }
}
