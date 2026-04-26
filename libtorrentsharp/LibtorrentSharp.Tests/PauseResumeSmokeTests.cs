using System;
using System.IO;
using Xunit;

namespace LibtorrentSharp.Tests;

public class PauseResumeSmokeTests
{
    private const string ValidMagnetUri = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=ubuntu-14.04.1-desktop-amd64.iso";

    [Fact]
    [Trait("Category", "Native")]
    public void PauseAndResume_OnValidMagnetHandle_DoesNotThrow()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = ValidMagnetUri }).Magnet!;
        Assert.True(handle.IsValid);

        handle.Pause();
        handle.Resume();
        handle.Pause();
    }

    [Fact]
    [Trait("Category", "Native")]
    public void PauseAndResume_OnInvalidHandle_IsNoOp()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = "not-a-magnet" }).Magnet!;
        Assert.False(handle.IsValid);

        handle.Pause();
        handle.Resume();
    }

    private static LibtorrentSession NewClient() =>
        new()
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };
}
