using FluentAssertions;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Logging;
using WinBit.Core.Notifications;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class TorrentErrorNotifierTests
{
    [Fact]
    public async Task Fires_once_on_transition_into_error_state()
    {
        var notifier = BuildNotifier(out var recorder, out var session);
        var id = TorrentId.FromInfoHash(new string('a', 40));
        session.Names[id.Value] = "ubuntu.iso";

        notifier.Absorb(new[] { Snap(id, TorrentState.Downloading) });
        notifier.Absorb(new[] { Snap(id, TorrentState.Error) });
        notifier.Absorb(new[] { Snap(id, TorrentState.Error) });

        await Task.Delay(50);

        recorder.ErrorCalls.Should().ContainSingle();
        recorder.ErrorCalls[0].Name.Should().Be("ubuntu.iso");
    }

    [Fact]
    public async Task Does_not_fire_for_torrents_already_errored_on_first_observation()
    {
        var notifier = BuildNotifier(out var recorder, out _);
        var id = TorrentId.FromInfoHash(new string('b', 40));

        // First tick sees the torrent already errored (fast-resume on startup). Must not toast.
        notifier.Absorb(new[] { Snap(id, TorrentState.Error) });
        notifier.Absorb(new[] { Snap(id, TorrentState.Error) });

        await Task.Delay(50);

        recorder.ErrorCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Forwards_snapshot_error_message_to_notification()
    {
        var notifier = BuildNotifier(out var recorder, out var session);
        var id = TorrentId.FromInfoHash(new string('d', 40));
        session.Names[id.Value] = "diskfull.iso";

        notifier.Absorb(new[] { Snap(id, TorrentState.Downloading) });
        notifier.Absorb(new[] { new TorrentSnapshot
        {
            Id = id,
            State = TorrentState.Error,
            ErrorMessage = "Disk write failure: No space left on device",
        } });

        await Task.Delay(50);

        recorder.ErrorCalls.Should().ContainSingle();
        recorder.ErrorCalls[0].Message.Should().Be("Disk write failure: No space left on device");
    }

    [Fact]
    public async Task Refires_when_torrent_recovers_and_errors_again()
    {
        var notifier = BuildNotifier(out var recorder, out _);
        var id = TorrentId.FromInfoHash(new string('c', 40));

        notifier.Absorb(new[] { Snap(id, TorrentState.Downloading) });
        notifier.Absorb(new[] { Snap(id, TorrentState.Error) });
        notifier.Absorb(new[] { Snap(id, TorrentState.Downloading) });
        notifier.Absorb(new[] { Snap(id, TorrentState.Error) });

        await Task.Delay(50);

        recorder.ErrorCalls.Should().HaveCount(2);
    }

    private static TorrentErrorNotifier BuildNotifier(
        out RecordingNotificationService recorder,
        out StubTorrentSession session)
    {
        recorder = new RecordingNotificationService();
        session = new StubTorrentSession();
        return new TorrentErrorNotifier(session, recorder, new NoopLog());
    }

    private static TorrentSnapshot Snap(TorrentId id, TorrentState state) => new()
    {
        Id = id,
        State = state,
        Progress = state == TorrentState.Error ? 0.5 : 0.6,
    };

    private sealed class RecordingNotificationService : INotificationService
    {
        public List<(string Name, string? Message)> ErrorCalls { get; } = new();

        public Task NotifyTorrentCompletedAsync(string name, string savePath, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task NotifyTorrentErrorAsync(string name, string? errorMessage, CancellationToken ct = default)
        {
            lock (ErrorCalls) ErrorCalls.Add((name, errorMessage));
            return Task.CompletedTask;
        }
    }

    private sealed class NoopLog : ILogService
    {
        public IReadOnlyList<LogEntry> GetMessages(long afterId = -1, LogSeverity filter = LogSeverity.All) => Array.Empty<LogEntry>();
        public void Write(string message, LogSeverity severity = LogSeverity.Normal) { }
        public event EventHandler<LogEntry>? MessageLogged { add { } remove { } }
    }
}
