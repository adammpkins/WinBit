using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using WinBit.Core.Logging;
using WinBit.Core.Networking;

namespace WinBit.Core.Updates;

/// <summary>
/// Queries <c>https://api.github.com/repos/{Repo}/releases/latest</c> and compares the <c>tag_name</c>
/// to the running assembly's version. Pre-releases and drafts are skipped because GitHub's
/// <c>/releases/latest</c> endpoint already filters them out.
/// </summary>
public sealed class GitHubUpdateChecker : IUpdateChecker
{
    /// <summary>Owner/name slug for the upstream repository.</summary>
    public const string Repo = "adammpkins/winbit";

    private readonly IHttpClientProvider _http;
    private readonly ILogService _log;

    public GitHubUpdateChecker(IHttpClientProvider http, ILogService log)
    {
        _http = http;
        _log = log;
    }

    public async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        var current = ResolveRunningVersion();
        try
        {
            var client = _http.Get();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            request.Headers.UserAgent.TryParseAdd($"WinBit/{current}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateInfo(current, null, null, null, HasUpdate: false);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return ParsePayload(current, await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _log.Write($"Update check failed: {ex.Message}", LogSeverity.Warning);
            return new UpdateInfo(current, null, null, null, HasUpdate: false);
        }
    }

    /// <summary>Public to let tests exercise the parser without hitting the network.</summary>
    public static UpdateInfo Parse(Version current, string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParsePayload(current, doc);
    }

    private static UpdateInfo ParsePayload(Version current, JsonDocument doc)
    {
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
        var url = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;
        var latest = TryParseVersion(tag);
        var hasUpdate = latest is not null && latest > current;
        return new UpdateInfo(current, latest, tag, url, hasUpdate);
    }

    public static Version? TryParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }
        var trimmed = tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;
        // Strip anything after a '-' (pre-release markers) or '+' (build metadata).
        var cutoff = trimmed.IndexOfAny(new[] { '-', '+' });
        if (cutoff >= 0)
        {
            trimmed = trimmed[..cutoff];
        }
        return Version.TryParse(trimmed, out var v) ? v : null;
    }

    private static Version ResolveRunningVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(GitHubUpdateChecker).Assembly;
        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return TryParseVersion(informational) ?? asm.GetName().Version ?? new Version(0, 0, 0);
    }
}
