using System;
using System.Text;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Smoke tests for the mutable BEP44 put binding. Verify the synchronous
/// dispatch path (no exceptions) and argument validation. The full network
/// round-trip — write a mutable item then read it back — needs bootstrapped
/// DHT peers and lands in a follow-up Network-category integration test;
/// here we just confirm the call queues the put without throwing.
/// </summary>
public sealed class DhtMutablePutSmokeTests
{
    [Fact]
    [Trait("Category", "Native")]
    public void DhtPutItemMutable_dispatchesWithoutThrowing_withRealKeypair()
    {
        using var session = new LibtorrentSession();

        var (pk, sk) = Ed25519.CreateKeypair(Ed25519.CreateSeed());
        var value = Encoding.UTF8.GetBytes("hello mutable world");

        // No-salt overload (default null).
        session.DhtPutItemMutable(pk, sk, value, seq: 1);

        // With salt.
        session.DhtPutItemMutable(pk, sk, value, seq: 2, salt: Encoding.ASCII.GetBytes("ns"));
    }

    [Fact]
    public void DhtPutItemMutable_throwsOnNullArgs()
    {
        using var session = new LibtorrentSession();
        var pk = new byte[Ed25519.PublicKeySize];
        var sk = new byte[Ed25519.SecretKeySize];
        var value = new byte[] { 0 };

        Assert.Throws<ArgumentNullException>(() => session.DhtPutItemMutable(null!, sk, value, 1));
        Assert.Throws<ArgumentNullException>(() => session.DhtPutItemMutable(pk, null!, value, 1));
        Assert.Throws<ArgumentNullException>(() => session.DhtPutItemMutable(pk, sk, null!, 1));
    }

    [Fact]
    public void DhtPutItemMutable_throwsOnWrongKeyLengths()
    {
        using var session = new LibtorrentSession();
        var sk = new byte[Ed25519.SecretKeySize];
        var pk = new byte[Ed25519.PublicKeySize];
        var value = new byte[] { 0 };

        Assert.Throws<ArgumentException>(() => session.DhtPutItemMutable(new byte[31], sk, value, 1));
        Assert.Throws<ArgumentException>(() => session.DhtPutItemMutable(pk, new byte[63], value, 1));
    }

    [Fact]
    public void DhtPutItemMutable_throwsAfterDispose()
    {
        var session = new LibtorrentSession();
        var pk = new byte[Ed25519.PublicKeySize];
        var sk = new byte[Ed25519.SecretKeySize];
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.DhtPutItemMutable(pk, sk, new byte[] { 0 }, 1));
    }
}
