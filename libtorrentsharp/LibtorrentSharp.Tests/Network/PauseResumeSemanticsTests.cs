using System;
using System.IO;
using LibtorrentSharp.Enums;
using Xunit;

namespace LibtorrentSharp.Tests.Network;

/// <summary>
/// c-pauseresume — verifies libtorrent's documented flag transitions for the
/// queue-aware pause/resume pair on <see cref="MagnetHandle"/>:
///
/// - <c>Pause</c>: sets paused=true AND auto_managed=true (queue may promote later).
/// - <c>Resume</c>: clears paused, sets auto_managed=true.
/// - <c>Stop</c> (the force pair, on TorrentHandle): paused=true with auto_managed
///   unchanged from prior state — distinct from Pause.
///
/// Lives under Network/ to share the trait gate with the other Phase C tests, but
/// the actual assertions are purely local — no real swarm activity needed.
/// </summary>
[Trait("Category", "Network")]
public sealed class PauseResumeSemanticsTests
{

    [Fact]
    public void Pause_then_Resume_toggle_the_queue_aware_flags()
    {
        if (!NetworkTestGate.ShouldRun())
        {
            return;
        }

        var savePath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests-PauseResume", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(savePath);

        try
        {
            using var client = new LibtorrentSession { DefaultDownloadPath = savePath };
            var magnet = client.Add(new AddTorrentParams { MagnetUri = PublicSwarmFixture.UbuntuMagnetUri, SavePath = savePath }).Magnet!;
            Assert.True(magnet.IsValid);

            // Fresh add: paused-by-default + NOT auto_managed (the C ABI's
            // attach_torrent path applies these explicitly via add_torrent_params).
            var initial = magnet.GetCurrentStatus();
            AssertPaused(initial.Flags, expected: true, "after AddMagnet");
            AssertAutoManaged(initial.Flags, expected: false, "after AddMagnet");

            // Resume (queue-aware): unset paused, set auto_managed.
            magnet.Resume();
            var afterResume = magnet.GetCurrentStatus();
            AssertPaused(afterResume.Flags, expected: false, "after Resume");
            AssertAutoManaged(afterResume.Flags, expected: true, "after Resume");

            // Pause (queue-aware): set paused AND auto_managed.
            magnet.Pause();
            var afterPause = magnet.GetCurrentStatus();
            AssertPaused(afterPause.Flags, expected: true, "after Pause");
            AssertAutoManaged(afterPause.Flags, expected: true, "after Pause");

            // Resume again — verifies the cycle is repeatable.
            magnet.Resume();
            var afterSecondResume = magnet.GetCurrentStatus();
            AssertPaused(afterSecondResume.Flags, expected: false, "after second Resume");
            AssertAutoManaged(afterSecondResume.Flags, expected: true, "after second Resume");
        }
        finally
        {
            try { Directory.Delete(savePath, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static void AssertPaused(TorrentFlags flags, bool expected, string context)
    {
        var actual = flags.HasFlag(TorrentFlags.Paused);
        Assert.True(actual == expected,
            $"paused flag mismatch {context}: expected={expected}, actual={actual} (raw flags={flags})");
    }

    private static void AssertAutoManaged(TorrentFlags flags, bool expected, string context)
    {
        var actual = flags.HasFlag(TorrentFlags.AutoManaged);
        Assert.True(actual == expected,
            $"auto_managed flag mismatch {context}: expected={expected}, actual={actual} (raw flags={flags})");
    }
}
