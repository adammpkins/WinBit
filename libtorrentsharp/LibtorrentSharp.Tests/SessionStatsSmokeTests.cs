using System;
using System.Threading;
using System.Threading.Tasks;
using LibtorrentSharp.Alerts;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Round-trips post_session_stats through the native session and the async
/// alert channel: open a session, post a stats request, wait for the matching
/// <see cref="SessionStatsAlert"/>, and assert the counter array is non-empty.
/// First slice of the f-session-stats cluster — the per-metric name/index
/// mapping (session_stats_metrics) lands in a follow-up slice.
/// </summary>
public sealed class SessionStatsSmokeTests
{
    [Fact]
    [Trait("Category", "Native")]
    public async Task PostSessionStats_FiresSessionStatsAlert_WithNonEmptyCounters()
    {
        using var session = new LibtorrentSession();

        // Spin up the iterator BEFORE PostSessionStats so the alert isn't lost
        // between the post and the first MoveNextAsync. Bounded channel buffers
        // any alerts that arrive before iteration starts.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var enumerator = session.Alerts.GetAsyncEnumerator(cts.Token);

        session.PostSessionStats();

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                if (enumerator.Current is SessionStatsAlert stats)
                {
                    Assert.NotNull(stats.Counters);
                    // libtorrent ships ~140 counters; a freshly-built session
                    // surfaces all of them, so a positive count proves the
                    // dispatcher copy worked end-to-end.
                    Assert.True(stats.Counters.Length > 0,
                        $"Counters was empty (expected ~140 metrics from a freshly-built session).");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("SessionStatsAlert didn't arrive within 5s of PostSessionStats().");
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.Fail("Alert stream completed before a SessionStatsAlert arrived.");
    }

    [Fact]
    public void PostSessionStats_ThrowsAfterDispose()
    {
        var session = new LibtorrentSession();
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.PostSessionStats());
    }
}
