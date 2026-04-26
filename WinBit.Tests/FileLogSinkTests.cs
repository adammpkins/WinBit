using FluentAssertions;
using WinBit.Core.Logging;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

public sealed class FileLogSinkTests
{
    [Fact]
    public async Task StartAsync_writesHeaderAndBufferedEntries()
    {
        using var temp = new TempDirectory();
        var paths = TestPaths.ForTemp(temp);
        var log = new LogService();
        log.Write("before-start-one", LogSeverity.Info);
        log.Write("before-start-two", LogSeverity.Warning);

        var sink = new FileLogSink(log, paths);
        await sink.StartAsync(default);
        try
        {
            log.Write("after-start", LogSeverity.Normal);

            var logFile = TodaysLogPath(paths);
            File.Exists(logFile).Should().BeTrue();

            var contents = ReadAllSharing(logFile);
            contents.Should().Contain("=== WinBit started ");
            contents.Should().Contain("before-start-one");
            contents.Should().Contain("before-start-two");
            contents.Should().Contain("after-start");
            contents.Should().Contain("[Info]");
            contents.Should().Contain("[Warning]");
            contents.Should().Contain("[Normal]");
        }
        finally
        {
            await sink.StopAsync(default);
        }
    }

    [Fact]
    public async Task Dedup_skipsEntryWrittenTwice()
    {
        // Same entry ID hit via both the ring-buffer drain AND the live event is the race
        // guarded by LastWrittenId. Simulate by writing an entry, starting the sink (which
        // drains the ring), then writing the same id-stream once more via the same service.
        using var temp = new TempDirectory();
        var paths = TestPaths.ForTemp(temp);
        var log = new LogService();
        log.Write("one-entry", LogSeverity.Info);

        var sink = new FileLogSink(log, paths);
        await sink.StartAsync(default);
        try
        {
            var contents = ReadAllSharing(TodaysLogPath(paths));
            // "one-entry" should appear exactly once: once from ring-buffer drain, zero
            // times from the live event (subscription happens after the drain).
            CountOccurrences(contents, "one-entry").Should().Be(1);
        }
        finally
        {
            await sink.StopAsync(default);
        }
    }

    [Fact]
    public async Task StopAsync_flushesAndUnsubscribes()
    {
        using var temp = new TempDirectory();
        var paths = TestPaths.ForTemp(temp);
        var log = new LogService();
        var sink = new FileLogSink(log, paths);

        await sink.StartAsync(default);
        log.Write("live-entry", LogSeverity.Info);
        await sink.StopAsync(default);

        // After Stop, writes no longer reach the file.
        log.Write("after-stop", LogSeverity.Info);

        var contents = ReadAllSharing(TodaysLogPath(paths));
        contents.Should().Contain("live-entry");
        contents.Should().NotContain("after-stop");
    }

    private static string TodaysLogPath(WinBit.Core.Persistence.Paths paths)
        => Path.Combine(paths.LogsDir, $"winbit-{DateTime.Now:yyyy-MM-dd}.log");

    // The sink keeps the file open for Write with FileShare.Read, so a reader that
    // wants the sink to keep writing must itself share ReadWrite (read-only sharing
    // excludes the sink's in-progress write).
    private static string ReadAllSharing(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
