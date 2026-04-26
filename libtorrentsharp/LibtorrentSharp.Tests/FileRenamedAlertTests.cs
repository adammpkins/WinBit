using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibtorrentSharp.Alerts;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Slice 7 of f-alerts-full — typed <see cref="FileRenamedAlert"/> /
/// <see cref="FileRenameFailedAlert"/> dispatch. Closes the loop on the
/// <see cref="TorrentHandle.RenameFile"/> binding shipped in
/// <c>f-handle-peers</c>: callers now get a typed success/failure notification.
/// The success case is deterministic (calling rename_file on a fresh zero-length
/// torrent always fires file_renamed_alert — libtorrent updates its internal
/// mapping synchronously when storage hasn't been materialized yet). The
/// failure case shares the dispatch plumbing but runtime verification defers —
/// engineering rename failures reliably across filesystems is flaky.
/// </summary>
public sealed class FileRenamedAlertTests
{
    private const int PieceLength = 16384;
    private const long TotalLength = 32768 + 12;

    [Fact]
    [Trait("Category", "Native")]
    public async Task RenameFile_emitsFileRenamedAlert()
    {
        using var session = new LibtorrentSession
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N")),
        };

        var result = session.Add(new AddTorrentParams { TorrentInfo = new TorrentInfo(BuildMinimalTorrent()) });
        Assert.True(result.IsValid);
        var handle = result.Torrent!;

        handle.Resume();
        handle.RenameFile(0, "renamed.bin");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var enumerator = session.Alerts.GetAsyncEnumerator(cts.Token);

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                if (enumerator.Current is FileRenamedAlert renamedAlert)
                {
                    Assert.Same(handle, renamedAlert.Subject);
                    Assert.Equal(0, renamedAlert.FileIndex);
                    Assert.Equal("renamed.bin", renamedAlert.NewName);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("FileRenamedAlert didn't arrive within 15s of RenameFile.");
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.Fail("Alert stream completed before a FileRenamedAlert arrived.");
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
        WriteBencString(ms, "name");   WriteBencString(ms, "rename-test");
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
