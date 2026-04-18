using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WinBit.Core.Logging;

namespace WinBit.Core.WebUi.Endpoints;

/// <summary>
/// Port of <c>qbittorrent/src/webui/api/logcontroller.cpp</c>. Read-only; writes happen
/// through the normal <see cref="ILogService.Write"/> / <see cref="IPeerLogService.Record"/>
/// surfaces used by the rest of Core.
/// </summary>
public static class LogEndpoints
{
    public static void Map(IEndpointRouteBuilder app, ILogService log, IPeerLogService peers,
        IWebUiAuthService auth)
    {
        app.MapGet("/api/v2/log/main", (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }

            var q = ctx.Request.Query;
            var lastKnownId = ParseInt(q["last_known_id"], -1);
            var showNormal = ParseBool(q["normal"], defaultValue: true);
            var showInfo = ParseBool(q["info"], defaultValue: true);
            var showWarning = ParseBool(q["warning"], defaultValue: true);
            var showCritical = ParseBool(q["critical"], defaultValue: true);

            var filter = LogSeverity.None;
            if (showNormal) filter |= LogSeverity.Normal;
            if (showInfo) filter |= LogSeverity.Info;
            if (showWarning) filter |= LogSeverity.Warning;
            if (showCritical) filter |= LogSeverity.Critical;

            var entries = log.GetMessages(lastKnownId, filter).Select(e => new
            {
                id = e.Id,
                timestamp = ToUnixMilliseconds(e.TimestampUtc),
                type = (int)e.Severity, // LogSeverity flag bits match qB: 1/2/4/8.
                message = e.Message,
            });

            return Results.Json(entries);
        });

        app.MapGet("/api/v2/log/peers", (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }

            var lastKnownId = ParseInt(ctx.Request.Query["last_known_id"], -1);
            var entries = peers.Recent
                .Where(e => e.Id > lastKnownId)
                .Select(e => new
                {
                    id = e.Id,
                    timestamp = ToUnixMilliseconds(e.TimestampUtc),
                    ip = e.PeerEndpoint,
                    blocked = true, // WinBit only records bans; never an "observed but allowed" entry.
                    reason = e.Reason,
                });

            return Results.Json(entries);
        });
    }

    private static long ToUnixMilliseconds(DateTime utc)
    {
        if (utc == default)
        {
            return 0;
        }
        var dt = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return new DateTimeOffset(dt).ToUnixTimeMilliseconds();
    }

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var n) ? n : fallback;

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        if (bool.TryParse(value, out var b)) return b;
        // qBittorrent accepts 1/0 as well.
        if (value == "1") return true;
        if (value == "0") return false;
        return defaultValue;
    }

    private static bool IsAuthorized(HttpContext ctx, IWebUiAuthService auth) =>
        WebUiAuthorization.IsAuthorized(ctx, auth);
}
