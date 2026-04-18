using System.Net;
using WinBit.Core.Settings;

namespace WinBit.Core.Networking;

/// <summary>
/// Builds an <see cref="HttpClient"/> from the current <c>AppSettings.Connection</c> proxy
/// block. .NET 6+ <see cref="WebProxy"/> supports <c>socks5://</c> URIs natively, so the
/// same pipeline handles HTTP and SOCKS5 — we just flip the scheme on the proxy URI.
/// </summary>
public sealed class HttpClientProvider : IHttpClientProvider, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly object _lock = new();
    private HttpClient? _client;
    private string? _signature;

    public HttpClientProvider(ISettingsService settings)
    {
        _settings = settings;
    }

    public HttpClient Get()
    {
        var snap = _settings.Current.Connection;
        var signature = BuildSignature(snap);
        lock (_lock)
        {
            if (_client is not null && signature == _signature)
            {
                return _client;
            }

            _client?.Dispose();
            _client = Build(snap);
            _signature = signature;
            return _client;
        }
    }

    /// <summary>Exposed for tests — builds a fresh handler without caching.</summary>
    public static HttpClientHandler BuildHandler(ConnectionSettings snap)
    {
        var handler = new HttpClientHandler();

        if (snap.ProxyType == ProxyType.None || string.IsNullOrWhiteSpace(snap.ProxyHost))
        {
            handler.UseProxy = false;
            return handler;
        }

        var scheme = snap.ProxyType switch
        {
            ProxyType.Socks5 => "socks5",
            _ => "http",
        };

        var proxy = new WebProxy($"{scheme}://{snap.ProxyHost}:{snap.ProxyPort}");

        if (!string.IsNullOrEmpty(snap.ProxyUsername))
        {
            proxy.Credentials = new NetworkCredential(snap.ProxyUsername, snap.ProxyPassword ?? string.Empty);
        }

        handler.Proxy = proxy;
        handler.UseProxy = true;
        return handler;
    }

    private static HttpClient Build(ConnectionSettings snap)
    {
        var handler = BuildHandler(snap);
        return new HttpClient(handler, disposeHandler: true);
    }

    private static string BuildSignature(ConnectionSettings snap) =>
        $"{snap.ProxyType}|{snap.ProxyHost}|{snap.ProxyPort}|{snap.ProxyUsername}|{snap.ProxyPassword}";

    public void Dispose()
    {
        lock (_lock)
        {
            _client?.Dispose();
            _client = null;
        }
    }
}
