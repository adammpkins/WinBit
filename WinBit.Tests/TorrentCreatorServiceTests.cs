using FluentAssertions;
using WinBit.Core.BitTorrent;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

/// <summary>
/// Pinning tests for the stub <see cref="TorrentCreatorService"/>. The libtorrent binding
/// does not yet wrap libtorrent's <c>create_torrent</c> surface — full creation tests
/// return when Phase G of <c>LIBTORRENT_TASKS.md</c> ships. Until then,
/// <see cref="ITorrentCreatorService.CreateAsync"/> is contractually a fail-fast stub
/// and its callers (<see cref="TorrentCreatorQueue"/>, the WebUI endpoints, the UI page)
/// must surface the failure cleanly rather than crash.
/// </summary>
public sealed class TorrentCreatorServiceTests
{
    [Fact]
    public async Task CreateAsync_throws_NotSupportedException_until_libtorrent_creator_lands()
    {
        using var temp = new TempDirectory();
        var service = new TorrentCreatorService();

        var act = async () => await service.CreateAsync(new TorrentCreateRequest
        {
            SourcePath = temp.Path,
            OutputPath = Path.Combine(temp.Path, "out.torrent"),
        });

        await act.Should().ThrowAsync<NotSupportedException>()
            .Where(ex => ex.Message.Contains("libtorrent", StringComparison.OrdinalIgnoreCase));
    }
}
