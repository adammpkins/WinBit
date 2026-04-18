# Persistence

File-system layout, SQLite schema, JSON shapes.

## Location

All user data lives under `%LOCALAPPDATA%\WinBit\`:

```
%LOCALAPPDATA%\WinBit\
├── settings.json                 # AppSettings
├── state.db                      # SQLite: torrents, fast-resume, logs
├── categories.json               # category name → CategoryOptions
├── tags.json                     # tag list
├── watched-folders.json          # path → WatchedFolderOptions
├── ip-filter.p2p                 # optional user blocklist
├── rss\
│   ├── feeds.json                # RSS tree (folders + feeds + refresh config)
│   └── articles.db               # SQLite: articles, last-read, rule-matches
└── logs\                         # rotating text logs (diagnostic only; live log is in state.db)
```

`Paths.cs` creates missing directories on first use.

## SQLite (`state.db`)

WAL mode. All writes serialized through a single writer queue (`SqliteWriteQueue`). Reads use a separate read-only connection pool.

### Tables (M2 schema — `SqlMigrations/001_init.sql`)

```sql
CREATE TABLE schema_version (version INTEGER NOT NULL);
INSERT INTO schema_version VALUES (1);

CREATE TABLE torrent (
    info_hash     TEXT    PRIMARY KEY,
    name          TEXT    NOT NULL,
    save_path     TEXT    NOT NULL,
    category      TEXT,
    tags          TEXT,                  -- JSON array
    added_utc     TEXT    NOT NULL,
    completed_utc TEXT,
    is_sequential INTEGER NOT NULL DEFAULT 0,
    first_last    INTEGER NOT NULL DEFAULT 0,
    dl_limit_bps  INTEGER NOT NULL DEFAULT 0,
    ul_limit_bps  INTEGER NOT NULL DEFAULT 0,
    share_limits  TEXT,                  -- JSON ShareLimits
    torrent_blob  BLOB,                  -- cached .torrent bytes
    fast_resume   BLOB,                  -- MonoTorrent fast-resume
    resume_ver    INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE log_entry (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    ts_utc    TEXT    NOT NULL,
    severity  INTEGER NOT NULL,
    message   TEXT    NOT NULL
);

CREATE TABLE peer_log (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    ts_utc    TEXT    NOT NULL,
    ip        TEXT    NOT NULL,
    reason    TEXT    NOT NULL,
    blocked   INTEGER NOT NULL
);
```

Later milestones add: `rss_article`, `rss_rule_match`, `stats_rollup`.

## JSON (`settings.json`)

Shape mirrors `WinBit.Core.Settings.AppSettings`:

```json
{
  "Downloads": { "defaultSavePath": "…", "autoTmmEnabled": false, "preallocate": false, … },
  "Connection": { "listenPort": 6881, "upnp": true, "proxy": { "type": "None", … } },
  "Speed": { "globalDownBps": 0, "globalUpBps": 0, "altDownBps": 0, "altUpBps": 0, "scheduler": { … } },
  "BitTorrent": { "dht": true, "pex": true, "lsd": true, "encryption": "Prefer", … },
  "Rss": { "enabled": true, "refreshIntervalMinutes": 30, "maxArticlesPerFeed": 100, "autoDownloader": true },
  "WebUi": { "enabled": false, "port": 8080, "https": false, "whitelistedSubnets": [] },
  "Advanced": { "asyncIoThreads": 4, … },
  "UiState": { "theme": "System", "accentColor": null, "columnLayout": { … }, "sidebarWidth": 240 }
}
```

Writes are **atomic**: serialize to `settings.json.tmp`, flush, rename over `settings.json`. Saves debounce 500 ms after the last `SettingsService.Update` call.

## RSS storage

- `rss/feeds.json` — tree structure (`RssFolder` → `RssFeed`[]), with refresh interval override per feed.
- `rss/articles.db` — articles grow unboundedly in naive stores; SQLite with per-feed retention (last N articles, matching `AppSettings.Rss.maxArticlesPerFeed`) keeps size bounded.

## Migration strategy

- `schema_version` row in SQLite gates migrations.
- JSON is forward-compatible (`System.Text.Json` ignores unknown fields by default, and we only add optional fields).
- **No qBittorrent migration path.** Fresh users only.

## Backup & export

Deferred to post-M12 backlog. When implemented, export bundles `settings.json` + selected tables from `state.db` as a single archive.
