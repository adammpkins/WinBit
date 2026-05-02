using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WinBit.Core.Settings;

namespace WinBit.Core.WebUi.Endpoints;

/// <summary>
/// Port of qBittorrent's <c>AppController</c> (see
/// <c>qbittorrent/src/webui/api/appcontroller.cpp</c>).
/// </summary>
public static class AppEndpoints
{
    public const string Version = WebUiService.VersionString;
    public const string WebApiVersion = "2.10.0";

    public static void Map(IEndpointRouteBuilder app, ISettingsService settings, IWebUiAuthService auth)
    {
        // qBittorrent responds with a bare text/plain body — not JSON — stay compatible.
        app.MapGet("/api/v2/app/version", () => Results.Text(Version, "text/plain"));
        app.MapGet("/api/v2/app/webapiVersion", () => Results.Text(WebApiVersion, "text/plain"));

        app.MapGet("/api/v2/app/buildInfo", () => Results.Json(new
        {
            qt = "",
            libtorrent = "libtorrent-rasterbar (LibtorrentSharp)",
            boost = "",
            openssl = "",
            zlib = "",
            bitness = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 or Architecture.Arm64 => 64,
                Architecture.X86 or Architecture.Arm => 32,
                _ => 64,
            },
            platform = "windows",
        }));

        app.MapGet("/api/v2/app/defaultSavePath", () =>
            Results.Text(settings.Current.Downloads.DefaultSavePath ?? string.Empty, "text/plain"));

        app.MapGet("/api/v2/app/preferences", (HttpContext ctx) =>
        {
            if (!WebUiAuthorization.IsAuthorized(ctx, auth))
                return Results.Unauthorized();

            var s = settings.Current;
            return Results.Json(BuildPreferences(s));
        });

        app.MapPost("/api/v2/app/setPreferences", async (HttpContext ctx) =>
        {
            if (!WebUiAuthorization.IsAuthorized(ctx, auth))
                return Results.Unauthorized();

            string body;
            using (var reader = new System.IO.StreamReader(ctx.Request.Body))
                body = await reader.ReadToEndAsync().ConfigureAwait(false);

            // Accepts both form-urlencoded json= param (qBittorrent compat) and raw JSON body.
            string json = body;
            if (ctx.Request.ContentType?.Contains("application/x-www-form-urlencoded") == true)
            {
                var form = System.Web.HttpUtility.ParseQueryString(body);
                json = form["json"] ?? "{}";
            }

            using var doc = JsonDocument.Parse(json);
            await settings.UpdateAsync(s => ApplyPreferences(s, doc.RootElement)).ConfigureAwait(false);
            return Results.Ok();
        });
    }

    private static object BuildPreferences(AppSettings s) => new
    {
        web_ui_port = s.WebUi.Port,
        web_ui_address = s.WebUi.BindAddress,
        web_ui_username = s.WebUi.Username ?? "admin",
        web_ui_use_https = s.WebUi.Https,
        web_ui_enable_remote_access = s.WebUi.EnableRemoteAccess,
        save_path = s.Downloads.DefaultSavePath ?? string.Empty,
        theme = s.UiState.Theme,
        accent_color = s.UiState.AccentColor,
        dht = s.BitTorrent.Dht,
        pex = s.BitTorrent.Pex,
        lsd = s.BitTorrent.Lsd,
        dl_limit = s.Speed.GlobalDownBps,
        up_limit = s.Speed.GlobalUpBps,
        scheduler_enabled = s.Speed.SchedulerEnabled,
        transfers_sort_column = s.UiState.TransfersGrid.SortColumn,
        transfers_sort_reverse = s.UiState.TransfersGrid.SortReverse,
    };

    private static void ApplyPreferences(AppSettings s, JsonElement root)
    {
        if (root.TryGetProperty("web_ui_port", out var port) && port.TryGetInt32(out var p))
            s.WebUi.Port = p;
        if (root.TryGetProperty("web_ui_address", out var addr) && addr.ValueKind == JsonValueKind.String)
            s.WebUi.BindAddress = addr.GetString()!;
        if (root.TryGetProperty("web_ui_enable_remote_access", out var remote))
            s.WebUi.EnableRemoteAccess = remote.GetBoolean();
        if (root.TryGetProperty("save_path", out var sp) && sp.ValueKind == JsonValueKind.String)
            s.Downloads.DefaultSavePath = sp.GetString();
        if (root.TryGetProperty("theme", out var theme) && theme.ValueKind == JsonValueKind.String)
            s.UiState.Theme = theme.GetString()!;
        if (root.TryGetProperty("accent_color", out var accent))
            s.UiState.AccentColor = accent.ValueKind == JsonValueKind.Null ? null : accent.GetString();
        if (root.TryGetProperty("dht", out var dht))
            s.BitTorrent.Dht = dht.GetBoolean();
        if (root.TryGetProperty("pex", out var pex))
            s.BitTorrent.Pex = pex.GetBoolean();
        if (root.TryGetProperty("lsd", out var lsd))
            s.BitTorrent.Lsd = lsd.GetBoolean();
        if (root.TryGetProperty("dl_limit", out var dl) && dl.TryGetInt32(out var dlv))
            s.Speed.GlobalDownBps = dlv;
        if (root.TryGetProperty("up_limit", out var ul) && ul.TryGetInt32(out var ulv))
            s.Speed.GlobalUpBps = ulv;
        if (root.TryGetProperty("scheduler_enabled", out var sched))
            s.Speed.SchedulerEnabled = sched.GetBoolean();
        if (root.TryGetProperty("transfers_sort_column", out var sc))
            s.UiState.TransfersGrid.SortColumn = sc.ValueKind == JsonValueKind.Null ? null : sc.GetString();
        if (root.TryGetProperty("transfers_sort_reverse", out var sr))
            s.UiState.TransfersGrid.SortReverse = sr.GetBoolean();
    }
}
