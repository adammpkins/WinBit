using System.Net;
using FluentAssertions;
using WinBit.Core.Networking;
using WinBit.Core.Settings;
using Xunit;

namespace WinBit.Tests;

public sealed class HttpClientProviderTests
{
    [Fact]
    public void None_returns_handler_with_proxy_disabled()
    {
        var handler = HttpClientProvider.BuildHandler(new ConnectionSettings { ProxyType = ProxyType.None });

        handler.UseProxy.Should().BeFalse();
    }

    [Fact]
    public void Http_builds_WebProxy_with_http_scheme()
    {
        var handler = HttpClientProvider.BuildHandler(new ConnectionSettings
        {
            ProxyType = ProxyType.Http,
            ProxyHost = "proxy.example",
            ProxyPort = 8080,
        });

        handler.UseProxy.Should().BeTrue();
        handler.Proxy.Should().BeOfType<WebProxy>();
        var proxy = (WebProxy)handler.Proxy!;
        proxy.Address.Should().Be(new Uri("http://proxy.example:8080"));
        proxy.Credentials.Should().BeNull();
    }

    [Fact]
    public void Socks5_builds_WebProxy_with_socks5_scheme()
    {
        var handler = HttpClientProvider.BuildHandler(new ConnectionSettings
        {
            ProxyType = ProxyType.Socks5,
            ProxyHost = "127.0.0.1",
            ProxyPort = 1080,
        });

        handler.UseProxy.Should().BeTrue();
        var proxy = (WebProxy)handler.Proxy!;
        proxy.Address.Should().Be(new Uri("socks5://127.0.0.1:1080"));
    }

    [Fact]
    public void Credentials_flow_through_when_username_is_set()
    {
        var handler = HttpClientProvider.BuildHandler(new ConnectionSettings
        {
            ProxyType = ProxyType.Http,
            ProxyHost = "proxy.example",
            ProxyPort = 8080,
            ProxyUsername = "alice",
            ProxyPassword = "s3cret",
        });

        var proxy = (WebProxy)handler.Proxy!;
        proxy.Credentials.Should().BeOfType<NetworkCredential>();
        var creds = (NetworkCredential)proxy.Credentials!;
        creds.UserName.Should().Be("alice");
        creds.Password.Should().Be("s3cret");
    }

    [Fact]
    public void Missing_host_falls_back_to_no_proxy_even_when_type_is_set()
    {
        var handler = HttpClientProvider.BuildHandler(new ConnectionSettings
        {
            ProxyType = ProxyType.Socks5,
            ProxyHost = "",
            ProxyPort = 1080,
        });

        handler.UseProxy.Should().BeFalse();
    }
}
