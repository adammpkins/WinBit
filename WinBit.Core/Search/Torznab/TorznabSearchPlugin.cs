using System.Runtime.CompilerServices;
using System.Web;
using WinBit.Core.Networking;

namespace WinBit.Core.Search.Torznab;

/// <summary>
/// Concrete <see cref="ISearchPlugin"/> backed by a Torznab indexer (Jackett, Prowlarr). The
/// plugin issues a single GET per search with <c>t=search</c> and the user's query, parses the
/// RSS + torznab:attr response, and yields <see cref="SearchResult"/>s.
/// </summary>
public sealed class TorznabSearchPlugin : ISearchPlugin
{
    private readonly TorznabFeedDefinition _def;
    private readonly IHttpClientProvider _http;

    public TorznabSearchPlugin(TorznabFeedDefinition def, IHttpClientProvider http)
    {
        _def = def;
        _http = http;
    }

    public string Name => _def.Name;
    public string DisplayName => string.IsNullOrWhiteSpace(_def.DisplayName) ? _def.Name : _def.DisplayName;
    public IReadOnlyList<string> SupportedCategories { get; } = Array.Empty<string>();

    public async IAsyncEnumerable<SearchResult> SearchAsync(
        SearchRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_def.Url))
        {
            yield break;
        }

        var url = BuildRequestUrl(request.Query);
        using var response = await _http.Get().GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            yield break;
        }
        var xml = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        foreach (var hit in TorznabResponseParser.Parse(Name, xml))
        {
            yield return hit;
        }
    }

    public string BuildRequestUrl(string query)
    {
        var encodedQuery = HttpUtility.UrlEncode(query ?? string.Empty);
        var separator = _def.Url.Contains('?') ? '&' : '?';
        var url = $"{_def.Url}{separator}t=search&q={encodedQuery}";
        if (!string.IsNullOrWhiteSpace(_def.ApiKey))
        {
            url += $"&apikey={HttpUtility.UrlEncode(_def.ApiKey)}";
        }
        return url;
    }
}
