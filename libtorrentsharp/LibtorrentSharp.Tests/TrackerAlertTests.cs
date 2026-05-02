using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibtorrentSharp.Alerts;
using LibtorrentSharp.Enums;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Slice 4 of f-alerts-full — typed <see cref="TrackerErrorAlert"/> /
/// <see cref="TrackerReplyAlert"/> dispatch. The fixture embeds a bogus
/// HTTP tracker URL pointing at the discard port (9); libtorrent's
/// announce against that URL reliably fails within the first retry
/// interval (DNS → connect → refused), producing a typed
/// <see cref="TrackerErrorAlert"/>. The success-case
/// <see cref="TrackerReplyAlert"/> shares the same dispatch plumbing
/// but its runtime verification defers to Phase C network tests —
/// it requires an actual reachable tracker.
/// </summary>
public sealed class TrackerAlertTests
{
    private const int PieceLength = 16384;
    private const long TotalLength = 32768 + 12;

    // Port 9 = RFC 863 discard protocol — typically refused or silently
    // dropped. Either outcome surfaces as an error on the first announce
    // attempt, which is enough to fire tracker_error_alert.
    private const string BogusTrackerUrl = "http://127.0.0.1:9/announce";

    [Fact]
    [Trait("Category", "Native")]
    public async Task InvalidTracker_emitsTrackerErrorAlert()
    {
        using var session = new LibtorrentSession
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N")),
        };

        var result = session.Add(new AddTorrentParams { TorrentInfo = new TorrentInfo(BuildTorrentWithBogusTracker()) });
        Assert.True(result.IsValid);
        var handle = result.Torrent!;

        // Resume so the torrent leaves paused-by-default state and the
        // announce actually fires.
        handle.Resume();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var enumerator = session.Alerts.GetAsyncEnumerator(cts.Token);

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                if (enumerator.Current is TrackerErrorAlert errorAlert)
                {
                    Assert.Same(handle, errorAlert.Subject);
                    Assert.Equal(BogusTrackerUrl, errorAlert.TrackerUrl);
                    Assert.NotEqual(0, errorAlert.ErrorCode);
                    // InfoHash mirrors the dispatcher-routing identifier;
                    // locks down `cs_tracker_error_alert.info_hash` marshal
                    // contract — second of the tracker-scoped sub-cluster
                    // started in slice 52.
                    var expectedHash = handle.Info.Metadata.Hashes!.Value.V1!.Value;
                    Assert.Equal(expectedHash, errorAlert.InfoHash);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("TrackerErrorAlert didn't arrive within 30s of Resume on a bogus-tracker torrent.");
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.Fail("Alert stream completed before a TrackerErrorAlert arrived.");
    }

    [Fact]
    [Trait("Category", "Native")]
    public async Task InvalidTracker_emitsTrackerAnnounceAlert()
    {
        // tracker_announce_alert fires when the announce request is sent, which
        // happens before tracker_error_alert (libtorrent always tries the request
        // even toward unreachable hosts). The Started event is guaranteed on the
        // first announce after a fresh add + Resume.
        using var session = new LibtorrentSession
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N")),
        };

        var result = session.Add(new AddTorrentParams { TorrentInfo = new TorrentInfo(BuildTorrentWithBogusTracker()) });
        Assert.True(result.IsValid);
        var handle = result.Torrent!;

        handle.Resume();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var enumerator = session.Alerts.GetAsyncEnumerator(cts.Token);

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                if (enumerator.Current is TrackerAnnounceAlert announceAlert)
                {
                    Assert.Same(handle, announceAlert.Subject);
                    Assert.Equal(BogusTrackerUrl, announceAlert.TrackerUrl);
                    // First announce after a fresh add is always event=Started.
                    Assert.Equal(AnnounceEvent.Started, announceAlert.Event);
                    // InfoHash mirrors the dispatcher-routing identifier;
                    // locks down `cs_tracker_announce_alert.info_hash`
                    // marshal contract (continues the -style
                    // InfoHash sweep, now into tracker-scoped alerts).
                    var expectedHash = handle.Info.Metadata.Hashes!.Value.V1!.Value;
                    Assert.Equal(expectedHash, announceAlert.InfoHash);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("TrackerAnnounceAlert didn't arrive within 30s of Resume.");
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.Fail("Alert stream completed before a TrackerAnnounceAlert arrived.");
    }

    private static byte[] BuildTorrentWithBogusTracker()
    {
        var numPieces = (int)((TotalLength + PieceLength - 1) / PieceLength);
        var pieces = new byte[numPieces * 20];

        using var ms = new MemoryStream();
        WriteByte(ms, 'd');
        WriteBencString(ms, "announce"); WriteBencString(ms, BogusTrackerUrl);
        WriteBencString(ms, "info");
        WriteByte(ms, 'd');
        WriteBencString(ms, "length"); WriteBencInt(ms, TotalLength);
        WriteBencString(ms, "name");   WriteBencString(ms, "tracker-test");
        WriteBencString(ms, "piece length"); WriteBencInt(ms, PieceLength);
        WriteBencString(ms, "pieces"); WriteBencBytes(ms, pieces);
        WriteByte(ms, 'e');
        WriteByte(ms, 'e');
        return ms.ToArray();
    }

    private static void WriteByte(Stream s, char c) => s.WriteByte((byte)c);

    private static void WriteBencString(Stream s, string value)
        => WriteBencBytes(s, Encoding.UTF8.GetBytes(value));

    private static void WriteBencBytes(Stream s, byte[] bytes)
    {
        var header = Encoding.ASCII.GetBytes($"{bytes.Length}:");
        s.Write(header, 0, header.Length);
        s.Write(bytes, 0, bytes.Length);
    }

    private static void WriteBencInt(Stream s, long value)
    {
        var bytes = Encoding.ASCII.GetBytes($"i{value}e");
        s.Write(bytes, 0, bytes.Length);
    }
}
