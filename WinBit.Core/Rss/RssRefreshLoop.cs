using Microsoft.Extensions.Hosting;
using WinBit.Core.Logging;
using WinBit.Core.Networking;
using WinBit.Core.Settings;

namespace WinBit.Core.Rss;

public sealed class RssFeedRefreshedEventArgs : EventArgs
{
    public required string FeedUrl { get; init; }

    public required IReadOnlyList<RssArticle> Articles { get; init; }
}

/// <summary>
/// Periodically walks <see cref="IRssService.GetTreeAsync"/> and fetches every feed whose
/// <c>LastRefreshUtc + effectiveInterval</c> has elapsed, where effective interval = per-feed
/// override ?? <c>AppSettings.Rss.RefreshIntervalMinutes</c>. Emits
/// <see cref="FeedRefreshed"/> with the parsed articles so downstream services (auto-downloader,
/// <c>RssPage</c>) can act without polling.
/// </summary>
public sealed class RssRefreshLoop : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);

    private readonly IRssService _rss;
    private readonly ISettingsService _settings;
    private readonly Func<Uri, CancellationToken, Task<string?>> _fetcher;
    private readonly ILogService _log;
    private readonly TimeProvider _time;

    public event EventHandler<RssFeedRefreshedEventArgs>? FeedRefreshed;

    public RssRefreshLoop(IRssService rss, ISettingsService settings, IHttpClientProvider http, ILogService log,
        TimeProvider? time = null)
        : this(rss, settings, MakeDefaultFetcher(http), log, time)
    {
    }

    // Injectable-fetcher ctor: callers (and tests) can supply their own HTTP fetcher.
    public RssRefreshLoop(IRssService rss, ISettingsService settings,
        Func<Uri, CancellationToken, Task<string?>> fetcher, ILogService log, TimeProvider? time = null)
    {
        _rss = rss;
        _settings = settings;
        _fetcher = fetcher;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            await TickAsync(stoppingToken).ConfigureAwait(false);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Runs a single refresh pass. Public so tests can drive the loop deterministically.</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        var rss = _settings.Current.Rss;
        if (!rss.Enabled)
        {
            return;
        }

        var globalIntervalMinutes = Math.Max(1, rss.RefreshIntervalMinutes);
        var now = _time.GetUtcNow().UtcDateTime;

        var tree = await _rss.GetTreeAsync(ct).ConfigureAwait(false);
        foreach (var feed in CollectFeeds(tree))
        {
            ct.ThrowIfCancellationRequested();

            var interval = TimeSpan.FromMinutes(feed.RefreshIntervalMinutesOverride ?? globalIntervalMinutes);
            if (feed.LastRefreshUtc is DateTime last && now - last < interval)
            {
                continue;
            }

            if (!Uri.TryCreate(feed.Url, UriKind.Absolute, out var uri))
            {
                _log.Write($"RSS: skipping feed with invalid URL '{feed.Url}'", LogSeverity.Warning);
                continue;
            }

            string? xml;
            try
            {
                xml = await _fetcher(uri, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Write($"RSS: fetch failed for '{feed.Url}': {ex.Message}", LogSeverity.Warning);
                continue;
            }

            if (string.IsNullOrWhiteSpace(xml))
            {
                await _rss.MarkRefreshedAsync(feed.Url, now, ct).ConfigureAwait(false);
                continue;
            }

            var doc = RssFeedParser.Parse(xml, feed.Url);
            FeedRefreshed?.Invoke(this, new RssFeedRefreshedEventArgs
            {
                FeedUrl = feed.Url,
                Articles = doc.Articles,
            });

            await _rss.MarkRefreshedAsync(feed.Url, now, ct).ConfigureAwait(false);
        }
    }

    internal static IEnumerable<RssFeedConfig> CollectFeeds(RssFolder folder)
    {
        foreach (var f in folder.Feeds)
        {
            yield return f;
        }
        foreach (var sub in folder.Folders)
        {
            foreach (var f in CollectFeeds(sub))
            {
                yield return f;
            }
        }
    }

    private static Func<Uri, CancellationToken, Task<string?>> MakeDefaultFetcher(IHttpClientProvider http) =>
        async (uri, ct) =>
        {
            var client = http.Get();
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        };
}
