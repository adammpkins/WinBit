using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WinBit.Core.Rss;

namespace WinBit.Core.WebUi.Endpoints;

/// <summary>
/// Port of the feed-list and rule-CRUD half of
/// <c>qbittorrent/src/webui/api/rsscontroller.cpp</c>. Read-modify-read state (markAsRead,
/// refreshItem, moveItem, matchingArticles) needs per-article persistence and a move
/// primitive on <see cref="IRssService"/> — tracked as a separate M10 sub-item.
/// </summary>
public static class RssEndpoints
{
    public static void Map(IEndpointRouteBuilder app, IRssService rss,
        IAutoDownloaderService rules, IRssArticleCache articles, IRssRefresher refresher,
        IWebUiAuthService auth)
    {
        // ---- Feed tree ------------------------------------------------------

        app.MapGet("/api/v2/rss/items", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }
            var withData = string.Equals(ctx.Request.Query["withData"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
            var root = await rss.GetTreeAsync(ctx.RequestAborted).ConfigureAwait(false);
            return Results.Json(withData ? SerializeFolderWithData(root, articles) : SerializeFolder(root));
        });

        app.MapPost("/api/v2/rss/addFolder", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }
            var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);
            var path = form["path"].ToString();
            if (string.IsNullOrWhiteSpace(path))
            {
                return Results.BadRequest("'path' is required.");
            }
            await rss.UpsertFolderAsync(path, ctx.RequestAborted).ConfigureAwait(false);
            return Results.Ok();
        });

        app.MapPost("/api/v2/rss/addFeed", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }
            var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);
            var url = form["url"].ToString();
            if (string.IsNullOrWhiteSpace(url))
            {
                return Results.BadRequest("'url' is required.");
            }
            // qBittorrent's path is "Folder/FeedName" or just "FeedName" — the feed takes the
            // leaf segment as its display title when no separate title is given.
            var rawPath = form["path"].ToString();
            var (parent, title) = SplitParentAndLeaf(rawPath);
            await rss.UpsertFeedAsync(parent, new RssFeedConfig
            {
                Url = url,
                Title = string.IsNullOrWhiteSpace(title) ? null : title,
            }, ctx.RequestAborted).ConfigureAwait(false);
            return Results.Ok();
        });

        app.MapPost("/api/v2/rss/refreshItem", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }
            var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);
            var itemPath = form["itemPath"].ToString();
            if (string.IsNullOrWhiteSpace(itemPath))
            {
                return Results.BadRequest("'itemPath' is required.");
            }

            var tree = await rss.GetTreeAsync(ctx.RequestAborted).ConfigureAwait(false);
            var feedUrls = CollectFeedUrlsForPath(tree, SplitPath(itemPath)).ToArray();
            if (feedUrls.Length == 0)
            {
                return Results.NotFound();
            }

            foreach (var url in feedUrls)
            {
                await refresher.RefreshFeedAsync(url, ctx.RequestAborted).ConfigureAwait(false);
            }
            return Results.Ok();
        });

        app.MapPost("/api/v2/rss/markAsRead", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }
            var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);
            var itemPath = form["itemPath"].ToString();
            if (string.IsNullOrWhiteSpace(itemPath))
            {
                return Results.BadRequest("'itemPath' is required.");
            }
            var articleId = form["articleId"].ToString();
            var articleIdOrNull = string.IsNullOrWhiteSpace(articleId) ? null : articleId;

            var tree = await rss.GetTreeAsync(ctx.RequestAborted).ConfigureAwait(false);
            var feedUrls = CollectFeedUrlsForPath(tree, SplitPath(itemPath)).ToArray();
            if (feedUrls.Length == 0)
            {
                return Results.NotFound();
            }

            foreach (var url in feedUrls)
            {
                await articles.MarkAsReadAsync(url, articleIdOrNull, ctx.RequestAborted).ConfigureAwait(false);
            }
            return Results.Ok();
        });

        app.MapPost("/api/v2/rss/moveItem", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }
            var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);
            var itemPath = form["itemPath"].ToString();
            var destPath = form["destPath"].ToString();
            if (string.IsNullOrWhiteSpace(itemPath) || string.IsNullOrWhiteSpace(destPath))
            {
                return Results.BadRequest("'itemPath' and 'destPath' are required.");
            }
            try
            {
                await rss.MoveItemAsync(itemPath, destPath, ctx.RequestAborted).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound();
            }
            return Results.Ok();
        });

        app.MapPost("/api/v2/rss/removeItem", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }
            var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);
            var path = form["path"].ToString();
            if (string.IsNullOrWhiteSpace(path))
            {
                return Results.BadRequest("'path' is required.");
            }

            // Try feed removal first (if leaf matches a feed URL under the parent),
            // otherwise treat as folder.
            var tree = await rss.GetTreeAsync(ctx.RequestAborted).ConfigureAwait(false);
            var (parent, leaf) = SplitParentAndLeaf(path);
            var feedUrl = FindFeedUrl(tree, SplitPath(path));
            if (feedUrl is not null)
            {
                await rss.RemoveFeedAsync(parent, feedUrl, ctx.RequestAborted).ConfigureAwait(false);
            }
            else
            {
                await rss.RemoveFolderAsync(path, ctx.RequestAborted).ConfigureAwait(false);
            }
            return Results.Ok();
        });

        // ---- Rules ---------------------------------------------------------

        app.MapGet("/api/v2/rss/rules", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }
            var all = await rules.GetAllAsync(ctx.RequestAborted).ConfigureAwait(false);
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var r in all)
            {
                result[r.Name] = SerializeRule(r);
            }
            return Results.Json(result);
        });

        app.MapPost("/api/v2/rss/setRule", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }
            var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);
            var name = form["ruleName"].ToString();
            var body = form["ruleDef"].ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest("'ruleName' is required.");
            }

            AutoDownloadRule rule;
            try
            {
                rule = DeserializeRule(name, body);
            }
            catch (JsonException ex)
            {
                return Results.BadRequest($"'ruleDef' invalid JSON: {ex.Message}");
            }

            await rules.UpsertAsync(rule, ctx.RequestAborted).ConfigureAwait(false);
            return Results.Ok();
        });

        app.MapPost("/api/v2/rss/removeRule", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }
            var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);
            var name = form["ruleName"].ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                await rules.RemoveAsync(name, ctx.RequestAborted).ConfigureAwait(false);
            }
            return Results.Ok();
        });

        app.MapGet("/api/v2/rss/matchingArticles", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }
            var name = ctx.Request.Query["ruleName"].ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest("'ruleName' is required.");
            }

            var rule = await rules.GetAsync(name, ctx.RequestAborted).ConfigureAwait(false);
            var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (rule is null)
            {
                return Results.Json(result);
            }

            // Mirror qBittorrent: only feeds in AffectedFeeds are probed. Empty list → empty
            // response, not "every feed".
            var tree = await rss.GetTreeAsync(ctx.RequestAborted).ConfigureAwait(false);
            foreach (var feedUrl in rule.AffectedFeeds)
            {
                var feed = FindFeed(tree, feedUrl);
                if (feed is null)
                {
                    continue;
                }
                var cached = articles.Get(feedUrl);
                var titles = cached
                    .Where(a => RuleMatcher.Evaluate(rule, a).IsMatch)
                    .Select(a => a.Title)
                    .ToList();
                if (titles.Count > 0)
                {
                    var displayName = string.IsNullOrWhiteSpace(feed.Title) ? feed.Url : feed.Title!;
                    result[displayName] = titles;
                }
            }

            return Results.Json(result);
        });

        app.MapPost("/api/v2/rss/renameRule", async (HttpContext ctx) =>
        {
            if (!IsAuthorized(ctx, auth))
            {
                return Results.Unauthorized();
            }
            var form = await ctx.Request.ReadFormAsync().ConfigureAwait(false);
            var fromName = form["ruleName"].ToString();
            var toName = form["newRuleName"].ToString();
            if (string.IsNullOrWhiteSpace(fromName) || string.IsNullOrWhiteSpace(toName))
            {
                return Results.BadRequest("'ruleName' and 'newRuleName' are required.");
            }
            var existing = await rules.GetAsync(fromName, ctx.RequestAborted).ConfigureAwait(false);
            if (existing is null)
            {
                return Results.NotFound();
            }
            await rules.UpsertAsync(existing with { Name = toName }, ctx.RequestAborted).ConfigureAwait(false);
            await rules.RemoveAsync(fromName, ctx.RequestAborted).ConfigureAwait(false);
            return Results.Ok();
        });
    }

    // ---- Serialization --------------------------------------------------

    internal static Dictionary<string, object> SerializeFolder(RssFolder folder)
    {
        var dict = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var sub in folder.Folders)
        {
            dict[sub.Name] = SerializeFolder(sub);
        }
        foreach (var feed in folder.Feeds)
        {
            var title = string.IsNullOrWhiteSpace(feed.Title) ? feed.Url : feed.Title!;
            dict[title] = new
            {
                url = feed.Url,
                uid = feed.Url, // we use URL as a stable uid — no per-feed GUID yet.
                lastBuildDate = feed.LastRefreshUtc?.ToString("O"),
            };
        }
        return dict;
    }

    internal static Dictionary<string, object> SerializeFolderWithData(RssFolder folder, IRssArticleCache cache)
    {
        var dict = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var sub in folder.Folders)
        {
            dict[sub.Name] = SerializeFolderWithData(sub, cache);
        }
        foreach (var feed in folder.Feeds)
        {
            var title = string.IsNullOrWhiteSpace(feed.Title) ? feed.Url : feed.Title!;
            var feedArticles = cache.Get(feed.Url).Select(a => new
            {
                id = a.Id,
                title = a.Title,
                date = a.PublishedUtc.ToString("O"),
                torrentURL = a.TorrentUrl ?? string.Empty,
                link = a.TorrentUrl ?? string.Empty,
                isRead = cache.IsRead(feed.Url, a.Id),
            }).ToArray();
            dict[title] = new
            {
                url = feed.Url,
                uid = feed.Url,
                lastBuildDate = feed.LastRefreshUtc?.ToString("O"),
                articles = feedArticles,
            };
        }
        return dict;
    }

    internal static object SerializeRule(AutoDownloadRule r) => new
    {
        enabled = r.Enabled,
        mustContain = r.MustContain,
        mustNotContain = r.MustNotContain,
        useRegex = r.UseRegex,
        episodeFilter = r.EpisodeFilter,
        smartFilter = r.SmartFilter,
        affectedFeeds = r.AffectedFeeds,
        ignoreDays = r.IgnoreDays,
        lastMatch = r.LastMatchUtc?.ToString("O"),
        previouslyMatchedEpisodes = r.PreviouslyMatchedEpisodes,
    };

    internal static AutoDownloadRule DeserializeRule(string name, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AutoDownloadRule { Name = name };
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new AutoDownloadRule
        {
            Name = name,
            Enabled = root.TryGetProperty("enabled", out var e) ? e.GetBoolean() : true,
            MustContain = root.TryGetProperty("mustContain", out var m) ? m.GetString() ?? "" : "",
            MustNotContain = root.TryGetProperty("mustNotContain", out var mn) ? mn.GetString() ?? "" : "",
            UseRegex = root.TryGetProperty("useRegex", out var ur) && ur.GetBoolean(),
            EpisodeFilter = root.TryGetProperty("episodeFilter", out var ef) ? ef.GetString() ?? "" : "",
            SmartFilter = root.TryGetProperty("smartFilter", out var sf) && sf.GetBoolean(),
            IgnoreDays = root.TryGetProperty("ignoreDays", out var id) ? id.GetInt32() : 0,
            AffectedFeeds = root.TryGetProperty("affectedFeeds", out var af)
                ? af.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToArray()
                : Array.Empty<string>(),
            PreviouslyMatchedEpisodes = root.TryGetProperty("previouslyMatchedEpisodes", out var pm)
                ? pm.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToArray()
                : Array.Empty<string>(),
        };
    }

    // ---- Path helpers ---------------------------------------------------

    private static (string Parent, string Leaf) SplitParentAndLeaf(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ("", "");
        }
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return ("", "");
        }
        var leaf = parts[^1];
        var parent = parts.Length > 1 ? string.Join('/', parts[..^1]) : "";
        return (parent, leaf);
    }

    private static string[] SplitPath(string path) =>
        string.IsNullOrWhiteSpace(path)
            ? Array.Empty<string>()
            : path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<string> CollectFeedUrlsForPath(RssFolder root, string[] segments)
    {
        if (segments.Length == 0)
        {
            // Root → every feed under every folder.
            foreach (var url in AllFeedUrls(root)) yield return url;
            yield break;
        }

        // Walk folders; at the leaf, decide whether it's a feed or a folder.
        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var next = current.Folders.FirstOrDefault(f =>
                string.Equals(f.Name, segments[i], StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                yield break;
            }
            current = next;
        }

        var leaf = segments[^1];
        var leafFolder = current.Folders.FirstOrDefault(f =>
            string.Equals(f.Name, leaf, StringComparison.OrdinalIgnoreCase));
        if (leafFolder is not null)
        {
            foreach (var url in AllFeedUrls(leafFolder)) yield return url;
            yield break;
        }

        var leafFeed = current.Feeds.FirstOrDefault(f =>
            string.Equals(f.Title, leaf, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f.Url, leaf, StringComparison.OrdinalIgnoreCase));
        if (leafFeed is not null)
        {
            yield return leafFeed.Url;
        }
    }

    private static IEnumerable<string> AllFeedUrls(RssFolder folder)
    {
        foreach (var f in folder.Feeds) yield return f.Url;
        foreach (var sub in folder.Folders)
        {
            foreach (var u in AllFeedUrls(sub)) yield return u;
        }
    }

    private static RssFeedConfig? FindFeed(RssFolder root, string url)
    {
        foreach (var f in root.Feeds)
        {
            if (string.Equals(f.Url, url, StringComparison.OrdinalIgnoreCase))
            {
                return f;
            }
        }
        foreach (var sub in root.Folders)
        {
            var found = FindFeed(sub, url);
            if (found is not null)
            {
                return found;
            }
        }
        return null;
    }

    private static string? FindFeedUrl(RssFolder node, string[] segments)
    {
        if (segments.Length == 0)
        {
            return null;
        }

        var leaf = segments[^1];

        // Walk down parent folders.
        var current = node;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var name = segments[i];
            var next = current.Folders.FirstOrDefault(f =>
                string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                return null;
            }
            current = next;
        }

        return current.Feeds.FirstOrDefault(f =>
                string.Equals(f.Title, leaf, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.Url, leaf, StringComparison.OrdinalIgnoreCase))?.Url;
    }

    private static bool IsAuthorized(HttpContext ctx, IWebUiAuthService auth) =>
        WebUiAuthorization.IsAuthorized(ctx, auth);
}
