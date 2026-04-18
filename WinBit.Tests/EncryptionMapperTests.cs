using FluentAssertions;
using MonoTorrent.Connections;
using WinBit.Core.BitTorrent;
using WinBit.Core.Settings;
using Xunit;

namespace WinBit.Tests;

public sealed class EncryptionMapperTests
{
    [Fact]
    public void Prefer_allows_all_three_types_with_RC4_ranked_first()
    {
        var allowed = EncryptionMapper.ToMonoTorrent(EncryptionMode.Prefer);

        allowed.Should().HaveCount(3);
        allowed.Should().Contain(EncryptionType.RC4Full);
        allowed.Should().Contain(EncryptionType.RC4Header);
        allowed.Should().Contain(EncryptionType.PlainText);
        allowed[0].Should().NotBe(EncryptionType.PlainText, "encrypted handshakes should be preferred");
    }

    [Fact]
    public void Require_excludes_plain_text()
    {
        var allowed = EncryptionMapper.ToMonoTorrent(EncryptionMode.Require);

        allowed.Should().NotContain(EncryptionType.PlainText);
        allowed.Should().Contain(EncryptionType.RC4Full);
        allowed.Should().Contain(EncryptionType.RC4Header);
    }

    [Fact]
    public void Disable_allows_plain_text_only()
    {
        var allowed = EncryptionMapper.ToMonoTorrent(EncryptionMode.Disable);

        allowed.Should().ContainSingle()
            .Which.Should().Be(EncryptionType.PlainText);
    }
}
