using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace WinBit.Core.WebUi.Endpoints;

/// <summary>
/// Port of <c>qbittorrent/src/webui/api/authcontroller.cpp</c>. Response bodies match
/// qBittorrent so <c>qbittorrent-api</c> / Sonarr / Radarr work unchanged: <c>"Ok."</c> on
/// successful login, <c>"Fails."</c> (HTTP 403) on bad credentials.
/// </summary>
public static class AuthEndpoints
{
    public const string SessionCookieName = "SID";

    public static void Map(IEndpointRouteBuilder app, IWebUiAuthService auth)
    {
        app.MapPost("/api/v2/auth/login", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);
            var username = form["username"].ToString();
            var password = form["password"].ToString();

            if (!auth.ValidateCredentials(username, password))
            {
                return Results.Text("Fails.", "text/plain", statusCode: StatusCodes.Status403Forbidden);
            }

            var sid = auth.StartSession();
            ctx.Response.Cookies.Append(SessionCookieName, sid, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Secure = ctx.Request.IsHttps,
            });
            return Results.Text("Ok.", "text/plain");
        });

        app.MapPost("/api/v2/auth/logout", (HttpContext ctx) =>
        {
            var sid = ctx.Request.Cookies[SessionCookieName];
            if (!string.IsNullOrEmpty(sid))
            {
                auth.EndSession(sid);
            }
            ctx.Response.Cookies.Delete(SessionCookieName, new CookieOptions { Path = "/" });
            return Results.Text(string.Empty, "text/plain");
        });
    }
}
