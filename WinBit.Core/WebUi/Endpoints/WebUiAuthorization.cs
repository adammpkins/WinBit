using Microsoft.AspNetCore.Http;

namespace WinBit.Core.WebUi.Endpoints;

/// <summary>
/// Single place every endpoint consults to decide whether a request is authorized. Clients
/// whose remote IP falls inside any configured
/// <c>AppSettings.WebUi.WhitelistedSubnets</c> entry skip the cookie check; everyone else
/// needs a valid SID from <see cref="AuthEndpoints.SessionCookieName"/>.
/// </summary>
public static class WebUiAuthorization
{
    public static bool IsAuthorized(HttpContext ctx, IWebUiAuthService auth)
    {
        if (auth.IsWhitelistedIp(ctx.Connection.RemoteIpAddress))
            return true;
        return auth.IsValidSession(ctx.Request.Cookies[AuthEndpoints.SessionCookieName]);
    }
}
