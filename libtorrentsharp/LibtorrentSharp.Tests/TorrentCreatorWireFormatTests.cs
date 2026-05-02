using LibtorrentSharp;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Pure unit tests on the static formatter helpers in
/// <see cref="LibtorrentSharp.TorrentCreator"/>. The wire format is a load-bearing
/// contract with the C ABI's apply_trackers parser in library.cpp; both sides
/// must agree on tier-boundary semantics or multi-tier torrents silently
/// collapse to tier 0.
/// </summary>
public sealed class TorrentCreatorWireFormatTests
{
    [Fact]
    public void FormatTrackers_emits_blank_line_between_tiers()
    {
        var tiers = new[]
        {
            new[] { "udp://t1.example/announce", "udp://t1b.example/announce" },
            new[] { "udp://t2.example/announce" },
        };

        var wire = TorrentCreator.FormatTrackers(tiers);

        Assert.Equal(
            "udp://t1.example/announce\nudp://t1b.example/announce\n\nudp://t2.example/announce",
            wire);
    }

    [Fact]
    public void FormatTrackers_returns_null_for_empty_input()
    {
        Assert.Null(TorrentCreator.FormatTrackers(System.Array.Empty<string[]>()));
    }

    [Fact]
    public void FormatTrackers_single_tier_has_no_blank_line()
    {
        var wire = TorrentCreator.FormatTrackers(new[] { new[] { "a", "b" } });
        Assert.Equal("a\nb", wire);
    }
}
