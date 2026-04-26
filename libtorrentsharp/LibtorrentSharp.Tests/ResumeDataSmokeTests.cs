using System;
using System.IO;
using Xunit;

namespace LibtorrentSharp.Tests;

public class ResumeDataSmokeTests
{
    private const string ValidMagnetUri = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=ubuntu-14.04.1-desktop-amd64.iso";

    [Fact]
    [Trait("Category", "Native")]
    public void Add_ResumeWithMalformedBlob_ReturnsInvalidHandle()
    {
        using var client = NewClient();

        var result = client.Add(new AddTorrentParams { ResumeData = new byte[] { 0x00, 0x01, 0x02, 0x03 } });

        Assert.NotNull(result.Magnet);
        Assert.False(result.IsValid);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void Add_ResumeWithEmptyBlob_Throws()
    {
        using var client = NewClient();

        Assert.Throws<ArgumentException>(() => client.Add(new AddTorrentParams { ResumeData = Array.Empty<byte>() }));
        // ResumeData = null with no other source set validates as "no source" (also ArgumentException).
        Assert.Throws<ArgumentException>(() => client.Add(new AddTorrentParams { ResumeData = null }));
    }

    [Fact]
    [Trait("Category", "Native")]
    public void RequestResumeData_OnMagnetHandle_DoesNotThrow()
    {
        using var client = NewClient();
        var handle = client.Add(new AddTorrentParams { MagnetUri = ValidMagnetUri }).Magnet!;
        Assert.True(handle.IsValid);

        // Validates the P/Invoke binding — the resume blob itself surfaces asynchronously
        // via ResumeDataReadyAlert and is exercised by the Layer-3 "resume round-trip" test.
        client.RequestResumeData(handle);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void RequestResumeData_WithNullHandle_Throws()
    {
        using var client = NewClient();
        Assert.Throws<ArgumentNullException>(() => client.RequestResumeData((MagnetHandle)null!));
        Assert.Throws<ArgumentNullException>(() => client.RequestResumeData((TorrentHandle)null!));
    }

    private static LibtorrentSession NewClient() =>
        new()
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };
}
