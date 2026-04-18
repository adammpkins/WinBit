using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace WinBit.Core.WebUi.Endpoints;

/// <summary>
/// Placeholder port of <c>qbittorrent/src/webui/api/searchcontroller.cpp</c>. Search plugins
/// are an M12 deliverable; these endpoints keep the URL surface live so
/// <c>qbittorrent-api</c>-shaped clients don't 404, but return empty lists on read routes
/// and 409 Conflict with the documented "Search service unavailable" body on the verbs
/// that would otherwise need a running plugin. Replaced with a real implementation when
/// <c>ISearchService</c> lands.
/// </summary>
public static class SearchEndpoints
{
    private const string Unavailable = "Search service unavailable";

    public static void Map(IEndpointRouteBuilder app, IWebUiAuthService auth)
    {
        // Read routes — return idle state, never 404.
        app.MapGet("/api/v2/search/plugins", (HttpContext ctx) =>
            IsAuthorized(ctx, auth) ? Results.Json(Array.Empty<object>()) : Results.Unauthorized());

        app.MapGet("/api/v2/search/status", (HttpContext ctx) =>
            IsAuthorized(ctx, auth) ? Results.Json(Array.Empty<object>()) : Results.Unauthorized());

        app.MapGet("/api/v2/search/results", (HttpContext ctx) =>
            IsAuthorized(ctx, auth)
                ? Results.Json(new { status = "Stopped", total = 0, results = Array.Empty<object>() })
                : Results.Unauthorized());

        // Mutating routes — honest 409 rather than fake-success.
        MapConflict(app, "/api/v2/search/start", auth);
        MapConflict(app, "/api/v2/search/stop", auth);
        MapConflict(app, "/api/v2/search/delete", auth);
        MapConflict(app, "/api/v2/search/installPlugin", auth);
        MapConflict(app, "/api/v2/search/uninstallPlugin", auth);
        MapConflict(app, "/api/v2/search/enablePlugin", auth);
        MapConflict(app, "/api/v2/search/updatePlugins", auth);
    }

    private static void MapConflict(IEndpointRouteBuilder app, string route, IWebUiAuthService auth)
    {
        app.MapPost(route, (HttpContext ctx) =>
            IsAuthorized(ctx, auth)
                ? Results.Text(Unavailable, "text/plain", statusCode: StatusCodes.Status409Conflict)
                : Results.Unauthorized());
    }

    private static bool IsAuthorized(HttpContext ctx, IWebUiAuthService auth) =>
        WebUiAuthorization.IsAuthorized(ctx, auth);
}
