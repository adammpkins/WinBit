using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WinBit.Core.BitTorrent;
using WinBit.Core.Categories;
using WinBit.Core.Settings;
using WinBit.Core.Tags;

namespace WinBit.Core.WebUi.Endpoints;

/// <summary>
/// Simplified port of <c>qbittorrent/src/webui/api/synccontroller.cpp</c>. qBittorrent sends
/// deltas keyed on a client-tracked <c>rid</c>; we always respond with <c>full_update=true</c>
/// so <c>qbittorrent-api</c> / Sonarr / Radarr (all of which accept that as "reset state")
/// stay in sync without the server-side delta bookkeeping. Delta support can be layered on
/// later without breaking the response shape.
/// </summary>
public static class SyncEndpoints
{
    public static void Map(IEndpointRouteBuilder app, ITorrentSessionService session,
        ISettingsService settings, ICategoryService categories, ITagService tags,
        IWebUiAuthService auth)
    {
        var ridState = new RidState();

        app.MapGet("/api/v2/sync/maindata", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }

            var rid = ridState.Next();
            var torrents = BuildTorrentsDictionary(session);
            var categoryList = await categories.GetAllAsync(ctx.RequestAborted).ConfigureAwait(false);
            var tagList = await tags.GetAllAsync(ctx.RequestAborted).ConfigureAwait(false);

            return Results.Json(new
            {
                rid,
                full_update = true,
                torrents,
                categories = BuildCategoriesDictionary(categoryList),
                tags = tagList,
                server_state = BuildServerState(session, settings),
            });
        });
    }

    private static Dictionary<string, object> BuildTorrentsDictionary(ITorrentSessionService session)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in session.GetSnapshots())
        {
            dict[snapshot.Id.Value] = new
            {
                name = session.GetName(snapshot.Id) ?? snapshot.Id.Value,
                state = MapStateForSync(snapshot.State),
                size = snapshot.TotalSize,
                progress = snapshot.Progress,
                dlspeed = snapshot.DownloadSpeedBps,
                upspeed = snapshot.UploadSpeedBps,
                downloaded = snapshot.BytesDownloaded,
                uploaded = snapshot.BytesUploaded,
                ratio = snapshot.Ratio,
                eta = snapshot.Eta?.TotalSeconds is double s ? (long)s : 8_640_000L,
                num_seeds = snapshot.Seeds,
                num_leechs = snapshot.Peers,
                save_path = session.GetSavePath(snapshot.Id) ?? string.Empty,
            };
        }
        return dict;
    }

    private static Dictionary<string, object> BuildCategoriesDictionary(IReadOnlyList<Category> categories)
    {
        var dict = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var c in categories)
        {
            dict[c.Name] = new
            {
                name = c.Name,
                savePath = c.SavePath ?? string.Empty,
            };
        }
        return dict;
    }

    private static object BuildServerState(ITorrentSessionService session, ISettingsService settings)
    {
        var stats = session.GetSessionStats();
        var speed = settings.Current.Speed;
        var alt = speed.AltEnabled;
        return new
        {
            dl_info_speed = stats.GlobalDownloadBps,
            dl_info_data = stats.SessionDownloadedBytes,
            up_info_speed = stats.GlobalUploadBps,
            up_info_data = stats.SessionUploadedBytes,
            dl_rate_limit = alt ? speed.AltDownBps : speed.GlobalDownBps,
            up_rate_limit = alt ? speed.AltUpBps : speed.GlobalUpBps,
            dht_nodes = stats.DhtNodes,
            connection_status = session.IsRunning ? "connected" : "disconnected",
            use_alt_speed_limits = alt,
            refresh_interval = 1500,
            queueing = false,
        };
    }

    // Port of qBittorrent's torrent-state vocabulary (same mapping as /torrents/info).
    private static string MapStateForSync(TorrentState state) => state switch
    {
        TorrentState.Stopped => "stoppedDL",
        TorrentState.Paused => "pausedDL",
        TorrentState.Checking => "checkingDL",
        TorrentState.Queued => "queuedDL",
        TorrentState.Metadata => "metaDL",
        TorrentState.Downloading => "downloading",
        TorrentState.Seeding => "uploading",
        TorrentState.Stalled => "stalledDL",
        TorrentState.Completed => "uploading",
        TorrentState.Error => "error",
        _ => "unknown",
    };

    private static bool IsAuthorized(HttpContext ctx, IWebUiAuthService auth) =>
        WebUiAuthorization.IsAuthorized(ctx, auth);

    private sealed class RidState
    {
        private long _rid;
        // qBittorrent cycles rid between 1 and 1_000_000 to stop it overflowing; mirror that.
        public long Next()
        {
            var next = Interlocked.Increment(ref _rid);
            if (next > 1_000_000)
            {
                Interlocked.Exchange(ref _rid, 1);
                return 1;
            }
            return next;
        }
    }
}
