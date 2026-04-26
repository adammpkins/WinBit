using System;
using System.IO;
using System.Net;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Exercises the f-handle-peers surface: <c>ConnectPeer</c>, <c>ClearError</c>,
/// <c>RenameFile</c>. Uses magnet handles (pre-metadata) plus intentionally
/// invalid handles for negative-path coverage. Rename / connect fire async
/// alerts — we only verify the native side accepts the call without crashing
/// here; alert round-trip is a separate concern for the full f-alerts-full
/// cluster.
/// </summary>
public sealed class HandlePeersSmokeTests
{
    private const string ValidMagnetUri = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=ubuntu-14.04.1-desktop-amd64.iso";

    [Fact]
    [Trait("Category", "Native")]
    public void ConnectPeer_OnValidMagnet_QueuesConnect()
    {
        using var session = NewSession();
        var handle = AddMagnet(session);

        // IPv4 via v4-mapped v6. Non-routable RFC 5737 documentation address
        // — libtorrent doesn't actually connect, but the connect-peer queue
        // must accept the endpoint.
        Assert.True(handle.ConnectPeer(IPAddress.Parse("192.0.2.1"), 6881));

        // Pure IPv6 loopback to cover the v6 path.
        Assert.True(handle.ConnectPeer(IPAddress.IPv6Loopback, 6882));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void ConnectPeer_OnInvalidMagnet_ReturnsFalse()
    {
        using var session = NewSession();
        var handle = session.Add(new AddTorrentParams { MagnetUri = "not-a-magnet" }).Magnet!;
        Assert.False(handle.IsValid);

        Assert.False(handle.ConnectPeer(IPAddress.Loopback, 6881));
    }

    [Fact]
    public void ConnectPeer_RejectsNullAddressAndBadPort()
    {
        using var session = NewSession();
        var handle = AddMagnet(session);

        Assert.Throws<ArgumentNullException>(() => handle.ConnectPeer(null!, 6881));
        Assert.Throws<ArgumentOutOfRangeException>(() => handle.ConnectPeer(IPAddress.Loopback, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => handle.ConnectPeer(IPAddress.Loopback, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => handle.ConnectPeer(IPAddress.Loopback, 70_000));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void ClearError_OnValidAndInvalidHandle_IsSafeNoOp()
    {
        using var session = NewSession();
        var valid = AddMagnet(session);
        var invalid = session.Add(new AddTorrentParams { MagnetUri = "not-a-magnet" }).Magnet!;

        // No sticky error yet; must still be a safe no-op.
        valid.ClearError();
        invalid.ClearError();
    }

    [Fact]
    [Trait("Category", "Native")]
    public void RenameFile_PreMetadata_IsNoOp()
    {
        using var session = NewSession();
        var handle = AddMagnet(session);

        // Metadata hasn't resolved yet; native side short-circuits.
        handle.RenameFile(0, "renamed.iso");
        handle.RenameFile(999_999, "subdir/renamed.iso");
    }

    [Fact]
    [Trait("Category", "Native")]
    public void RenameFile_OnInvalidHandle_IsNoOp()
    {
        using var session = NewSession();
        var handle = session.Add(new AddTorrentParams { MagnetUri = "not-a-magnet" }).Magnet!;
        Assert.False(handle.IsValid);

        handle.RenameFile(0, "renamed.iso");
    }

    [Fact]
    public void RenameFile_RejectsEmptyName()
    {
        using var session = NewSession();
        var handle = AddMagnet(session);

        Assert.Throws<ArgumentException>(() => handle.RenameFile(0, ""));
        Assert.Throws<ArgumentException>(() => handle.RenameFile(0, "   "));
        Assert.Throws<ArgumentException>(() => handle.RenameFile(0, null!));
    }

    private static LibtorrentSession NewSession() =>
        new()
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };

    private static MagnetHandle AddMagnet(LibtorrentSession session)
    {
        var result = session.Add(new AddTorrentParams { MagnetUri = ValidMagnetUri });
        Assert.True(result.IsValid);
        return result.Magnet!;
    }
}
