using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WinBit.Core.BitTorrent;

namespace WinBit.Core.WebUi.Endpoints;

/// <summary>
/// Port of <c>qbittorrent/src/webui/api/torrentcreatorcontroller.cpp</c>. Mirrors the four
/// externally-visible verbs: addTask, status, downloadTorrent, deleteTask. The format /
/// optimizeAlignment knobs are libtorrent-specific and are not currently configurable through
/// our <see cref="TorrentCreateRequest"/> — the endpoint accepts them but ignores them so the
/// Python <c>qbittorrent-api</c> client's default payload is accepted cleanly.
/// </summary>
public static class TorrentCreatorEndpoints
{
    public static void Map(IEndpointRouteBuilder app, ITorrentCreatorQueue queue,
        IWebUiAuthService auth)
    {
        app.MapPost("/api/v2/torrentcreator/addTask", async (HttpContext ctx) =>
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
            var sourcePath = form["sourcePath"].ToString();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return Results.BadRequest("'sourcePath' is required.");
            }

            var request = new TorrentCreateRequest
            {
                SourcePath = sourcePath,
                OutputPath = form["torrentFilePath"].ToString() is var tp && !string.IsNullOrWhiteSpace(tp)
                    ? tp
                    : string.Empty, // queue assigns a temp path when blank
                Comment = NullIfEmpty(form["comment"].ToString()),
                IsPrivate = ParseBool(form["private"].ToString()),
                PieceLength = ParsePieceSize(form["pieceSize"].ToString()),
                TrackerTiers = SplitTrackerTiers(form["trackers"].ToString()),
                WebSeeds = SplitLines(form["urlSeeds"].ToString()),
            };

            var taskId = queue.AddTask(request);
            return Results.Json(new { taskID = taskId });
        });

        app.MapGet("/api/v2/torrentcreator/status", (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }

            var id = ctx.Request.Query["taskID"].ToString();
            if (string.IsNullOrWhiteSpace(id))
            {
                return Results.Json(queue.GetStatus().Select(Serialize));
            }

            var one = queue.GetStatus(id);
            if (one is null)
            {
                return Results.NotFound();
            }
            return Results.Json(new[] { Serialize(one) });
        });

        app.MapGet("/api/v2/torrentcreator/downloadTorrent", (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }
            var id = ctx.Request.Query["taskID"].ToString();
            if (string.IsNullOrWhiteSpace(id))
            {
                return Results.BadRequest("'taskID' is required.");
            }
            var status = queue.GetStatus(id);
            if (status is null)
            {
                return Results.NotFound();
            }
            if (status.State != TorrentCreatorTaskState.Finished)
            {
                return Results.Conflict();
            }
            var bytes = queue.GetResult(id);
            if (bytes is null)
            {
                return Results.NotFound();
            }
            return Results.File(bytes, "application/x-bittorrent", $"{id}.torrent");
        });

        app.MapPost("/api/v2/torrentcreator/deleteTask", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }
            var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);
            var id = form["taskID"].ToString();
            if (string.IsNullOrWhiteSpace(id))
            {
                return Results.BadRequest("'taskID' is required.");
            }
            return queue.DeleteTask(id) ? Results.Ok() : Results.NotFound();
        });
    }

    private static object Serialize(TorrentCreatorTaskStatus s) => new
    {
        taskID = s.TaskId,
        status = s.State.ToString(),
        sourcePath = s.Request.SourcePath,
        torrentFilePath = s.Request.OutputPath ?? string.Empty,
        pieceSize = s.Request.PieceLength ?? 0,
        @private = s.Request.IsPrivate,
        comment = s.Request.Comment ?? string.Empty,
        trackers = s.Request.TrackerTiers.SelectMany(t => t).ToArray(),
        urlSeeds = s.Request.WebSeeds,
        timeAdded = s.TimeAddedUtc.ToString("O"),
        timeStarted = s.TimeStartedUtc?.ToString("O"),
        timeFinished = s.TimeFinishedUtc?.ToString("O"),
        progress = s.Progress,
        error = s.Error ?? string.Empty,
    };

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static bool ParseBool(string? s) =>
        string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);

    private static int? ParsePieceSize(string? s)
    {
        if (int.TryParse(s, out var v) && v > 0)
        {
            return v;
        }
        return null;
    }

    private static IReadOnlyList<string> SplitLines(string raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<string>()
            : raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<IReadOnlyList<string>> SplitTrackerTiers(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<IReadOnlyList<string>>();
        }
        var tiers = new List<IReadOnlyList<string>>();
        var current = new List<string>();
        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                if (current.Count > 0)
                {
                    tiers.Add(current);
                    current = new();
                }
            }
            else
            {
                current.Add(line);
            }
        }
        if (current.Count > 0) tiers.Add(current);
        return tiers;
    }

    private static bool IsAuthorized(HttpContext ctx, IWebUiAuthService auth) =>
        WebUiAuthorization.IsAuthorized(ctx, auth);
}
