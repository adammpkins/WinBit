using System;
using System.IO;
using LibtorrentSharp.Enums;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Round-trips libtorrent's torrent_flags_t through the binding's
/// <see cref="MagnetHandle.Flags"/> / <c>SetFlags</c> / <c>UnsetFlags</c>
/// surface. Uses a magnet handle since add-via-magnet is the simplest path
/// to a valid handle that doesn't depend on a real .torrent fixture.
/// </summary>
public sealed class TorrentFlagsSmokeTests
{
    private const string ValidMagnetUri = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=ubuntu-14.04.1-desktop-amd64.iso";

    private static MagnetHandle AddMagnet(LibtorrentSession session)
    {
        var result = session.Add(new AddTorrentParams { MagnetUri = ValidMagnetUri });
        Assert.True(result.IsValid);
        return result.Magnet!;
    }

    [Fact]
    [Trait("Category", "Native")]
    public void Flags_returnsPausedAndAutoManaged_onFreshAddDefaults()
    {
        using var session = new LibtorrentSession
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };
        var handle = AddMagnet(session);

        // The native add path forces paused-on, auto_managed-off (see
        // lts_add_magnet in library.cpp). Round-trip those bits.
        var flags = handle.Flags;
        Assert.True(flags.HasFlag(TorrentFlags.Paused),
            $"Expected Paused bit set on fresh add; got {flags}");
        Assert.False(flags.HasFlag(TorrentFlags.AutoManaged),
            $"Expected AutoManaged bit clear on fresh add; got {flags}");
    }

    [Fact]
    [Trait("Category", "Native")]
    public void SetFlags_setsRequestedBits_andLeavesOthersUntouched()
    {
        using var session = new LibtorrentSession
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };
        var handle = AddMagnet(session);
        var before = handle.Flags;

        // Pick three orthogonal bits unlikely to be set by default.
        const TorrentFlags ToSet = TorrentFlags.SequentialDownload | TorrentFlags.DisableDht | TorrentFlags.ApplyIpFilter;
        handle.SetFlags(ToSet);

        var after = handle.Flags;
        Assert.True(after.HasFlag(TorrentFlags.SequentialDownload));
        Assert.True(after.HasFlag(TorrentFlags.DisableDht));
        Assert.True(after.HasFlag(TorrentFlags.ApplyIpFilter));

        // Bits we didn't touch should match the prior state.
        const TorrentFlags Untouched = TorrentFlags.Paused;
        Assert.Equal(before & Untouched, after & Untouched);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void UnsetFlags_clearsRequestedBits_andLeavesOthersUntouched()
    {
        using var session = new LibtorrentSession
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };
        var handle = AddMagnet(session);

        // Seed a known bit, then clear it.
        handle.SetFlags(TorrentFlags.SequentialDownload);
        Assert.True(handle.Flags.HasFlag(TorrentFlags.SequentialDownload));

        handle.UnsetFlags(TorrentFlags.SequentialDownload);
        Assert.False(handle.Flags.HasFlag(TorrentFlags.SequentialDownload));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void SetFlagsWithMask_onlyRewritesMaskedBits()
    {
        using var session = new LibtorrentSession
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };
        var handle = AddMagnet(session);

        // Seed two bits.
        handle.SetFlags(TorrentFlags.SequentialDownload | TorrentFlags.DisableDht);

        // Now ask: clear SequentialDownload, set DisableLsd. Restrict the mask
        // to those two bits so DisableDht must be left alone.
        const TorrentFlags Mask = TorrentFlags.SequentialDownload | TorrentFlags.DisableLsd;
        handle.SetFlags(TorrentFlags.DisableLsd, Mask);

        var after = handle.Flags;
        Assert.False(after.HasFlag(TorrentFlags.SequentialDownload),
            "SequentialDownload should be cleared by the masked set");
        Assert.True(after.HasFlag(TorrentFlags.DisableLsd),
            "DisableLsd should be set");
        Assert.True(after.HasFlag(TorrentFlags.DisableDht),
            "DisableDht is outside the mask — should remain set");
    }

    [Fact]
    public void Flags_returnsNone_onInvalidMagnetHandle()
    {
        using var session = new LibtorrentSession();
        var result = session.Add(new AddTorrentParams { MagnetUri = "not-a-magnet" });
        Assert.False(result.IsValid);
        Assert.Equal(TorrentFlags.None, result.Magnet!.Flags);
    }
}
