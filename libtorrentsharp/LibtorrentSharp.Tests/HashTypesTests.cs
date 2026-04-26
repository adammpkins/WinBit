using System;
using Xunit;

namespace LibtorrentSharp.Tests;

public sealed class Sha1HashTests
{
    private const string SampleHex = "dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c";

    [Fact]
    public void TryParse_round_trips_through_ToString()
    {
        Assert.True(Sha1Hash.TryParse(SampleHex, out var hash));
        Assert.Equal(SampleHex, hash.ToString());
    }

    [Fact]
    public void TryParse_is_case_insensitive_and_canonical_output_is_lowercase()
    {
        Assert.True(Sha1Hash.TryParse(SampleHex.ToUpperInvariant(), out var hash));
        Assert.Equal(SampleHex, hash.ToString());
    }

    [Fact]
    public void TryParse_rejects_non_hex_characters()
    {
        Assert.False(Sha1Hash.TryParse(("zz" + SampleHex.Substring(2)).AsSpan(), out _));
    }

    [Fact]
    public void TryParse_rejects_wrong_length()
    {
        Assert.False(Sha1Hash.TryParse("", out _));
        Assert.False(Sha1Hash.TryParse(SampleHex + "00", out _));
        Assert.False(Sha1Hash.TryParse(SampleHex.AsSpan()[..^2], out _));
    }

    [Fact]
    public void Equality_compares_by_value()
    {
        Assert.True(Sha1Hash.TryParse(SampleHex, out var a));
        Assert.True(Sha1Hash.TryParse(SampleHex, out var b));
        Assert.True(a == b);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void IsZero_returns_true_for_default()
    {
        Assert.True(default(Sha1Hash).IsZero);
    }

    [Fact]
    public void Constructor_throws_when_byte_span_is_wrong_length()
    {
        Assert.Throws<ArgumentException>(() => new Sha1Hash(new byte[19]));
        Assert.Throws<ArgumentException>(() => new Sha1Hash(new byte[21]));
    }

    [Fact]
    public void ToArray_round_trips_through_constructor()
    {
        Assert.True(Sha1Hash.TryParse(SampleHex, out var original));
        var bytes = original.ToArray();
        Assert.Equal(20, bytes.Length);
        var rebuilt = new Sha1Hash(bytes);
        Assert.Equal(original, rebuilt);
    }
}

public sealed class Sha256HashTests
{
    private const string SampleHex = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void TryParse_round_trips_through_ToString()
    {
        Assert.True(Sha256Hash.TryParse(SampleHex, out var hash));
        Assert.Equal(SampleHex, hash.ToString());
    }

    [Fact]
    public void TryParse_rejects_wrong_length()
    {
        Assert.False(Sha256Hash.TryParse("", out _));
        Assert.False(Sha256Hash.TryParse(new string('a', 40), out _));
        Assert.False(Sha256Hash.TryParse(new string('a', 65), out _));
    }

    [Fact]
    public void Equality_compares_by_value()
    {
        Assert.True(Sha256Hash.TryParse(SampleHex, out var a));
        Assert.True(Sha256Hash.TryParse(SampleHex, out var b));
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void IsZero_returns_true_for_default()
    {
        Assert.True(default(Sha256Hash).IsZero);
    }
}

public sealed class InfoHashesTests
{
    private const string V1Hex = "dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c";
    private const string V2Hex = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Constructor_requires_at_least_one_hash()
    {
        Assert.Throws<ArgumentException>(() => new InfoHashes(null, null));
    }

    [Fact]
    public void PreferredHex_returns_v1_when_present()
    {
        Assert.True(Sha1Hash.TryParse(V1Hex, out var v1));
        Assert.True(Sha256Hash.TryParse(V2Hex, out var v2));
        var hashes = new InfoHashes(v1, v2);
        Assert.Equal(V1Hex, hashes.PreferredHex);
        Assert.True(hashes.IsHybrid);
    }

    [Fact]
    public void PreferredHex_falls_back_to_v2_when_v1_is_missing()
    {
        Assert.True(Sha256Hash.TryParse(V2Hex, out var v2));
        var hashes = new InfoHashes(null, v2);
        Assert.Equal(V2Hex, hashes.PreferredHex);
        Assert.False(hashes.IsHybrid);
    }

    [Fact]
    public void FromNativeBuffers_treats_zero_buffers_as_absent()
    {
        var v1Bytes = new byte[20];
        Assert.True(Sha256Hash.TryParse(V2Hex, out var v2));
        var hashes = InfoHashes.FromNativeBuffers(v1Bytes, v2.ToArray());
        Assert.NotNull(hashes);
        Assert.Null(hashes!.Value.V1);
        Assert.Equal(v2, hashes.Value.V2);
    }

    [Fact]
    public void FromNativeBuffers_returns_null_when_both_are_zero_or_empty()
    {
        Assert.Null(InfoHashes.FromNativeBuffers(new byte[20], new byte[32]));
        Assert.Null(InfoHashes.FromNativeBuffers(Array.Empty<byte>(), Array.Empty<byte>()));
    }
}
