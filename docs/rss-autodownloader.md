# RSS auto-downloader

Rule semantics for the RSS auto-download engine, ported from qBittorrent's reference behavior at qBittorrent's RSS auto-download rule.

Delivered in **M9**.

## Rule shape

```csharp
public sealed class AutoDownloadRule
{
    public string Name { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public IReadOnlyList<string> AffectedFeeds { get; init; } = [];

    // Content filters
    public string MustContain { get; init; } = "";          // space-separated OR; "|" nests as AND-groups
    public string MustNotContain { get; init; } = "";
    public bool UseRegex { get; init; } = false;

    // Episode filter (seasonal TV)
    public string EpisodeFilter { get; init; } = "";        // e.g. "1x2;5-8"

    // Smart episode filter — suppresses re-download of same episode
    public bool SmartFilter { get; init; } = false;
    public int EpisodeHistoryDays { get; init; } = 0;

    // Routing
    public string? AssignedCategory { get; init; }
    public string? SavePath { get; init; }
    public TriState AddPaused { get; init; } = TriState.Default;
    public TriState CreateSubfolder { get; init; } = TriState.Default;
    public TorrentContentLayout? ContentLayout { get; init; }

    // Scheduling
    public DayOfWeek[]? IgnoreDays { get; init; }
    public TimeOnly? IgnoreFrom { get; init; }
    public TimeOnly? IgnoreTo { get; init; }

    // Telemetry
    public DateTime? LastMatchUtc { get; init; }
    public IReadOnlyList<string> PreviouslyMatchedEpisodes { get; init; } = [];
}
```

## Matching algorithm

```
for each article in feed:
    if !rule.Enabled: continue
    if !rule.AffectedFeeds.Contains(feed.Url): continue
    if inIgnoreWindow(now, rule): continue

    title = article.Title

    if !TokenMatch(title, rule.MustContain, rule.UseRegex): continue
    if TokenMatch(title, rule.MustNotContain, rule.UseRegex): continue

    if rule.EpisodeFilter is not empty:
        if !EpisodeFilterMatches(title, rule.EpisodeFilter): continue

    if rule.SmartFilter:
        episode = ExtractEpisodeTag(title)
        if episode is null: continue
        if episode in rule.PreviouslyMatchedEpisodes: continue
        if rule.EpisodeHistoryDays > 0:
            if MatchedWithin(rule, episode, rule.EpisodeHistoryDays): continue

    => MATCH
```

### Token match

- Space-separated tokens are **AND**.
- `|` separates OR-groups: `foo bar | baz` ≡ `(foo AND bar) OR (baz)`.
- If `UseRegex`, each token is a regex; title matches iff each AND-token regex matches anywhere.

### Episode filter

Format mirrors qBittorrent: `1x2`, `1x2-5`, `1x2-`, `1x2;3-5` (semicolon-separated), season `x` episode. Comparisons are case-insensitive.

### Smart episode filter

Strips noise (release-group tags in brackets, resolution like `1080p`, codec like `x264`), canonicalizes episode as `SxxEyy`, compares against `PreviouslyMatchedEpisodes`. Re-download window (`EpisodeHistoryDays`) prevents same-episode re-downloads inside N days.

## Persistence

Rules live in `%LOCALAPPDATA%\WinBit\rss\rules.json`. `PreviouslyMatchedEpisodes` and `LastMatchUtc` update after each match and are written back atomically.

## Testing

Parity tests against fixtures derived from qBittorrent's test suite (`qbittorrent/test/testrssautodownloadrule.cpp`). Each rule example in our tests has a qBittorrent-behavior comment as the oracle.
