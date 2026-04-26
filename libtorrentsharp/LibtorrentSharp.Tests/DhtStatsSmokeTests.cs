using System;
using System.Threading;
using System.Threading.Tasks;
using LibtorrentSharp.Alerts;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Round-trips post_dht_stats through the native session and the async alert
/// channel: open a session, post a stats request, wait for the matching
/// <see cref="DhtStatsAlert"/>, and assert the totals are non-negative.
/// Acts as the smoke test for the Phase F dht_stats slice (totals only —
/// per-bucket detail lands alongside dht_put / dht_get item APIs).
/// </summary>
public sealed class DhtStatsSmokeTests
{
    [Fact]
    [Trait("Category", "Native")]
    public async Task PostDhtStats_FiresDhtStatsAlert_WithNonNegativeTotals()
    {
        using var session = new LibtorrentSession();

        // Spin up the iterator BEFORE PostDhtStats so the alert isn't lost between
        // the post and the first MoveNextAsync. The single-consumer Channel buffers
        // any alerts that arrive before we start reading.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var enumerator = session.Alerts.GetAsyncEnumerator(cts.Token);

        session.PostDhtStats();

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                if (enumerator.Current is DhtStatsAlert stats)
                {
                    Assert.True(stats.TotalNodes >= 0, $"TotalNodes was {stats.TotalNodes}");
                    Assert.True(stats.TotalReplacements >= 0, $"TotalReplacements was {stats.TotalReplacements}");
                    Assert.True(stats.ActiveRequests >= 0, $"ActiveRequests was {stats.ActiveRequests}");

                    Assert.NotNull(stats.Buckets);
                    var bucketSum = 0;
                    var replacementSum = 0;
                    foreach (var bucket in stats.Buckets)
                    {
                        Assert.True(bucket.NumNodes >= 0, $"bucket NumNodes was {bucket.NumNodes}");
                        Assert.True(bucket.NumReplacements >= 0, $"bucket NumReplacements was {bucket.NumReplacements}");
                        // LastActiveSeconds < 0 is the documented "never active" sentinel
                        // for empty buckets — see DhtRoutingBucket.LastActiveSeconds doc.
                        // Only assert non-negative when the bucket is actually active.
                        if (bucket.NumNodes > 0)
                        {
                            Assert.True(bucket.LastActiveSeconds >= 0, $"active bucket LastActiveSeconds was {bucket.LastActiveSeconds}");
                        }
                        bucketSum += bucket.NumNodes;
                        replacementSum += bucket.NumReplacements;
                    }
                    Assert.Equal(stats.TotalNodes, bucketSum);
                    Assert.Equal(stats.TotalReplacements, replacementSum);

                    Assert.NotNull(stats.Lookups);
                    Assert.Equal(stats.ActiveRequests, stats.Lookups.Count);
                    foreach (var lookup in stats.Lookups)
                    {
                        Assert.NotNull(lookup.Type);
                        Assert.True(lookup.OutstandingRequests >= 0);
                        Assert.True(lookup.NodesLeft >= 0);
                    }
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("DhtStatsAlert didn't arrive within 5s of PostDhtStats().");
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.Fail("Alert stream completed before a DhtStatsAlert arrived.");
    }
}
