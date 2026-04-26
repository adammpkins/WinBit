using System;
using System.Threading;
using System.Threading.Tasks;
using LibtorrentSharp.Enums;
using Xunit;

namespace LibtorrentSharp.Tests.Network;

/// <summary>
/// Phase C public-swarm integration tests. Both share <see cref="PublicSwarmFixture"/>
/// so the magnet add only happens once per test class run.
/// </summary>
[Trait("Category", "Network")]
public sealed class PublicSwarmTests : IClassFixture<PublicSwarmFixture>
{
    private readonly PublicSwarmFixture _fixture;

    public PublicSwarmTests(PublicSwarmFixture fixture) => _fixture = fixture;

    /// <summary>
    /// c-magnet — adds a well-seeded public magnet, polls until libtorrent has
    /// either resolved metadata or found at least one peer (whichever comes first),
    /// asserts the result within 60 s. Either signal proves the magnet path is
    /// reaching the swarm.
    /// </summary>
    [Fact]
    public async Task AddMagnet_resolves_peers_or_metadata_within_60s()
    {
        if (!NetworkTestGate.ShouldRun())
        {
            return;
        }

        Assert.NotNull(_fixture.Magnet);
        Assert.True(_fixture.Magnet!.IsValid);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        TorrentStatus? lastStatus = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            lastStatus = _fixture.Magnet.GetCurrentStatus();
            // Past metadata fetch OR at least one connected peer = the magnet
            // reached the swarm.
            if (lastStatus.PeerCount > 0 || lastStatus.State != TorrentState.DownloadingMetadata)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        Assert.Fail(
            $"Magnet failed to reach the swarm within 60s. Last state={lastStatus?.State}, " +
            $"peers={lastStatus?.PeerCount}, seeds={lastStatus?.SeedCount}.");
    }

    /// <summary>
    /// c-progress — picks up where c-magnet leaves off: once metadata has been
    /// fetched, polls for 30 s and asserts the torrent has actually downloaded
    /// some bytes and moved past the metadata-fetch state.
    /// </summary>
    [Fact]
    public async Task DownloadingMetadata_advances_and_bytes_flow_within_30s()
    {
        if (!NetworkTestGate.ShouldRun())
        {
            return;
        }

        Assert.NotNull(_fixture.Magnet);
        Assert.True(_fixture.Magnet!.IsValid);

        // Wait up to 60s for metadata first (idempotent with c-magnet's wait).
        var metadataDeadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (DateTimeOffset.UtcNow < metadataDeadline)
        {
            var s = _fixture.Magnet.GetCurrentStatus();
            if (s.State != TorrentState.DownloadingMetadata)
            {
                break;
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        var progressDeadline = DateTimeOffset.UtcNow.AddSeconds(30);
        TorrentStatus? lastStatus = null;
        while (DateTimeOffset.UtcNow < progressDeadline)
        {
            lastStatus = _fixture.Magnet.GetCurrentStatus();
            if (lastStatus.State != TorrentState.DownloadingMetadata && lastStatus.BytesDownloaded > 0)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        Assert.Fail(
            $"Torrent didn't make progress within 30s after metadata. Last state={lastStatus?.State}, " +
            $"bytes={lastStatus?.BytesDownloaded}, peers={lastStatus?.PeerCount}.");
    }
}
