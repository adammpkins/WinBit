using System;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Round-trips libtorrent's session state through the binding's save/load
/// surface (slice 1 of the f-session-state-io cluster). Captures state from
/// one session, restores into a second, and asserts the restored session is
/// usable.
/// </summary>
public sealed class SessionStateIoTests
{
    [Fact]
    [Trait("Category", "Native")]
    public void SaveState_returnsNonEmptyBlob_freshSession()
    {
        using var session = new LibtorrentSession();
        var state = session.SaveState();
        Assert.NotNull(state);
        // Even a freshly-constructed session has a settings pack + empty DHT
        // routing table to serialize — bencoded output is always > 0 bytes.
        Assert.True(state.Length > 0, $"SaveState returned empty blob (got {state.Length} bytes)");
    }

    [Fact]
    [Trait("Category", "Native")]
    public void SaveThenFromState_roundTrips_freshSession()
    {
        byte[] state;
        using (var first = new LibtorrentSession())
        {
            state = first.SaveState();
        }

        Assert.True(state.Length > 0);

        using var restored = LibtorrentSession.FromState(state);
        // Round-trip the restored session through SaveState again; the result
        // should also be a non-empty bencoded blob (libtorrent doesn't promise
        // byte-exact equality across save/load cycles since timestamps and
        // internal counters can shift).
        var restoredState = restored.SaveState();
        Assert.True(restoredState.Length > 0);

        // Restored session must be functional — exercise an alert-bearing
        // call to confirm the alert pump is wired up.
        restored.PostSessionStats();
    }

    [Fact]
    public void SaveState_throwsAfterDispose()
    {
        var session = new LibtorrentSession();
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.SaveState());
    }

    [Fact]
    public void FromState_throwsOnNullArg()
    {
        Assert.Throws<ArgumentNullException>(() => LibtorrentSession.FromState(null!));
    }

    [Fact]
    public void FromState_throwsOnEmptyArg()
    {
        Assert.Throws<ArgumentException>(() => LibtorrentSession.FromState(Array.Empty<byte>()));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void FromState_throwsOnMalformedBlob()
    {
        // Garbage bytes that aren't valid bencoding — libtorrent's
        // read_session_params should throw inside the shim, surfacing as
        // null handle and InvalidOperationException on the managed side.
        var garbage = new byte[] { 0xFF, 0xFE, 0xFD, 0xFC, 0xFB, 0xFA };
        Assert.Throws<InvalidOperationException>(() => LibtorrentSession.FromState(garbage));
    }
}
