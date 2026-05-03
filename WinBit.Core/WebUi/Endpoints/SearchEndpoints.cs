using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WinBit.Core.Search;

namespace WinBit.Core.WebUi.Endpoints;

/// <summary>
/// Port of <c>qbittorrent/src/webui/api/searchcontroller.cpp</c>. Uses
/// <see cref="ISearchPluginHost"/> to run concurrent Torznab searches and serves results
/// via the same polling API shape that qBittorrent clients expect.
/// </summary>
public static class SearchEndpoints
{
    private sealed class SearchJob
    {
        public int Id { get; init; }
        public List<object> Results { get; } = new();
        public volatile bool IsRunning = true;
        public readonly CancellationTokenSource Cts = new();
        public readonly object Lock = new();
    }

    private static int _nextId;
    private static readonly ConcurrentDictionary<int, SearchJob> Jobs = new();

    public static void Map(IEndpointRouteBuilder app, IWebUiAuthService auth,
        ISearchPluginHost searchPluginHost)
    {
        app.MapGet("/api/v2/search/plugins", (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth)) return Results.Unauthorized();
            var plugins = searchPluginHost.Plugins.Select(p => new
            {
                name = p.Name,
                fullName = p.Name,
                enabled = true,
                version = "1.0",
                url = "",
                supportedCategories = new[] { new { id = "all", name = "All categories" } },
            }).ToArray();
            return Results.Json(plugins);
        });

        app.MapGet("/api/v2/search/status", (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth)) return Results.Unauthorized();
            var idStr = ctx.Request.Query["id"].ToString();
            IEnumerable<SearchJob> jobs = int.TryParse(idStr, out var id) && Jobs.TryGetValue(id, out var j)
                ? [j]
                : Jobs.Values;
            var result = jobs.Select(job => new
            {
                id = job.Id,
                status = job.IsRunning ? "Running" : "Stopped",
                total = job.Results.Count,
            }).ToArray();
            return Results.Json(result);
        });

        app.MapGet("/api/v2/search/results", (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth)) return Results.Unauthorized();
            if (!int.TryParse(ctx.Request.Query["id"].ToString(), out var id) || !Jobs.TryGetValue(id, out var job))
                return Results.NotFound();
            _ = int.TryParse(ctx.Request.Query["offset"].ToString(), out var offset);
            _ = int.TryParse(ctx.Request.Query["limit"].ToString(), out var limit);
            if (limit <= 0) limit = 50;

            object[] slice;
            int total;
            lock (job.Lock)
            {
                total = job.Results.Count;
                slice = job.Results.Skip(offset).Take(limit).ToArray();
            }
            return Results.Json(new
            {
                status = job.IsRunning ? "Running" : "Stopped",
                total,
                results = slice,
            });
        });

        app.MapPost("/api/v2/search/start", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth)) return Results.Unauthorized();
            var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);
            var pattern = form["pattern"].ToString();
            var category = form["category"].ToString();
            if (string.IsNullOrWhiteSpace(pattern)) return Results.BadRequest("'pattern' is required.");

            var job = new SearchJob { Id = Interlocked.Increment(ref _nextId) };
            Jobs[job.Id] = job;

            var request = new SearchRequest(pattern, string.IsNullOrWhiteSpace(category) || category == "all" ? null : category);
            var pluginsParam = form["plugins"].ToString();
            string[]? pluginNames = string.IsNullOrWhiteSpace(pluginsParam) || pluginsParam == "all" || pluginsParam == "enabled"
                ? null
                : pluginsParam.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var r in searchPluginHost.SearchAsync(request, pluginNames, job.Cts.Token).ConfigureAwait(false))
                    {
                        var row = new
                        {
                            fileName = r.Name,
                            fileUrl = r.TorrentUrl ?? r.MagnetUri ?? string.Empty,
                            fileSize = r.SizeBytes ?? -1L,
                            nbSeeders = r.Seeders ?? -1,
                            nbLeechers = r.Leechers ?? -1,
                            engineName = r.PluginName,
                            siteUrl = r.DetailsUrl ?? string.Empty,
                            descrLink = r.DetailsUrl ?? string.Empty,
                        };
                        lock (job.Lock) { job.Results.Add(row); }
                    }
                }
                catch (OperationCanceledException) { }
                finally { job.IsRunning = false; }
            });

            return Results.Json(new { id = job.Id });
        });

        app.MapPost("/api/v2/search/stop", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth)) return Results.Unauthorized();
            var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);
            if (int.TryParse(form["id"].ToString(), out var id) && Jobs.TryGetValue(id, out var job))
                job.Cts.Cancel();
            return Results.Ok();
        });

        app.MapPost("/api/v2/search/delete", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth)) return Results.Unauthorized();
            var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);
            if (int.TryParse(form["id"].ToString(), out var id) && Jobs.TryGetValue(id, out var job))
            {
                job.Cts.Cancel();
                Jobs.TryRemove(id, out _);
            }
            return Results.Ok();
        });

        // Plugin management stubs — not applicable for Torznab (managed via settings).
        app.MapPost("/api/v2/search/installPlugin", (HttpContext ctx) =>
            IsAuthorized(ctx, auth) ? Results.Ok() : Results.Unauthorized());
        app.MapPost("/api/v2/search/uninstallPlugin", (HttpContext ctx) =>
            IsAuthorized(ctx, auth) ? Results.Ok() : Results.Unauthorized());
        app.MapPost("/api/v2/search/enablePlugin", (HttpContext ctx) =>
            IsAuthorized(ctx, auth) ? Results.Ok() : Results.Unauthorized());
        app.MapPost("/api/v2/search/updatePlugins", (HttpContext ctx) =>
            IsAuthorized(ctx, auth) ? Results.Ok() : Results.Unauthorized());
    }

    private static bool IsAuthorized(HttpContext ctx, IWebUiAuthService auth) =>
        WebUiAuthorization.IsAuthorized(ctx, auth);
}
