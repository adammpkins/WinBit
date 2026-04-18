using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using WinBit.Core.Settings;
using WinBit.Core.WebUi.Endpoints;

namespace WinBit.Core.WebUi;

/// <summary>
/// Serves qBittorrent's HTML admin UI straight out of the WinBit.Core assembly. Unauthenticated
/// requests receive <c>public/</c> (the login page), authenticated requests receive
/// <c>private/</c> (the real admin). Template markers that qBittorrent's C++ server rewrites
/// server-side (<c>QBT_TR(text)QBT_TR[CONTEXT=...]</c>, <c>${LANG}</c>, <c>${CACHEID}</c>) are
/// rewritten here so the UI renders without untranslated placeholders.
/// </summary>
public static class QBittorrentAssets
{
    public const string CacheId = "1";
    public const string Language = "en";

    private const string QBittorrentPrefix = "WinBit.Core.WebUi.WebAssets.";
    private const string NativePrefix = "WinBit.Core.WebUi.NativeClient.";
    public const string NativeRoutePrefix = "/winbit/";
    public const string QBittorrentRoutePrefix = "/qbittorrent/";

    private static readonly Regex TranslationMarker = new(
        @"QBT_TR\((?<text>.*?)\)QBT_TR\[CONTEXT=.*?\]",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private static readonly HashSet<string> ResourceNames = new(
        typeof(QBittorrentAssets).Assembly.GetManifestResourceNames(),
        StringComparer.Ordinal);

    public static void Map(WebApplication app, IWebUiAuthService auth, ISettingsService settings)
    {
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? "/";

            // API routes must land on endpoint handlers.
            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                await next().ConfigureAwait(false);
                return;
            }

            if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method))
            {
                await next().ConfigureAwait(false);
                return;
            }

            // Explicit routes: /winbit/ → native client, /qbittorrent/ → qBittorrent UI.
            if (path.StartsWith(NativeRoutePrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (await TryServeNativeAsync(ctx, path[NativeRoutePrefix.Length..]).ConfigureAwait(false))
                {
                    return;
                }
                await next().ConfigureAwait(false);
                return;
            }
            if (path.StartsWith(QBittorrentRoutePrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (await TryServeQBittorrentAsync(ctx, path[QBittorrentRoutePrefix.Length..], auth).ConfigureAwait(false))
                {
                    return;
                }
                await next().ConfigureAwait(false);
                return;
            }

            // Everything else comes through the configured default client.
            var defaultIsNative = settings.Current.WebUi.UseNativeClient;
            var served = defaultIsNative
                ? await TryServeNativeAsync(ctx, path.TrimStart('/')).ConfigureAwait(false)
                : await TryServeQBittorrentAsync(ctx, path.TrimStart('/'), auth).ConfigureAwait(false);

            if (!served)
            {
                await next().ConfigureAwait(false);
            }
        });
    }

    private static async Task<bool> TryServeQBittorrentAsync(HttpContext ctx, string sub, IWebUiAuthService auth)
    {
        var authed = WebUiAuthorization.IsAuthorized(ctx, auth);
        var rootFolder = authed ? "private" : "public";
        return await ServeAsync(ctx, QBittorrentPrefix + rootFolder + ".", sub, rewriteHtml: true).ConfigureAwait(false);
    }

    private static Task<bool> TryServeNativeAsync(HttpContext ctx, string sub) =>
        ServeAsync(ctx, NativePrefix, sub, rewriteHtml: false);

    private static async Task<bool> ServeAsync(HttpContext ctx, string resourcePrefix, string sub, bool rewriteHtml)
    {
        if (string.IsNullOrEmpty(sub) || sub.EndsWith('/'))
        {
            sub += "index.html";
        }

        var resourceName = resourcePrefix + sub.Replace('/', '.');
        if (!ResourceNames.Contains(resourceName))
        {
            return false;
        }

        var fileName = Path.GetFileName(sub);
        if (!ContentTypes.TryGetContentType(fileName, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        await using var stream = typeof(QBittorrentAssets).Assembly
            .GetManifestResourceStream(resourceName)!;

        if (rewriteHtml && IsHtmlLike(fileName, contentType))
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var rewritten = Rewrite(await reader.ReadToEndAsync(ctx.RequestAborted).ConfigureAwait(false));
            ctx.Response.ContentType = contentType;
            await ctx.Response.WriteAsync(rewritten, Encoding.UTF8, ctx.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            ctx.Response.ContentType = contentType;
            await stream.CopyToAsync(ctx.Response.Body, ctx.RequestAborted).ConfigureAwait(false);
        }
        return true;
    }

    /// <summary>Public so tests and a future native web client can reuse qBittorrent's
    /// template-stripping rules.</summary>
    public static string Rewrite(string content) => TranslationMarker
        .Replace(content, m => m.Groups["text"].Value)
        .Replace("${CACHEID}", CacheId, StringComparison.Ordinal)
        .Replace("${LANG}", Language, StringComparison.Ordinal);

    private static bool IsHtmlLike(string fileName, string? contentType)
    {
        if (contentType is not null &&
            (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) ||
             contentType.StartsWith("application/xhtml", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        return fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase);
    }
}
