using System;
using System.Linq;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Tests for the Ed25519 helpers backing the BEP44 mutable item APIs.
/// Includes the RFC 8032 §7.1 TEST 1 known-answer vector to guard against
/// any future deviation from the standard ed25519 implementation libtorrent
/// vendors.
/// </summary>
public sealed class Ed25519Tests
{
    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", string.Empty));

    [Fact]
    [Trait("Category", "Native")]
    public void CreateSeed_returnsThirtyTwoRandomBytes()
    {
        var a = Ed25519.CreateSeed();
        var b = Ed25519.CreateSeed();
        Assert.Equal(Ed25519.SeedSize, a.Length);
        Assert.Equal(Ed25519.SeedSize, b.Length);
        Assert.NotEqual(a, b);
        Assert.Contains(a, x => x != 0);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void CreateKeypair_isDeterministicForSameSeed()
    {
        var seed = new byte[Ed25519.SeedSize];
        for (var i = 0; i < seed.Length; i++) seed[i] = (byte)i;

        var (pk1, sk1) = Ed25519.CreateKeypair(seed);
        var (pk2, sk2) = Ed25519.CreateKeypair(seed);

        Assert.Equal(Ed25519.PublicKeySize, pk1.Length);
        Assert.Equal(Ed25519.SecretKeySize, sk1.Length);
        Assert.Equal(pk1, pk2);
        Assert.Equal(sk1, sk2);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void Rfc8032_test1_matchesKnownVectors()
    {
        // RFC 8032 §7.1 TEST 1
        var seed = Hex("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        var expectedPublicKey = Hex("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        var expectedSignature = Hex("e5564300c360ac729086e2cc806e828a"
                                  + "84877f1eb8e5d974d873e06522490155"
                                  + "5fb8821590a33bacc61e39701cf9b46b"
                                  + "d25bf5f0595bbe24655141438e7a100b");

        var (publicKey, secretKey) = Ed25519.CreateKeypair(seed);
        Assert.Equal(expectedPublicKey, publicKey);

        var signature = Ed25519.Sign(Array.Empty<byte>(), publicKey, secretKey);
        Assert.Equal(expectedSignature, signature);

        Assert.True(Ed25519.Verify(signature, Array.Empty<byte>(), publicKey));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void Sign_then_Verify_roundTripsArbitraryMessage()
    {
        var (pk, sk) = Ed25519.CreateKeypair(Ed25519.CreateSeed());
        var message = System.Text.Encoding.UTF8.GetBytes("libtorrentsharp BEP44 round-trip");

        var signature = Ed25519.Sign(message, pk, sk);
        Assert.True(Ed25519.Verify(signature, message, pk));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void Verify_rejectsTamperedSignature()
    {
        var (pk, sk) = Ed25519.CreateKeypair(Ed25519.CreateSeed());
        var message = new byte[] { 1, 2, 3 };
        var signature = Ed25519.Sign(message, pk, sk);
        signature[0] ^= 0x01;
        Assert.False(Ed25519.Verify(signature, message, pk));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void Verify_rejectsWrongPublicKey()
    {
        var (pk, sk) = Ed25519.CreateKeypair(Ed25519.CreateSeed());
        var (wrongPk, _) = Ed25519.CreateKeypair(Ed25519.CreateSeed());
        var message = new byte[] { 1, 2, 3 };
        var signature = Ed25519.Sign(message, pk, sk);
        Assert.False(Ed25519.Verify(signature, message, wrongPk));
    }

    [Fact]
    public void CreateKeypair_rejectsBadSeedLength()
    {
        Assert.Throws<ArgumentNullException>(() => Ed25519.CreateKeypair(null!));
        Assert.Throws<ArgumentException>(() => Ed25519.CreateKeypair(new byte[31]));
        Assert.Throws<ArgumentException>(() => Ed25519.CreateKeypair(new byte[33]));
    }

    [Fact]
    public void Sign_rejectsBadKeyLengths()
    {
        var msg = new byte[] { 0 };
        Assert.Throws<ArgumentException>(() => Ed25519.Sign(msg, new byte[31], new byte[Ed25519.SecretKeySize]));
        Assert.Throws<ArgumentException>(() => Ed25519.Sign(msg, new byte[Ed25519.PublicKeySize], new byte[63]));
        Assert.Throws<ArgumentNullException>(() => Ed25519.Sign(null!, new byte[Ed25519.PublicKeySize], new byte[Ed25519.SecretKeySize]));
    }

    [Fact]
    public void Verify_rejectsBadInputLengths()
    {
        var msg = new byte[] { 0 };
        var pk = new byte[Ed25519.PublicKeySize];
        Assert.Throws<ArgumentException>(() => Ed25519.Verify(new byte[63], msg, pk));
        Assert.Throws<ArgumentException>(() => Ed25519.Verify(new byte[Ed25519.SignatureSize], msg, new byte[31]));
        Assert.Throws<ArgumentNullException>(() => Ed25519.Verify(null!, msg, pk));
    }
}
