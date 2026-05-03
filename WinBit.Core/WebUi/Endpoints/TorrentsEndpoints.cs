using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Persistence;
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
        IWebUiAuthService auth, ISettingsService settings, ITorrentStateStore stateStore)
    {
        app.MapGet("/api/v2/torrents/info", async (HttpContext ctx, CancellationToken ct) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }

            var snapshots = session.GetSnapshots();
            var stateRecords = await stateStore.GetAllAsync(ct).ConfigureAwait(false);
            var stateMap = stateRecords.ToDictionary(r => r.Id);
            var rows = snapshots.Select(s => BuildInfoRow(s, session, stateMap)).ToArray();
            return Results.Json(rows);
        });

        app.MapPost("/api/v2/torrents/pause",
            (Func<HttpContext, Task<IResult>>)(ctx => RunHashActionAsync(ctx, auth, session, PauseAction)));
        // qBittorrent v5 renamed /pause→/stop and /resume→/start; support both so native WebUI and v5-aware clients work.
        app.MapPost("/api/v2/torrents/stop",
            (Func<HttpContext, Task<IResult>>)(ctx => RunHashActionAsync(ctx, auth, session, PauseAction)));
        app.MapPost("/api/v2/torrents/resume",
            (Func<HttpContext, Task<IResult>>)(ctx => RunHashActionAsync(ctx, auth, session, ResumeAction)));
        app.MapPost("/api/v2/torrents/start",
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

            // .torrent file uploads — spool to a temp file so the engine can open it by path.
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

        app.MapGet("/api/v2/torrents/properties", async (HttpContext ctx, CancellationToken ct) =>
        {
            if (!IsAuthorized(ctx, auth)) return Results.Unauthorized();
            var hash = ctx.Request.Query["hash"].ToString();
            if (string.IsNullOrWhiteSpace(hash)) return Results.BadRequest();
            var id = TorrentId.FromInfoHash(hash);
            var detail = await session.GetTorrentDetailAsync(id, ct).ConfigureAwait(false);
            if (detail is null) return Results.NotFound();
            var snap = session.GetSnapshots().FirstOrDefault(s => s.Id == id);
            return Results.Json(new
            {
                hash = id.Value,
                save_path = detail.SavePath ?? string.Empty,
                comment = detail.Comment ?? string.Empty,
                created_by = detail.Creator ?? string.Empty,
                creation_date = detail.CreationDate.HasValue ? (long?)detail.CreationDate.Value.ToUnixTimeSeconds() : null,
                addition_date = detail.AddedDate.HasValue ? (long?)detail.AddedDate.Value.ToUnixTimeSeconds() : null,
                completion_date = detail.CompletionDate.HasValue ? (long?)detail.CompletionDate.Value.ToUnixTimeSeconds() : null,
                total_size = snap.TotalSize,
                pieces_num = detail.TotalPieces,
                piece_size = detail.PieceLength,
                dl_speed = snap.DownloadSpeedBps,
                up_speed = snap.UploadSpeedBps,
                downloaded = snap.BytesDownloaded,
                uploaded = snap.BytesUploaded,
                ratio = snap.Ratio,
            });
        });

        app.MapGet("/api/v2/torrents/trackers", async (HttpContext ctx, CancellationToken ct) =>
        {
            if (!IsAuthorized(ctx, auth)) return Results.Unauthorized();
            var hash = ctx.Request.Query["hash"].ToString();
            if (string.IsNullOrWhiteSpace(hash)) return Results.BadRequest();
            var id = TorrentId.FromInfoHash(hash);
            var trackers = await session.GetTrackersAsync(id, ct).ConfigureAwait(false);
            var rows = trackers.Select(t => new
            {
                url = t.Url.ToString(),
                status = MapTrackerStatus(t.Status),
                tier = t.Tier,
                num_seeds = t.Seeds,
                num_leeches = t.Leeches,
                num_downloaded = t.Completed,
                msg = t.LastError ?? string.Empty,
            }).ToArray();
            return Results.Json(rows);
        });

        app.MapGet("/api/v2/torrents/peers", async (HttpContext ctx, CancellationToken ct) =>
        {
            if (!IsAuthorized(ctx, auth)) return Results.Unauthorized();
            var hash = ctx.Request.Query["hash"].ToString();
            if (string.IsNullOrWhiteSpace(hash)) return Results.BadRequest();
            var id = TorrentId.FromInfoHash(hash);
            var peers = await session.GetPeersAsync(id, ct).ConfigureAwait(false);
            // qBittorrent returns { full_update: bool, peers: { "ip:port": { ... } } }
            var dict = new Dictionary<string, object>();
            foreach (var p in peers)
            {
                var parts = p.Address.Split(':');
                dict[p.Address] = new
                {
                    ip = parts[0],
                    port = parts.Length > 1 && int.TryParse(parts[^1], out var port) ? port : 0,
                    client = p.Client ?? string.Empty,
                    progress = p.Progress,
                    dl_speed = p.DownloadSpeedBps,
                    up_speed = p.UploadSpeedBps,
                    flags = (p.IsSeeder ? "S" : "") + (p.IsEncrypted ? "E" : ""),
                };
            }
            return Results.Json(new { full_update = true, peers = dict });
        });

        app.MapGet("/api/v2/torrents/files", async (HttpContext ctx, CancellationToken ct) =>
        {
            if (!IsAuthorized(ctx, auth)) return Results.Unauthorized();
            var hash = ctx.Request.Query["hash"].ToString();
            if (string.IsNullOrWhiteSpace(hash)) return Results.BadRequest();
            var id = TorrentId.FromInfoHash(hash);
            var files = await session.GetTorrentFilesAsync(id, ct).ConfigureAwait(false);
            var rows = files.Select(f => new
            {
                index = f.Index,
                name = f.RelativePath,
                size = f.SizeBytes,
                progress = f.ProgressFraction,
                priority = (int)f.Priority,
            }).ToArray();
            return Results.Json(rows);
        });

        app.MapGet("/api/v2/torrents/pieceStates", async (HttpContext ctx, CancellationToken ct) =>
        {
            if (!IsAuthorized(ctx, auth)) return Results.Unauthorized();
            var hash = ctx.Request.Query["hash"].ToString();
            if (string.IsNullOrWhiteSpace(hash)) return Results.BadRequest();
            var id = TorrentId.FromInfoHash(hash);
            var pieces = await session.GetPiecesAsync(id, ct).ConfigureAwait(false);
            // qBittorrent int array: 0=not downloaded, 1=downloading, 2=downloaded
            var states = pieces.Select(have => have ? 2 : 0).ToArray();
            return Results.Json(states);
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

    private static object BuildInfoRow(TorrentSnapshot snapshot, ITorrentSessionService session,
        Dictionary<TorrentId, TorrentStateRecord> stateMap)
    {
        var name = session.GetName(snapshot.Id) ?? snapshot.Id.Value;
        var savePath = session.GetSavePath(snapshot.Id) ?? string.Empty;
        var magnet = session.GetMagnetUri(snapshot.Id);
        stateMap.TryGetValue(snapshot.Id, out var rec);
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
            category = rec?.Category ?? "",
            tags = GetTagsString(rec),
        };
    }

    private static string GetTagsString(TorrentStateRecord? rec)
    {
        if (rec?.Tags == null) return "";
        try
        {
            return string.Join(",", System.Text.Json.JsonSerializer.Deserialize<string[]>(rec.Tags) ?? []);
        }
        catch
        {
            return "";
        }
    }

    // Port of qBittorrent's torrent-state string vocabulary so Sonarr/Radarr classifiers work.
    internal static string MapState(TorrentState state) => state switch
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

    // Maps to qBittorrent's tracker status integers: 1=not contacted, 2=working, 3=updating, 4=not working.
    // 0 (disabled) is never emitted because WinBit has no disabled-tracker concept.
    private static int MapTrackerStatus(TrackerStatus s) => s switch
    {
        TrackerStatus.NotContacted => 1,
        TrackerStatus.Working => 2,
        TrackerStatus.Updating => 3,
        TrackerStatus.Failure => 4,
        _ => 1,
    };
}
