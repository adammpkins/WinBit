using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WinBit.Core.Settings;

namespace WinBit.Core.WebUi.Endpoints;

/// <summary>
/// Port of qBittorrent's <c>AppController</c> (see
/// <c>qbittorrent/src/webui/api/appcontroller.cpp</c>). Only the endpoints the
/// <c>qbittorrent-api</c> Python client and Sonarr/Radarr touch during discovery are
/// implemented here; the heavier <c>preferences</c> + <c>setPreferences</c> surface lands as
/// its own M10 sub-item.
/// </summary>
public static class AppEndpoints
{
    /// <summary>Version of this WinBit build, formatted to match qBittorrent's bare string output.</summary>
    public const string Version = WebUiService.VersionString;

    /// <summary>qBittorrent Web API version we claim compatibility with.</summary>
    public const string WebApiVersion = "2.10.0";

    public static void Map(IEndpointRouteBuilder app, ISettingsService settings)
    {
        // qBittorrent responds with a bare text/plain body for these — not JSON. Stay compatible
        // so clients that `.strip()` the response work unchanged.
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
    }
}
