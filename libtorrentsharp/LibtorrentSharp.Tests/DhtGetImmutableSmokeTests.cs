using System;
using System.Text;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Smoke tests for the immutable BEP44 lookup binding. Verify the synchronous
/// dispatch path (no exceptions) and argument validation. The full network
/// round-trip — put then get with a real DHT — lands as a Network-category
/// integration test in a follow-up; without bootstrapped DHT peers neither
/// the dht_put_alert nor the dht_immutable_item_alert fires reliably in CI.
/// </summary>
public sealed class DhtGetImmutableSmokeTests
{
    [Fact]
    [Trait("Category", "Native")]
    public void DhtGetImmutable_dispatchesWithoutThrowing_forArbitraryTarget()
    {
        using var session = new LibtorrentSession();

        // Use the deterministic put-target for "hello" (5:hello bencoded) so
        // the lookup is well-formed even if no peer ever responds.
        var payload = Encoding.ASCII.GetBytes("hello");
        var target = session.DhtPutImmutable(payload);

        // The call queues the lookup and returns immediately. Result (if any)
        // arrives later as DhtImmutableItemAlert on the Alerts stream — not
        // asserted here.
        session.DhtGetImmutable(target);
    }

    [Fact]
    public void DhtGetImmutable_throwsAfterDispose()
    {
        var session = new LibtorrentSession();
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.DhtGetImmutable(default));
    }
}
