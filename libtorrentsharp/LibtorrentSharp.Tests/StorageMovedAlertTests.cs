using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibtorrentSharp.Alerts;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Slice 3 of f-alerts-full — typed <see cref="StorageMovedAlert"/> dispatch.
/// Attaches a hand-built bencoded torrent (so the subject routes via the
/// attached-managers map), calls <see cref="TorrentHandle.MoveStorage"/> to
/// a fresh destination, and awaits the typed alert carrying the matching
/// subject + non-empty new path. <see cref="StorageMovedFailedAlert"/>
/// shares the same dispatch plumbing but its failure case is hard to
/// engineer reliably in CI — its runtime verification is deferred.
/// </summary>
public sealed class StorageMovedAlertTests
{
    private const int PieceLength = 16384;
    private const long TotalLength = 32768 + 12;

    [Fact]
    [Trait("Category", "Native")]
    public async Task MoveStorage_emitsStorageMovedAlert()
    {
        var initialPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"));
        var destinationPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", "dest-" + Guid.NewGuid().ToString("N"));

        using var session = new LibtorrentSession
        {
            DefaultDownloadPath = initialPath,
        };

        var result = session.Add(new AddTorrentParams { TorrentInfo = new TorrentInfo(BuildMinimalTorrent()) });
        Assert.True(result.IsValid);
        var handle = result.Torrent!;

        // Resume so the state machine advances past paused/auto-managed defaults
        // before the move. MoveStorage itself works on paused torrents too,
        // but resuming exercises the same path the adapter uses at runtime.
        handle.Resume();
        handle.MoveStorage(destinationPath);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var enumerator = session.Alerts.GetAsyncEnumerator(cts.Token);

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                if (enumerator.Current is StorageMovedAlert movedAlert)
                {
                    Assert.Same(handle, movedAlert.Subject);
                    Assert.False(string.IsNullOrEmpty(movedAlert.StoragePath));
                    // libtorrent normalizes paths; assert the destination
                    // leaf is present rather than comparing byte-for-byte.
                    Assert.Contains(Path.GetFileName(destinationPath), movedAlert.StoragePath);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("StorageMovedAlert didn't arrive within 15s of MoveStorage.");
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.Fail("Alert stream completed before a StorageMovedAlert arrived.");
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
        WriteBencString(ms, "name");   WriteBencString(ms, "move-storage-test");
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
