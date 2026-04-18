using FluentAssertions;
using WinBit.Core.Networking;
using WinBit.Core.Search.Torznab;
using Xunit;

namespace WinBit.Tests;

public sealed class TorznabSearchPluginTests
{
    [Fact]
    public void Appends_t_search_query_and_apikey_to_configured_url()
    {
        var plugin = new TorznabSearchPlugin(
            new TorznabFeedDefinition { Name = "jackett", Url = "http://example/api", ApiKey = "k&1" },
            new NullHttp());
        plugin.BuildRequestUrl("ubuntu linux").Should()
            .Be("http://example/api?t=search&q=ubuntu+linux&apikey=k%261");
    }

    [Fact]
    public void Uses_ampersand_when_url_already_has_querystring()
    {
        var plugin = new TorznabSearchPlugin(
            new TorznabFeedDefinition { Name = "j", Url = "http://example/api?x=y" },
            new NullHttp());
        plugin.BuildRequestUrl("q").Should().Be("http://example/api?x=y&t=search&q=q");
    }

    [Fact]
    public void Omits_apikey_when_blank()
    {
        var plugin = new TorznabSearchPlugin(
            new TorznabFeedDefinition { Name = "j", Url = "http://example/api", ApiKey = "" },
            new NullHttp());
        plugin.BuildRequestUrl("q").Should().Be("http://example/api?t=search&q=q");
    }

    private sealed class NullHttp : IHttpClientProvider
    {
        public HttpClient Get() => new();
    }
}
