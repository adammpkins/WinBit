using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibtorrentSharp.Alerts;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Slice 5 of f-alerts-full — typed <see cref="ScrapeFailedAlert"/> /
/// <see cref="ScrapeReplyAlert"/> dispatch. The fixture embeds a bogus
/// HTTP tracker URL pointing at the discard port (9); calling
/// <see cref="TorrentHandle.ScrapeTracker"/> against that URL reliably
/// fails within the first retry interval (DNS → connect → refused),
/// producing a typed <see cref="ScrapeFailedAlert"/>. The success-case
/// <see cref="ScrapeReplyAlert"/> shares the same dispatch plumbing
/// but its runtime verification defers to Phase C network tests —
/// it requires an actual reachable tracker.
/// </summary>
public sealed class ScrapeAlertTests
{
    private const int PieceLength = 16384;
    private const long TotalLength = 32768 + 12;

    // Port 9 = RFC 863 discard protocol — typically refused or silently
    // dropped. Either outcome surfaces as an error on the first scrape
    // attempt, which is enough to fire scrape_failed_alert.
    private const string BogusTrackerUrl = "http://127.0.0.1:9/announce";

    [Fact]
    [Trait("Category", "Native")]
    public async Task ScrapeBogusTracker_emitsScrapeFailedAlert()
    {
        using var session = new LibtorrentSession
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N")),
        };

        var result = session.Add(new AddTorrentParams { TorrentInfo = new TorrentInfo(BuildTorrentWithBogusTracker()) });
        Assert.True(result.IsValid);
        var handle = result.Torrent!;

        // Resume so the torrent leaves paused-by-default state and the
        // scrape request can actually fire.
        handle.Resume();
        handle.ScrapeTracker();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var enumerator = session.Alerts.GetAsyncEnumerator(cts.Token);

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                if (enumerator.Current is ScrapeFailedAlert failedAlert)
                {
                    Assert.Same(handle, failedAlert.Subject);
                    Assert.Equal(BogusTrackerUrl, failedAlert.TrackerUrl);
                    Assert.NotEqual(0, failedAlert.ErrorCode);
                    // InfoHash mirrors the dispatcher-routing identifier;
                    // locks down `cs_scrape_failed_alert.info_hash` marshal
                    // contract — fourth in the tracker-scoped sub-cluster.
                    var expectedHash = handle.Info.Metadata.Hashes!.Value.V1!.Value;
                    Assert.Equal(expectedHash, failedAlert.InfoHash);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("ScrapeFailedAlert didn't arrive within 30s of ScrapeTracker on a bogus-tracker torrent.");
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.Fail("Alert stream completed before a ScrapeFailedAlert arrived.");
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
        WriteBencString(ms, "name");   WriteBencString(ms, "scrape-test");
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
