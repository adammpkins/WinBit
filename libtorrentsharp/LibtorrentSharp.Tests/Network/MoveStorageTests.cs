using System;
using System.IO;
using System.Threading.Tasks;
using LibtorrentSharp.Enums;
using Xunit;

namespace LibtorrentSharp.Tests.Network;

/// <summary>
/// c-movestorage — MoveStorage relocates a torrent to a new path and the binding
/// reflects the relocation.
///
/// Scope note: the spec wants a wait for <c>storage_moved_alert</c> followed by a
/// disk-side assertion that files actually moved. The current LibtorrentSharp alert
/// surface doesn't expose <c>storage_moved_alert</c> as a typed event (it would
/// arrive only as a generic <c>Alert</c> with no path payload), and a fresh
/// magnet has no payload bytes on disk to inspect anyway. Until a typed
/// <c>StorageMovedAlert</c> ships under <c>f-alerts-full</c>, this test polls
/// <see cref="TorrentStatus.SavePath"/> as the binding-visible signal that
/// libtorrent has accepted and applied the relocation.
/// </summary>
[Trait("Category", "Network")]
public sealed class MoveStorageTests
{
    private static readonly TimeSpan SavePathPollTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task MoveStorage_updates_TorrentStatus_SavePath()
    {
        if (!NetworkTestGate.ShouldRun())
        {
            return;
        }

        var rootDir = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests-MoveStorage", Guid.NewGuid().ToString("N"));
        var originalPath = Path.Combine(rootDir, "original");
        var movedPath = Path.Combine(rootDir, "moved");
        Directory.CreateDirectory(originalPath);
        Directory.CreateDirectory(movedPath);

        try
        {
            using var client = new LibtorrentSession { DefaultDownloadPath = originalPath };
            var magnet = client.Add(new AddTorrentParams { MagnetUri = PublicSwarmFixture.UbuntuMagnetUri, SavePath = originalPath }).Magnet!;
            Assert.True(magnet.IsValid);

            var beforeMove = magnet.GetCurrentStatus();
            Assert.Equal(originalPath, beforeMove.SavePath);

            // AlwaysReplaceFiles is the safest flag for a fresh-add (no payload yet,
            // so there's nothing to clobber); the call should succeed regardless of
            // metadata-resolution status.
            magnet.MoveStorage(movedPath, MoveStorageFlags.AlwaysReplaceFiles);

            // libtorrent applies move_storage asynchronously; poll the binding's
            // SavePath until it reflects the new value or the deadline lapses.
            var deadline = DateTimeOffset.UtcNow.Add(SavePathPollTimeout);
            string? lastSeen = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                lastSeen = magnet.GetCurrentStatus().SavePath;
                if (string.Equals(lastSeen, movedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }

            Assert.Fail(
                $"SavePath did not update to '{movedPath}' within {SavePathPollTimeout.TotalSeconds:0}s. " +
                $"Last observed: '{lastSeen}'.");
        }
        finally
        {
            try { Directory.Delete(rootDir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
