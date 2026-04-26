using System.Linq;
using LibtorrentSharp.Enums;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Static-registry coverage for <see cref="SessionStatsMetrics"/> — the
/// name/index/type metadata surface that pairs with slice 1's
/// <see cref="LibtorrentSession.PostSessionStats"/> + counter array. Second
/// slice of the f-session-stats cluster.
/// </summary>
public sealed class SessionStatsMetricsTests
{
    [Fact]
    [Trait("Category", "Native")]
    public void All_returnsLibtorrentsBuiltinMetrics_withWellFormedEntries()
    {
        var metrics = SessionStatsMetrics.All;

        // libtorrent ships ~140 metrics in current versions — anything < 50
        // would mean the bridge is broken or libtorrent regressed dramatically.
        Assert.True(metrics.Count > 50, $"Expected > 50 metrics; got {metrics.Count}.");

        foreach (var metric in metrics)
        {
            Assert.False(string.IsNullOrEmpty(metric.Name), "metric Name must be non-empty");
            Assert.True(metric.Index >= 0, $"metric {metric.Name} has negative Index {metric.Index}");
            Assert.True(metric.Type == MetricType.Counter || metric.Type == MetricType.Gauge,
                $"metric {metric.Name} has unrecognized Type {metric.Type}");
        }

        // Indices must be unique — they're positional offsets into the same
        // counter array.
        var distinctIndices = metrics.Select(m => m.Index).Distinct().Count();
        Assert.Equal(metrics.Count, distinctIndices);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void All_isCachedAcrossCalls()
    {
        Assert.Same(SessionStatsMetrics.All, SessionStatsMetrics.All);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void FindIndex_resolvesWellKnownMetric()
    {
        // "net.recv_bytes" is documented as stable in libtorrent's headers
        // (deprecation comments reference it as the canonical replacement
        // for the legacy total_payload_download counter).
        var idx = SessionStatsMetrics.FindIndex("net.recv_bytes");
        Assert.True(idx >= 0, "net.recv_bytes should be found in libtorrent's metric registry");

        // The lookup must agree with All-iteration on the same name.
        var fromList = SessionStatsMetrics.All.Single(m => m.Name == "net.recv_bytes");
        Assert.Equal(fromList.Index, idx);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void FindIndex_returnsMinusOne_forUnknownMetric()
    {
        Assert.Equal(-1, SessionStatsMetrics.FindIndex("nonexistent.completely.fictional.metric"));
    }
}
