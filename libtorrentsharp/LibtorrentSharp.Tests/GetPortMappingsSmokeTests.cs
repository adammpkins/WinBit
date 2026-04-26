using System;
using System.IO;
using Xunit;

namespace LibtorrentSharp.Tests;

public class GetPortMappingsSmokeTests
{
    [Fact]
    [Trait("Category", "Native")]
    public void GetPortMappings_OnFreshSession_ReturnsEmptyList()
    {
        using var client = NewClient();

        var mappings = client.GetPortMappings();

        Assert.NotNull(mappings);
        Assert.Empty(mappings);
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetPortMappings_ThrowsAfterDispose()
    {
        var client = NewClient();
        client.Dispose();

        Assert.Throws<ObjectDisposedException>(() => client.GetPortMappings());
    }

    private static LibtorrentSession NewClient() =>
        new()
        {
            DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests", Guid.NewGuid().ToString("N"))
        };
}
