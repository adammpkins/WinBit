using FluentAssertions;
using WinBit.Core.BitTorrent;
using WinBit.Core.Settings;
using Xunit;

namespace WinBit.Tests;

public class LibtorrentEncryptionMapperTests
{
    [Theory]
    [InlineData(EncryptionMode.Prefer, 1)]
    [InlineData(EncryptionMode.Require, 0)]
    [InlineData(EncryptionMode.Disable, 2)]
    public void ToPolicy_maps_to_libtorrent_enc_policy(EncryptionMode mode, int expected) =>
        LibtorrentEncryptionMapper.ToPolicy(mode).Should().Be(expected);
}
