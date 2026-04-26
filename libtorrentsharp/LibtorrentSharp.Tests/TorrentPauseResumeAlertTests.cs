using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibtorrentSharp.Alerts;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Slice 1 of f-alerts-full — typed <see cref="TorrentPausedAlert"/> /
/// <see cref="TorrentResumedAlert"/> dispatch. Uses a hand-built V1 torrent
/// added via <see cref="TorrentInfo"/> (not a magnet) because the pause/
/// resume dispatch path currently resolves against <c>_attachedManagers</c>
/// — magnet-only handles are a follow-up slice.
/// </summary>
public sealed class TorrentPauseResumeAlertTests
{
    private const int PieceLength = 16384;
    private const long TotalLength = 32768 + 12;

    [Fact]
    [Trait("Category", "Native")]
    public async Task ResumeThenPause_emitsResumedThenPausedAlerts()
    {
        using var session = new LibtorrentSession
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N")),
        };

        var result = session.Add(new AddTorrentParams { TorrentInfo = new TorrentInfo(BuildMinimalTorrent()) });
        Assert.True(result.IsValid);
        var handle = result.Torrent!;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var enumerator = session.Alerts.GetAsyncEnumerator(cts.Token);

        handle.Resume();

        var sawResumed = false;
        try
        {
            while (await enumerator.MoveNextAsync())
            {
                switch (enumerator.Current)
                {
                    case TorrentResumedAlert resumed when !sawResumed:
                        Assert.Same(handle, resumed.Subject);
                        sawResumed = true;
                        handle.Pause();
                        break;

                    case TorrentPausedAlert paused:
                        Assert.True(sawResumed, "Paused alert arrived before resumed alert.");
                        Assert.Same(handle, paused.Subject);
                        return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail($"Did not observe both resumed+paused alerts within 15s (sawResumed={sawResumed}).");
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.Fail("Alert stream completed before a TorrentPausedAlert arrived.");
    }

    private static byte[] BuildMinimalTorrent()
    {
        var numPieces = (int)((TotalLength + PieceLength - 1) / PieceLength);
        var pieces = new byte[numPieces * 20];

        using var ms = new MemoryStream();
        WriteByte(ms, 'd');
        WriteBencString(ms, "info");
        WriteByte(ms, 'd');
        WriteBencString(ms, "length"); WriteBencInt(ms, TotalLength);
        WriteBencString(ms, "name");   WriteBencString(ms, "pause-resume-test");
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
