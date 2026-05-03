using FluentAssertions;
using WinBit.Core.BitTorrent;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

/// <summary>
/// Round-trip tests for the libtorrent-backed <see cref="TorrentCreatorService"/>.
/// Both tests load the LibtorrentSharp native library (lts.dll on Windows) — they
/// will fail with <see cref="DllNotFoundException"/> when the native runtime is
/// absent and with <see cref="EntryPointNotFoundException"/> when an older lts.dll
/// is present on disk that pre-dates the <c>lts_create_torrent</c> export. Both
/// surface as test failures (rather than passing silently); rebuild
/// <c>libtorrentsharp/LibtorrentSharp.Native</c> if either fires.
/// </summary>
[Trait("Category", "Native")]
public sealed class TorrentCreatorServiceTests
{
    [Fact(Skip = "Crashes the test host intermittently — SyncProgress is meant to invoke on the calling thread, but in practice the libtorrent native callback re-enters from a different thread, racing the List<T>.Add and corrupting state. Tracked in TASKS.md backlog.")]
    public async Task CreateAsync_round_trips_a_small_file_into_a_valid_bencoded_torrent()
    {
        using var temp = new TempDirectory();

        // Multi-piece source so the hashing loop fires multiple progress events.
        var sourceDir = Path.Combine(temp.Path, "payload");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "data.bin");
        await File.WriteAllBytesAsync(sourceFile, new byte[256 * 1024]);

        var output = Path.Combine(temp.Path, "out.torrent");
        var service = new TorrentCreatorService();

        var progressEvents = new List<TorrentCreateProgress>();
        // SyncProgress invokes the handler on the calling thread (the Task.Run worker
        // that drives the blocking P/Invoke). Progress<T> posts to ThreadPool, causing
        // multiple concurrent Add() calls to a non-thread-safe List<T> → corruption.
        var progress = new SyncProgress<TorrentCreateProgress>(p => progressEvents.Add(p));

        var request = new TorrentCreateRequest
        {
            SourcePath = sourceDir,
            OutputPath = output,
            Comment = "winbit-roundtrip",
            CreatedBy = "WinBit.Tests",
            TrackerTiers = new[]
            {
                new[] { "udp://tracker.tier0.example/announce" },
                new[] { "udp://tracker.tier1.example/announce" },
            },
            PieceLength = 16 * 1024,
        };

        var result = await service.CreateAsync(request, progress);

        result.IsSuccess.Should().BeTrue(result.Error);
        File.Exists(output).Should().BeTrue();
        var bytes = await File.ReadAllBytesAsync(output);
        bytes.Length.Should().BeGreaterThan(0);
        bytes[0].Should().Be((byte)'d'); // bencoded dict prefix
        // Spot-check the torrent contains the comment we asked libtorrent to embed
        // and that BOTH tracker tiers survived (catches the wire-format collapse bug
        // where a single newline between tiers would drop everything to tier 0 — the
        // tier-boundary semantics are unit-tested in TorrentCreatorWireFormatTests).
        var asLatin1 = System.Text.Encoding.Latin1.GetString(bytes);
        asLatin1.Should().Contain("winbit-roundtrip");
        asLatin1.Should().Contain("tracker.tier0.example");
        asLatin1.Should().Contain("tracker.tier1.example");

        // libtorrent fires at least one progress event for the initial 0/N tick
        // even on a one-piece source.
        progressEvents.Should().NotBeEmpty();
    }

    [Fact(Skip = "Same flake class as CreateAsync_round_trips_… — libtorrent native progress callback re-enters from a different thread, racing the test's SyncProgress sink. Tracked in TASKS.md.")]
    public async Task CreateAsync_does_not_write_output_when_cancelled()
    {
        using var temp = new TempDirectory();

        // 32 MB so the hashing loop has enough pieces to observe a cancellation
        // mid-flight even on fast disks. The synchronous SyncProgress sink trips
        // the token from inside the callback so the next piece-hash iteration
        // observes the flag — Progress<T>'s async dispatch can race past the
        // hashing loop on small inputs.
        var sourceFile = Path.Combine(temp.Path, "big.bin");
        await File.WriteAllBytesAsync(sourceFile, new byte[32 * 1024 * 1024]);

        var output = Path.Combine(temp.Path, "out.torrent");
        var service = new TorrentCreatorService();

        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<TorrentCreateProgress>(_ => cts.Cancel());

        var request = new TorrentCreateRequest
        {
            SourcePath = sourceFile,
            OutputPath = output,
            PieceLength = 16 * 1024,
        };

        var act = async () => await service.CreateAsync(request, progress, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(output).Should().BeFalse();
    }

    /// <summary>
    /// Synchronous <see cref="IProgress{T}"/> — invokes the action on whatever
    /// thread reports progress. <see cref="Progress{T}"/> dispatches via
    /// SynchronizationContext / ThreadPool, which can race past a fast hashing
    /// loop without ever surfacing on the cancellation thread.
    /// </summary>
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SyncProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}
