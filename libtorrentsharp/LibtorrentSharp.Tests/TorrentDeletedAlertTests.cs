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
/// Slice 9 of f-alerts-full — typed <see cref="TorrentDeletedAlert"/> /
/// <see cref="TorrentDeleteFailedAlert"/> dispatch. Tests the delete-content
/// flow end-to-end: attach a hand-built zero-length fixture, resume, detach
/// with <see cref="RemoveFlags.DeleteFiles"/>, and wait for the typed
/// success alert. <see cref="TorrentDeleteFailedAlert"/> shares dispatch
/// plumbing but runtime verification defers — engineering delete failures
/// reliably across filesystems is fragile.
/// </summary>
public sealed class TorrentDeletedAlertTests
{
    private const int PieceLength = 16384;
    private const long TotalLength = 32768 + 12;

    [Fact]
    [Trait("Category", "Native")]
    public async Task DetachWithDeleteFiles_emitsTorrentDeletedAlert()
    {
        using var session = new LibtorrentSession
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N")),
        };

        var torrentBytes = BuildMinimalTorrent();
        var torrentInfo = new TorrentInfo(torrentBytes);
        var expectedInfoHash = torrentInfo.Metadata.Hashes!.Value.V1!.Value;

        var result = session.Add(new AddTorrentParams { TorrentInfo = torrentInfo });
        Assert.True(result.IsValid);
        var handle = result.Torrent!;

        handle.Resume();
        session.DetachTorrent(handle, RemoveFlags.DeleteFiles);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var enumerator = session.Alerts.GetAsyncEnumerator(cts.Token);

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                if (enumerator.Current is TorrentDeletedAlert deletedAlert)
                {
                    Assert.Equal(expectedInfoHash, deletedAlert.InfoHash);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("TorrentDeletedAlert didn't arrive within 15s of DetachTorrent(DeleteFiles).");
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.Fail("Alert stream completed before a TorrentDeletedAlert arrived.");
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
        WriteBencString(ms, "name");   WriteBencString(ms, "delete-test");
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
