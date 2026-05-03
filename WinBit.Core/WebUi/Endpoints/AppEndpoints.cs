using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WinBit.Core.Settings;
using WinBit.Core.WebUi;

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
        preallocate_all = s.Downloads.PreAllocate,
        auto_tmm_enabled = s.Downloads.AutoTmmEnabled,
        theme = s.UiState.Theme,
        accent_color = s.UiState.AccentColor,
        listen_port = s.Connection.ListenPort,
        upnp = s.Connection.Upnp,
        dht = s.BitTorrent.Dht,
        pex = s.BitTorrent.Pex,
        lsd = s.BitTorrent.Lsd,
        encryption = (int)s.BitTorrent.Encryption,
        dl_limit = s.Speed.GlobalDownBps,
        up_limit = s.Speed.GlobalUpBps,
        alt_dl_limit = s.Speed.AltDownBps,
        alt_up_limit = s.Speed.AltUpBps,
        alt_speed_enabled = s.Speed.AltEnabled,
        scheduler_enabled = s.Speed.SchedulerEnabled,
        schedule_from_hour = s.Speed.SchedulerStartTime.Hour,
        schedule_from_min = s.Speed.SchedulerStartTime.Minute,
        schedule_to_hour = s.Speed.SchedulerEndTime.Hour,
        schedule_to_min = s.Speed.SchedulerEndTime.Minute,
        scheduler_days = (int)s.Speed.SchedulerDays,
        transfers_sort_column = s.UiState.TransfersGrid.SortColumn,
        transfers_sort_reverse = s.UiState.TransfersGrid.SortReverse,
        transfers_hidden_columns = s.UiState.TransfersGrid.HiddenColumns,
    };

    private static void ApplyPreferences(AppSettings s, JsonElement root)
    {
        if (root.TryGetProperty("web_ui_port", out var port) && port.TryGetInt32(out var p))
            s.WebUi.Port = p;
        if (root.TryGetProperty("web_ui_address", out var addr) && addr.ValueKind == JsonValueKind.String)
            s.WebUi.BindAddress = addr.GetString()!;
        if (root.TryGetProperty("web_ui_enable_remote_access", out var remote))
            s.WebUi.EnableRemoteAccess = remote.GetBoolean();
        if (root.TryGetProperty("web_ui_username", out var wuUser))
        {
            var u = wuUser.GetString() ?? string.Empty;
            // Mirror qBittorrent's validation: min 3 chars, colon disallowed (Basic auth separator).
            if (u.Length >= 3 && !u.Contains(':'))
                s.WebUi.Username = u;
        }
        if (root.TryGetProperty("web_ui_password", out var wuPass))
        {
            var pw = wuPass.GetString() ?? string.Empty;
            // Mirror qBittorrent's minimum password length.
            if (pw.Length >= 6)
                s.WebUi.PasswordHash = PasswordHasher.Hash(pw);
        }
        if (root.TryGetProperty("save_path", out var sp) && sp.ValueKind == JsonValueKind.String)
            s.Downloads.DefaultSavePath = sp.GetString();
        if (root.TryGetProperty("preallocate_all", out var pa))
            s.Downloads.PreAllocate = pa.GetBoolean();
        if (root.TryGetProperty("auto_tmm_enabled", out var atm))
            s.Downloads.AutoTmmEnabled = atm.GetBoolean();
        if (root.TryGetProperty("theme", out var theme) && theme.ValueKind == JsonValueKind.String)
            s.UiState.Theme = theme.GetString()!;
        if (root.TryGetProperty("accent_color", out var accent))
            s.UiState.AccentColor = accent.ValueKind == JsonValueKind.Null ? null : accent.GetString();
        if (root.TryGetProperty("listen_port", out var lp) && lp.TryGetInt32(out var lpv))
            s.Connection.ListenPort = lpv;
        if (root.TryGetProperty("upnp", out var upnp))
            s.Connection.Upnp = upnp.GetBoolean();
        if (root.TryGetProperty("dht", out var dht))
            s.BitTorrent.Dht = dht.GetBoolean();
        if (root.TryGetProperty("pex", out var pex))
            s.BitTorrent.Pex = pex.GetBoolean();
        if (root.TryGetProperty("lsd", out var lsd))
            s.BitTorrent.Lsd = lsd.GetBoolean();
        if (root.TryGetProperty("encryption", out var enc))
            s.BitTorrent.Encryption = (EncryptionMode)enc.GetInt32();
        if (root.TryGetProperty("dl_limit", out var dl) && dl.TryGetInt32(out var dlv))
            s.Speed.GlobalDownBps = dlv;
        if (root.TryGetProperty("up_limit", out var ul) && ul.TryGetInt32(out var ulv))
            s.Speed.GlobalUpBps = ulv;
        if (root.TryGetProperty("alt_dl_limit", out var altDl) && altDl.TryGetInt32(out var altDlv))
            s.Speed.AltDownBps = altDlv;
        if (root.TryGetProperty("alt_up_limit", out var altUp) && altUp.TryGetInt32(out var altUpv))
            s.Speed.AltUpBps = altUpv;
        if (root.TryGetProperty("alt_speed_enabled", out var altEn))
            s.Speed.AltEnabled = altEn.GetBoolean();
        if (root.TryGetProperty("scheduler_enabled", out var sched))
            s.Speed.SchedulerEnabled = sched.GetBoolean();
        if (root.TryGetProperty("schedule_from_hour", out var sfh) && root.TryGetProperty("schedule_from_min", out var sfm)
            && sfh.TryGetInt32(out var sfhv) && sfm.TryGetInt32(out var sfmv))
            s.Speed.SchedulerStartTime = new TimeOnly(sfhv, sfmv);
        if (root.TryGetProperty("schedule_to_hour", out var sth) && root.TryGetProperty("schedule_to_min", out var stm)
            && sth.TryGetInt32(out var sthv) && stm.TryGetInt32(out var stmv))
            s.Speed.SchedulerEndTime = new TimeOnly(sthv, stmv);
        if (root.TryGetProperty("scheduler_days", out var sd) && sd.TryGetInt32(out var sdv))
            s.Speed.SchedulerDays = (BandwidthScheduleDays)sdv;
        if (root.TryGetProperty("transfers_sort_column", out var sc))
            s.UiState.TransfersGrid.SortColumn = sc.ValueKind == JsonValueKind.Null ? null : sc.GetString();
        if (root.TryGetProperty("transfers_sort_reverse", out var sr))
            s.UiState.TransfersGrid.SortReverse = sr.GetBoolean();
        if (root.TryGetProperty("transfers_hidden_columns", out var hc) && hc.ValueKind == JsonValueKind.Array)
            s.UiState.TransfersGrid.HiddenColumns = hc.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();
    }
}
