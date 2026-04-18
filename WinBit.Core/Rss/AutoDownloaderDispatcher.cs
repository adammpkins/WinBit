using Microsoft.Extensions.Hosting;
using WinBit.Core.BitTorrent;
using WinBit.Core.Logging;
using WinBit.Core.Settings;

namespace WinBit.Core.Rss;

/// <summary>
/// Bridges <see cref="RssRefreshLoop.FeedRefreshed"/> into the auto-download pipeline: for
/// every refreshed batch, evaluates each article against every enabled rule via
/// <see cref="RuleMatcher.Evaluate"/>, queues successful matches through
/// <see cref="ITorrentSessionService.AddAsync"/>, and stamps the rule's
/// <c>PreviouslyMatchedEpisodes</c> + <c>LastMatchUtc</c> back through
/// <see cref="IAutoDownloaderService.UpsertAsync"/> so smart-episode dedupe survives restarts.
/// </summary>
public sealed class AutoDownloaderDispatcher : IHostedService
{
    private readonly RssRefreshLoop _loop;
    private readonly IAutoDownloaderService _rules;
    private readonly ISettingsService _settings;
    private readonly ITorrentSessionService _session;
    private readonly ILogService _log;

    public AutoDownloaderDispatcher(RssRefreshLoop loop, IAutoDownloaderService rules,
        ISettingsService settings, ITorrentSessionService session, ILogService log)
    {
        _loop = loop;
        _rules = rules;
        _settings = settings;
        _session = session;
        _log = log;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _loop.FeedRefreshed += OnFeedRefreshed;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _loop.FeedRefreshed -= OnFeedRefreshed;
        return Task.CompletedTask;
    }

    private void OnFeedRefreshed(object? sender, RssFeedRefreshedEventArgs e) =>
        _ = ProcessArticlesAsync(e.FeedUrl, e.Articles, CancellationToken.None);

    /// <summary>
    /// Public so tests and the refresh loop can drive evaluation deterministically. Evaluates
    /// every enabled rule against every article and auto-adds matches.
    /// </summary>
    public async Task ProcessArticlesAsync(string feedUrl, IReadOnlyList<RssArticle> articles, CancellationToken ct)
    {
        if (!_settings.Current.Rss.AutoDownloader || articles.Count == 0)
        {
            return;
        }

        var savePath = _settings.Current.Downloads.DefaultSavePath;
        if (string.IsNullOrWhiteSpace(savePath))
        {
            _log.Write("RSS auto-downloader: skipping — no default save path is set.", LogSeverity.Warning);
            return;
        }

        var rules = await _rules.GetAllAsync(ct).ConfigureAwait(false);
        foreach (var original in rules)
        {
            if (!original.Enabled)
            {
                continue;
            }

            var rule = original;
            foreach (var article in articles)
            {
                ct.ThrowIfCancellationRequested();

                var result = RuleMatcher.Evaluate(rule, article);
                if (!result.IsMatch)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(article.TorrentUrl))
                {
                    _log.Write($"RSS auto-downloader: '{rule.Name}' matched '{article.Title}' but article has no torrent URL.",
                        LogSeverity.Warning);
                    continue;
                }

                var add = await _session.AddAsync(new AddTorrentParams
                {
                    Source = article.TorrentUrl!,
                    SavePath = savePath!,
                    StartImmediately = true,
                }, ct).ConfigureAwait(false);

                if (!add.IsSuccess)
                {
                    _log.Write($"RSS auto-downloader: add failed for '{article.Title}' ({rule.Name}): {add.Error}",
                        LogSeverity.Warning);
                    continue;
                }

                _log.Write($"RSS auto-downloader: added '{article.Title}' via rule '{rule.Name}'.",
                    LogSeverity.Normal);

                // Carry smart-filter state forward so the next batch of articles against the
                // same rule sees the updated PreviouslyMatchedEpisodes.
                rule = rule with
                {
                    LastMatchUtc = article.PublishedUtc == default ? DateTime.UtcNow : article.PublishedUtc,
                    PreviouslyMatchedEpisodes = result.NewEpisodeTags.Count == 0
                        ? rule.PreviouslyMatchedEpisodes
                        : rule.PreviouslyMatchedEpisodes.Concat(result.NewEpisodeTags).ToArray(),
                };
            }

            if (!ReferenceEquals(rule, original))
            {
                await _rules.UpsertAsync(rule, ct).ConfigureAwait(false);
            }
        }
    }
}
