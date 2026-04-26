using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Round-trips the immutable BEP44 put through the native session: store a known
/// blob, assert the returned target SHA-1 matches the deterministic
/// <c>SHA1(bencode(blob))</c> calculated on the managed side. The actual network
/// put is asynchronous and isn't asserted here — without bootstrapped DHT peers
/// the dht_put_alert won't fire reliably in CI.
/// Acts as the smoke test for the f-session-dht slice 3 binding.
/// </summary>
public sealed class DhtPutImmutableSmokeTests
{
    [Fact]
    [Trait("Category", "Native")]
    public void DhtPutImmutable_returnsDeterministicSha1OfBencodedString()
    {
        using var session = new LibtorrentSession();

        var payload = Encoding.ASCII.GetBytes("hello");

        var target = session.DhtPutImmutable(payload);

        // libtorrent wraps a byte[] as entry::string_t whose bencoded form is
        // "<len>:<bytes>" — for "hello" that's "5:hello". The DHT target is
        // SHA-1 of that bencoded form.
        var bencoded = Encoding.ASCII.GetBytes("5:hello");
        var expected = SHA1.HashData(bencoded);

        Assert.False(target.IsZero, "Returned target was the zero hash.");
        Assert.True(Sha1Hash.TryParse(System.Convert.ToHexString(expected), out var expectedHash));
        Assert.Equal(expectedHash, target);
    }

    [Fact]
    public void DhtPutImmutable_throwsOnNullData()
    {
        using var session = new LibtorrentSession();
        Assert.Throws<System.ArgumentNullException>(() => session.DhtPutImmutable(null!));
    }
}
