using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WinBit.Core.BitTorrent;
using WinBit.Core.Settings;

namespace WinBit.Core.WebUi.Endpoints;

/// <summary>
/// Port of <c>qbittorrent/src/webui/api/transfercontroller.cpp</c>: session-level rates,
/// session-total bytes, and the alt-speed mode toggle. The rate-limit fields come from
/// <see cref="AppSettings.Speed"/>; per-torrent limit endpoints belong to the torrents
/// controller and aren't touched here.
/// </summary>
public static class TransferEndpoints
{
    public static void Map(IEndpointRouteBuilder app, ITorrentSessionService session,
        IWebUiAuthService auth, ISettingsService settings)
    {
        app.MapGet("/api/v2/transfer/info", (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }

            var stats = session.GetSessionStats();
            var speed = settings.Current.Speed;
            var alt = speed.AltEnabled;
            return Results.Json(new
            {
                dl_info_speed = stats.GlobalDownloadBps,
                dl_info_data = stats.SessionDownloadedBytes,
                up_info_speed = stats.GlobalUploadBps,
                up_info_data = stats.SessionUploadedBytes,
                // qBittorrent reports the currently-active global limit (alt if enabled).
                dl_rate_limit = alt ? speed.AltDownBps : speed.GlobalDownBps,
                up_rate_limit = alt ? speed.AltUpBps : speed.GlobalUpBps,
                dht_nodes = stats.DhtNodes,
                connection_status = session.IsRunning ? "connected" : "disconnected",
            });
        });

        app.MapGet("/api/v2/transfer/speedLimitsMode", (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }
            return Results.Text(settings.Current.Speed.AltEnabled ? "1" : "0", "text/plain");
        });

        app.MapPost("/api/v2/transfer/toggleSpeedLimitsMode",
            (Func<HttpContext, Task<IResult>>)(ctx => SetAltAsync(ctx, auth, settings, s =>
            {
                s.Speed.AltEnabled = !s.Speed.AltEnabled;
            })));

        app.MapPost("/api/v2/transfer/setSpeedLimitsMode",
            (Func<HttpContext, Task<IResult>>)(async ctx =>
            {
                if (!IsAuthorized(ctx, auth))
                {
                    return Results.Unauthorized();
                }
                var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);
                if (!int.TryParse(form["mode"].ToString(), out var mode))
                {
                    return Results.BadRequest("'mode': invalid argument");
                }
                // qBittorrent: any non-zero mode → alt enabled.
                await settings.UpdateAsync(s => s.Speed.AltEnabled = mode != 0, ctx.RequestAborted).ConfigureAwait(false);
                return Results.Ok();
            }));
    }

    private static async Task<IResult> SetAltAsync(HttpContext ctx, IWebUiAuthService auth,
        ISettingsService settings, Action<AppSettings> mutate)
    {
        if (!IsAuthorized(ctx, auth))
        {
            return Results.Unauthorized();
        }
        await settings.UpdateAsync(mutate, ctx.RequestAborted).ConfigureAwait(false);
        return Results.Ok();
    }

    private static bool IsAuthorized(HttpContext ctx, IWebUiAuthService auth) =>
        WebUiAuthorization.IsAuthorized(ctx, auth);
}
