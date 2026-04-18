using System.Net;
using System.Text;
using FluentAssertions;
using WinBit.Core.BitTorrent;
using Xunit;

namespace WinBit.Tests;

public sealed class UrlDownloaderTests
{
    [Fact]
    public async Task Rejects_non_http_schemes()
    {
        var downloader = new UrlDownloader(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("should not reach handler"))));

        var result = await downloader.DownloadAsync(new Uri("ftp://example.invalid/file.torrent"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Unsupported URL scheme");
    }

    [Fact]
    public async Task Returns_bytes_on_success()
    {
        var expected = Encoding.UTF8.GetBytes("d4:infod6:lengthi1ee4:name4:teste8:announce19:http://t.example/ae");
        var downloader = new UrlDownloader(new HttpClient(new StubHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(expected) })));

        var result = await downloader.DownloadAsync(new Uri("https://example.invalid/file.torrent"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(expected);
    }

    [Fact]
    public async Task Propagates_HTTP_error_status_as_failure()
    {
        var downloader = new UrlDownloader(new HttpClient(new StubHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.NotFound) { ReasonPhrase = "Not Found" })));

        var result = await downloader.DownloadAsync(new Uri("https://example.invalid/missing.torrent"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("404").And.Contain("Not Found");
    }

    [Fact]
    public async Task Rejects_when_content_length_exceeds_cap()
    {
        var content = new ByteArrayContent(new byte[16]);
        content.Headers.ContentLength = 1_000_000;
        var downloader = new UrlDownloader(
            new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = content })),
            maxBytes: 1024);

        var result = await downloader.DownloadAsync(new Uri("https://example.invalid/large.torrent"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("exceeds max size");
    }

    [Fact]
    public async Task Rejects_when_streaming_body_exceeds_cap_without_content_length()
    {
        var payload = new byte[2048];
        var downloader = new UrlDownloader(
            new HttpClient(new StubHandler((_, _) =>
            {
                var content = new StreamContent(new MemoryStream(payload));
                content.Headers.ContentLength = null;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            })),
            maxBytes: 1024);

        var result = await downloader.DownloadAsync(new Uri("https://example.invalid/chunked.torrent"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("exceeds max size");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request, cancellationToken));
    }
}
