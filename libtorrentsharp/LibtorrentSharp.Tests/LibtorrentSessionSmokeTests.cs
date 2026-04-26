using Xunit;

namespace LibtorrentSharp.Tests;

public class TorrentClientSmokeTests
{
    [Fact]
    [Trait("Category", "Native")]
    public void Constructor_CreatesAndDisposesSession_WithoutThrowing()
    {
        using var client = new LibtorrentSession();
        Assert.NotNull(client);
    }
}
