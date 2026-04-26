using System;
using Xunit;

namespace LibtorrentSharp.Tests;

public sealed class MagnetUriTests
{
    private const string Sha1Hex = "dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c";
    private const string Sha256Hex = "01020304050607080910111213141516171819202122232425262728293031aa";

    [Fact]
    public void Parse_validV1Magnet_returnsParamsCarryingUri()
    {
        var uri = $"magnet:?xt=urn:btih:{Sha1Hex}&dn=example";
        var parameters = MagnetUri.Parse(uri);
        Assert.Equal(uri, parameters.MagnetUri);
        Assert.Null(parameters.TorrentInfo);
        Assert.Null(parameters.ResumeData);
    }

    [Fact]
    public void Parse_validV2Magnet_returnsParamsCarryingUri()
    {
        var uri = $"magnet:?xt=urn:btih:{Sha256Hex}";
        var parameters = MagnetUri.Parse(uri);
        Assert.Equal(uri, parameters.MagnetUri);
    }

    [Fact]
    public void Parse_caseInsensitivePrefix_acceptsMixedCase()
    {
        var uri = $"MAGNET:?XT=URN:BTIH:{Sha1Hex}";
        Assert.True(MagnetUri.TryParse(uri, out var parameters));
        Assert.Equal(uri, parameters.MagnetUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-magnet")]
    [InlineData("http://example.org/torrent")]
    public void TryParse_rejectsObviouslyInvalidInput(string? input)
    {
        Assert.False(MagnetUri.TryParse(input, out _));
    }

    [Fact]
    public void TryParse_missingXtParameter_returnsFalse()
    {
        Assert.False(MagnetUri.TryParse("magnet:?dn=only-a-display-name", out _));
    }

    [Fact]
    public void TryParse_xtWithWrongLengthHash_returnsFalse()
    {
        // 39 chars — one short of SHA-1.
        Assert.False(MagnetUri.TryParse($"magnet:?xt=urn:btih:{Sha1Hex[..^1]}", out _));
        // 41 chars — one over.
        Assert.False(MagnetUri.TryParse($"magnet:?xt=urn:btih:{Sha1Hex}a", out _));
    }

    [Fact]
    public void TryParse_xtWithNonHexChars_returnsFalse()
    {
        var bad = new string('z', Sha1Hash.HexLength);
        Assert.False(MagnetUri.TryParse($"magnet:?xt=urn:btih:{bad}", out _));
    }

    [Fact]
    public void TryParse_takesFirstValidHashAndIgnoresLaterGarbage()
    {
        var uri = $"magnet:?xt=urn:btih:{Sha1Hex}&xt=urn:btih:zzzz";
        Assert.True(MagnetUri.TryParse(uri, out var parameters));
        Assert.Equal(uri, parameters.MagnetUri);
    }

    [Fact]
    public void Parse_onInvalidInput_throwsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => MagnetUri.Parse("not-a-magnet"));
    }

    [Fact]
    public void TryGetInfoHash_returnsTheParsedSha1()
    {
        var uri = $"magnet:?xt=urn:btih:{Sha1Hex}&dn=ubuntu";
        Assert.True(MagnetUri.TryGetInfoHash(uri, out var hash));
        Assert.Equal(Sha1Hex, hash.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("magnet:?dn=no-hash")]
    [InlineData("not-a-magnet")]
    public void TryGetInfoHash_returnsFalseOnAnythingMissingTheBtihHash(string? input)
    {
        Assert.False(MagnetUri.TryGetInfoHash(input, out _));
    }

    [Fact]
    public void TryGetDisplayName_returnsTheUrlDecodedDnParameter()
    {
        var uri = $"magnet:?xt=urn:btih:{Sha1Hex}&dn=Ubuntu%2024.04";
        Assert.Equal("Ubuntu 24.04", MagnetUri.TryGetDisplayName(uri));
    }

    [Fact]
    public void TryGetDisplayName_returnsNullWhenDnIsAbsent()
    {
        Assert.Null(MagnetUri.TryGetDisplayName($"magnet:?xt=urn:btih:{Sha1Hex}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-magnet")]
    [InlineData("magnet:?xt=urn:btih:" + Sha1Hex + "&dn=")]
    public void TryGetDisplayName_returnsNullForInvalidOrEmptyInput(string? input)
    {
        Assert.Null(MagnetUri.TryGetDisplayName(input));
    }
}
