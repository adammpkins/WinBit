using WinBit.Core.Common;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Fetches a <c>.torrent</c> file from an HTTP(S) URL so it can be handed to MonoTorrent.
/// Enforces scheme, size cap, and success-status guards — any failure returns a
/// <see cref="Result{T}"/> failure that viewmodels render as an inline <c>InfoBar</c> on
/// the Add dialog rather than a toast.
/// </summary>
public sealed class UrlDownloader
{
    public const long DefaultMaxBytes = 20 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly long _maxBytes;

    public UrlDownloader(HttpClient http, long maxBytes = DefaultMaxBytes)
    {
        _http = http;
        _maxBytes = maxBytes;
    }

    public async Task<Result<byte[]>> DownloadAsync(Uri url, CancellationToken ct = default)
    {
        if (!string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return Result<byte[]>.Failure($"Unsupported URL scheme: {url.Scheme}. Only http/https are allowed.");
        }

        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Result<byte[]>.Failure($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            if (response.Content.Headers.ContentLength is long advertised && advertised > _maxBytes)
            {
                return Result<byte[]>.Failure($"Torrent file exceeds max size ({advertised} > {_maxBytes} bytes).");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var tmp = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(tmp.AsMemory(0, tmp.Length), ct).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > _maxBytes)
                {
                    return Result<byte[]>.Failure($"Torrent file exceeds max size (> {_maxBytes} bytes).");
                }
                buffer.Write(tmp, 0, read);
            }

            return Result<byte[]>.Success(buffer.ToArray());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return Result<byte[]>.Failure($"Download failed: {ex.Message}");
        }
    }
}
