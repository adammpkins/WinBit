using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

namespace WinBit.Core.WebUi;

/// <summary>
/// Serves the Vue 3 + Fluent SPA built into the assembly under <c>WebUi/WinBitApp/</c>.
/// All files are served no-cache because Vite uses stable (non-hashed) filenames so the
/// browser must always revalidate. Unknown paths fall back to <c>index.html</c> to support
/// Vue Router's hash-mode navigation.
/// </summary>
public static class WinBitAppAssets
{
    private const string ResourcePrefix = "WinBit.Core.WebUi.WinBitApp.";

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private static readonly HashSet<string> ResourceNames = new(
        typeof(WinBitAppAssets).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)),
        StringComparer.Ordinal);

    public static void Map(WebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? "/";

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

            // Try exact file; fall back to index.html for SPA client-side routing
            if (await TryServeAsync(ctx, path).ConfigureAwait(false)) return;
            if (await TryServeAsync(ctx, "/index.html").ConfigureAwait(false)) return;

            await next().ConfigureAwait(false);
        });
    }

    private static async Task<bool> TryServeAsync(HttpContext ctx, string path)
    {
        var sub = path.TrimStart('/');
        if (string.IsNullOrEmpty(sub)) sub = "index.html";

        var resourceName = ResourcePrefix + sub.Replace('/', '.');
        if (!ResourceNames.Contains(resourceName)) return false;

        var fileName = Path.GetFileName(sub);
        if (!ContentTypes.TryGetContentType(fileName, out var contentType))
            contentType = "application/octet-stream";

        ctx.Response.Headers.CacheControl = "no-cache";

        ctx.Response.ContentType = contentType;
        await using var stream = typeof(WinBitAppAssets).Assembly.GetManifestResourceStream(resourceName)!;
        await stream.CopyToAsync(ctx.Response.Body, ctx.RequestAborted).ConfigureAwait(false);
        return true;
    }
}
