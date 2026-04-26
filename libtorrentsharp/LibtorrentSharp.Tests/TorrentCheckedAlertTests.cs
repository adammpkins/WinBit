using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibtorrentSharp.Alerts;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Slice 2 of f-alerts-full — typed <see cref="TorrentCheckedAlert"/> dispatch.
/// The fixture's initial hash check always completes (zero-length content,
/// no peers needed) so the alert fires reliably within a few seconds without
/// a network dependency. <see cref="TorrentFinishedAlert"/> shares the same
/// dispatch pattern but requires actual download completion to trigger — its
/// runtime verification lives in the Phase C network tests.
/// </summary>
public sealed class TorrentCheckedAlertTests
{
    private const int PieceLength = 16384;
    private const long TotalLength = 32768 + 12;

    [Fact]
    [Trait("Category", "Native")]
    public async Task Attach_emitsTorrentCheckedAlert()
    {
        using var session = new LibtorrentSession
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N")),
        };

        var result = session.Add(new AddTorrentParams { TorrentInfo = new TorrentInfo(BuildMinimalTorrent()) });
        Assert.True(result.IsValid);
        var handle = result.Torrent!;

        // Attach + resume to exit the paused state, then force a recheck to
        // guarantee the state machine transitions through checking_* (the
        // initial attach can skip it for never-downloaded data).
        handle.Resume();
        handle.ForceRecheck();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var enumerator = session.Alerts.GetAsyncEnumerator(cts.Token);

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                if (enumerator.Current is TorrentCheckedAlert checkedAlert)
                {
                    Assert.Same(handle, checkedAlert.Subject);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("TorrentCheckedAlert didn't arrive within 15s of attach + resume.");
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.Fail("Alert stream completed before a TorrentCheckedAlert arrived.");
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
        WriteBencString(ms, "name");   WriteBencString(ms, "checked-test");
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
