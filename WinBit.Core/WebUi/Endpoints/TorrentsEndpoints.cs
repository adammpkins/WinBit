using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Settings;

namespace WinBit.Core.WebUi.Endpoints;

/// <summary>
/// Read + control subset of qBittorrent's torrents controller (see
/// <c>qbittorrent/src/webui/api/torrentscontroller.cpp</c>): <c>/info</c> returns the
/// array of snapshots that clients like Sonarr/Radarr poll, and the control endpoints
/// (<c>/pause</c>, <c>/resume</c>, <c>/delete</c>, <c>/recheck</c>) accept the familiar
/// <c>hashes=hash1|hash2|all</c> form-field contract.
/// </summary>
public static class TorrentsEndpoints
{
    public static void Map(IEndpointRouteBuilder app, ITorrentSessionService session,
        IWebUiAuthService auth, ISettingsService settings)
    {
        app.MapGet("/api/v2/torrents/info", (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }

            var snapshots = session.GetSnapshots();
            var rows = snapshots.Select(s => BuildInfoRow(s, session)).ToArray();
            return Results.Json(rows);
        });

        app.MapPost("/api/v2/torrents/pause",
            (Func<HttpContext, Task<IResult>>)(ctx => RunHashActionAsync(ctx, auth, session, PauseAction)));
        app.MapPost("/api/v2/torrents/resume",
            (Func<HttpContext, Task<IResult>>)(ctx => RunHashActionAsync(ctx, auth, session, ResumeAction)));
        app.MapPost("/api/v2/torrents/recheck",
            (Func<HttpContext, Task<IResult>>)(ctx => RunHashActionAsync(ctx, auth, session, RecheckAction)));

        app.MapPost("/api/v2/torrents/add", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }

            if (!ctx.Request.HasFormContentType)
            {
                return Results.BadRequest();
            }

            var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);

            var savePath = string.IsNullOrWhiteSpace(form["savepath"].ToString())
                ? settings.Current.Downloads.DefaultSavePath
                : form["savepath"].ToString();
            if (string.IsNullOrWhiteSpace(savePath))
            {
                return Results.Text("No save path configured.", "text/plain",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var category = NullIfEmpty(form["category"].ToString());
            var tags = SplitTags(form["tags"].ToString());
            var paused = ParseBool(form["paused"].ToString());
            var skipChecking = ParseBool(form["skip_checking"].ToString());
            var sequential = ParseBool(form["sequentialDownload"].ToString());
            var firstLast = ParseBool(form["firstLastPiecePrio"].ToString());

            AddTorrentParams Build(string source) => new()
            {
                Source = source,
                SavePath = savePath!,
                Category = category,
                Tags = tags,
                StartImmediately = !paused,
                SkipHashCheck = skipChecking,
                Sequential = sequential,
                FirstAndLastPiecePriority = firstLast,
            };

            var anySuccess = false;
            var anyFailure = false;

            // URLs / magnets — newline-separated per qBittorrent's wire format.
            foreach (var url in (form["urls"].ToString() ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var result = await session.AddAsync(Build(url), ctx.RequestAborted).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    anySuccess = true;
                }
                else
                {
                    anyFailure = true;
                }
            }

            // .torrent file uploads — spool to a temp file so MonoTorrent can open it by path.
            foreach (var file in form.Files.Where(f => f.Length > 0))
            {
                var tempPath = Path.Combine(Path.GetTempPath(),
                    $"winbit-webui-{Guid.NewGuid():N}.torrent");
                try
                {
                    await using (var fs = File.Create(tempPath))
                    {
                        await file.CopyToAsync(fs, ctx.RequestAborted).ConfigureAwait(false);
                    }

                    var result = await session.AddAsync(Build(tempPath), ctx.RequestAborted).ConfigureAwait(false);
                    if (result.IsSuccess)
                    {
                        anySuccess = true;
                    }
                    else
                    {
                        anyFailure = true;
                    }
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
                }
            }

            // qBittorrent returns 200 with plaintext "Ok." / "Fails." — stay compatible.
            if (!anySuccess && anyFailure)
            {
                return Results.Text("Fails.", "text/plain",
                    statusCode: StatusCodes.Status415UnsupportedMediaType);
            }
            return Results.Text("Ok.", "text/plain");
        });

        app.MapPost("/api/v2/torrents/delete", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }

            var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);
            var deleteFiles = string.Equals(form["deleteFiles"].ToString(), "true",
                StringComparison.OrdinalIgnoreCase);

            foreach (var id in ResolveTargetIds(form["hashes"].ToString(), session))
            {
                await session.RemoveAsync(id, deleteFiles, ctx.RequestAborted).ConfigureAwait(false);
            }
            return Results.Ok();
        });
    }

    private static Task<Result> PauseAction(ITorrentSessionService s, TorrentId id, CancellationToken ct) => s.PauseAsync(id, ct);
    private static Task<Result> ResumeAction(ITorrentSessionService s, TorrentId id, CancellationToken ct) => s.ResumeAsync(id, ct);
    private static Task<Result> RecheckAction(ITorrentSessionService s, TorrentId id, CancellationToken ct) => s.ForceRecheckAsync(id, ct);

    private static async Task<IResult> RunHashActionAsync(HttpContext ctx,
        IWebUiAuthService auth, ITorrentSessionService session,
        Func<ITorrentSessionService, TorrentId, CancellationToken, Task<Result>> action)
    {
        if (!IsAuthorized(ctx, auth))
        {
            return Results.Unauthorized();
        }

        var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);
        foreach (var id in ResolveTargetIds(form["hashes"].ToString(), session))
        {
            await action(session, id, ctx.RequestAborted).ConfigureAwait(false);
        }
        return Results.Ok();
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static bool ParseBool(string? s) =>
        string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> SplitTags(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<string>()
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static IEnumerable<TorrentId> ResolveTargetIds(string hashes, ITorrentSessionService session)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return Array.Empty<TorrentId>();
        }

        if (string.Equals(hashes, "all", StringComparison.OrdinalIgnoreCase))
        {
            return session.Torrents.ToArray();
        }

        return hashes.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(h => TorrentId.FromInfoHash(h))
            .ToArray();
    }

    private static bool IsAuthorized(HttpContext ctx, IWebUiAuthService auth) =>
        WebUiAuthorization.IsAuthorized(ctx, auth);

    private static object BuildInfoRow(TorrentSnapshot snapshot, ITorrentSessionService session)
    {
        var name = session.GetName(snapshot.Id) ?? snapshot.Id.Value;
        var savePath = session.GetSavePath(snapshot.Id) ?? string.Empty;
        var magnet = session.GetMagnetUri(snapshot.Id);
        return new
        {
            hash = snapshot.Id.Value,
            name,
            state = MapState(snapshot.State),
            progress = snapshot.Progress,
            dlspeed = snapshot.DownloadSpeedBps,
            upspeed = snapshot.UploadSpeedBps,
            downloaded = snapshot.BytesDownloaded,
            uploaded = snapshot.BytesUploaded,
            ratio = snapshot.Ratio,
            eta = snapshot.Eta?.TotalSeconds is double seconds ? (long)seconds : 8_640_000L,
            num_seeds = snapshot.Seeds,
            num_leechs = snapshot.Peers,
            save_path = savePath,
            magnet_uri = magnet,
        };
    }

    // Port of qBittorrent's torrent-state string vocabulary so Sonarr/Radarr classifiers work.
    internal static string MapState(TorrentState state) => state switch
    {
        TorrentState.Stopped => "stoppedDL",
        TorrentState.Paused => "pausedDL",
        TorrentState.Checking => "checkingDL",
        TorrentState.Queued => "queuedDL",
        TorrentState.Downloading => "downloading",
        TorrentState.Seeding => "uploading",
        TorrentState.Stalled => "stalledDL",
        TorrentState.Completed => "uploading",
        TorrentState.Error => "error",
        _ => "unknown",
    };
}
