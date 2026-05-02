using System;
using System.Threading;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Smoke coverage for the listen-state surface: `ListenPort`,
/// `SslListenPort`, `IsListening`, and `SetListenInterfaces`. Alert-driven
/// tracking (listen_succeeded / listen_failed alert types) defers to a
/// follow-up slice.
/// </summary>
public sealed class SessionListenTests
{
    [Fact]
    [Trait("Category", "Native")]
    public void FreshSession_bindsToDefaultInterface()
    {
        using var session = new LibtorrentSession();

        // Listen socket bringup is async inside libtorrent — constructor
        // returns before the listener is fully up, so poll briefly.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (session.IsListening && session.ListenPort > 0)
            {
                break;
            }
            Thread.Sleep(50);
        }

        Assert.True(session.IsListening, "session should have at least one open listen socket");
        Assert.True(session.ListenPort > 0, $"ListenPort was {session.ListenPort}");
    }

    [Fact]
    [Trait("Category", "Native")]
    public void SslListenPort_isZero_whenSslNotConfigured()
    {
        using var session = new LibtorrentSession();
        // Default config doesn't enable SSL — expect 0.
        Assert.Equal(0, session.SslListenPort);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void SetListenInterfaces_acceptsLocalhostWildcard()
    {
        using var session = new LibtorrentSession();

        // Ask libtorrent to rebind to an OS-assigned port (port 0 = let OS pick)
        // on loopback only. The rebind is asynchronous.
        session.SetListenInterfaces("127.0.0.1:0");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (session.IsListening && session.ListenPort > 0)
            {
                break;
            }
            Thread.Sleep(50);
        }

        Assert.True(session.IsListening);
        Assert.True(session.ListenPort > 0);
    }

    [Fact]
    public void ListenPort_throwsAfterDispose()
    {
        var session = new LibtorrentSession();
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = session.ListenPort);
    }

    [Fact]
    public void SetListenInterfaces_throwsOnNull()
    {
        using var session = new LibtorrentSession();
        Assert.Throws<ArgumentNullException>(() => session.SetListenInterfaces(null!));
    }

    [Fact]
    public void SetListenInterfaces_throwsAfterDispose()
    {
        var session = new LibtorrentSession();
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.SetListenInterfaces("127.0.0.1:0"));
    }
}
