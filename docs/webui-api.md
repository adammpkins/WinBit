# Web UI API

Target: byte-compatible with qBittorrent's v2 HTTP API so third-party clients (`qbittorrent-api`, Sonarr, Radarr, Lidarr, etc.) work against WinBit without modification.

Delivered in **M10**.

## Host

In-process **Kestrel** via `WebUiService : IHostedService`. Started/stopped with the app host. Configuration surfaces through `AppSettings.WebUi` (enabled, port, HTTPS cert path, whitelist).

## Routes (planned)

Mirrors qBittorrent v2 (see qBittorrent's WebUI API surface for reference controllers):

| Route prefix | Controller (Core) | qBittorrent reference |
|---|---|---|
| `/api/v2/auth/*` | `AuthController` | `authcontroller.cpp` |
| `/api/v2/app/*` | `AppController` | `appcontroller.cpp` |
| `/api/v2/transfer/*` | `TransferController` | `transfercontroller.cpp` |
| `/api/v2/torrents/*` | `TorrentsController` | `torrentscontroller.cpp` |
| `/api/v2/sync/*` | `SyncController` | `synccontroller.cpp` |
| `/api/v2/log/*` | `LogController` | `logcontroller.cpp` |
| `/api/v2/rss/*` | `RssController` | `rsscontroller.cpp` |
| `/api/v2/search/*` | `SearchController` | `searchcontroller.cpp` |
| `/api/v2/torrentcreator/*` | `TorrentCreatorController` | `torrentcreatorcontroller.cpp` |

## Authentication

- Cookie session (`SID`) identical to qBittorrent.
- Password stored as **PBKDF2**(password, salt, 100 000 iters, SHA-256) — the default for qBittorrent 4.6+.
- `Referer` check (configurable) on state-changing requests.
- Optional IP subnet whitelist bypasses auth entirely for localhost / LAN.

## HTTPS

- Optional self-signed cert (generated on first HTTPS enable) or user-supplied PFX.
- HTTP/2 enabled.
- HSTS off by default (self-signed friendly); configurable.

## Static content

Ship qBittorrent's HTML admin UI (from qBittorrent's WebUI templates) as static files served by Kestrel. We already implement the API it talks to — the UI "just works." Post-M12 backlog item: replace with a native-feel web client.

## Compatibility oracle

CI runs a subset of `qbittorrent-api` Python client scenarios against a live WinBit instance. Any behavioral drift fails the build.

## Not implemented

- `torrents/info` with the rarely-used `hashes` filter edge cases (document in M10).
- Legacy `/api/v1/*` routes. v2 only.
