using System;
using System.Text;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Smoke tests for the mutable BEP44 lookup binding. Verify the synchronous
/// dispatch path (no exceptions) and argument validation. The full network
/// round-trip — write a mutable item then read it — needs an Ed25519 keypair
/// + signing infrastructure that lands in a follow-up slice; for now we just
/// confirm the call queues a lookup without throwing.
/// </summary>
public sealed class DhtMutableGetSmokeTests
{
    [Fact]
    [Trait("Category", "Native")]
    public void DhtGetItemMutable_dispatchesWithoutThrowing_forArbitraryKey()
    {
        using var session = new LibtorrentSession();

        var key = new byte[32];
        new Random(1).NextBytes(key);

        // No-salt overload (default null).
        session.DhtGetItemMutable(key);

        // With salt.
        session.DhtGetItemMutable(key, Encoding.ASCII.GetBytes("ns"));
    }

    [Fact]
    public void DhtGetItemMutable_throwsOnNullKey()
    {
        using var session = new LibtorrentSession();
        Assert.Throws<ArgumentNullException>(() => session.DhtGetItemMutable(null!));
    }

    [Fact]
    public void DhtGetItemMutable_throwsOnWrongKeyLength()
    {
        using var session = new LibtorrentSession();
        Assert.Throws<ArgumentException>(() => session.DhtGetItemMutable(new byte[31]));
        Assert.Throws<ArgumentException>(() => session.DhtGetItemMutable(new byte[33]));
    }

    [Fact]
    public void DhtGetItemMutable_throwsAfterDispose()
    {
        var session = new LibtorrentSession();
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.DhtGetItemMutable(new byte[32]));
    }
}
