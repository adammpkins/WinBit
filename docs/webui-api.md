# Web UI API

Target: byte-compatible with qBittorrent's v2 HTTP API so third-party clients (`qbittorrent-api`, Sonarr, Radarr, Lidarr, etc.) work against WinBit without modification.

Delivered in **M10**.

## Host

In-process **Kestrel** via `WebUiService : IHostedService`. Started/stopped with the app host. Configuration surfaces through `AppSettings.WebUi` (enabled, port, HTTPS cert path, whitelist).

## Routes

Mirrors the qBittorrent v2 API surface:

| Route prefix | Controller (Core) |
|---|---|
| `/api/v2/auth/*` | `AuthController` |
| `/api/v2/app/*` | `AppController` |
| `/api/v2/transfer/*` | `TransferController` |
| `/api/v2/torrents/*` | `TorrentsController` |
| `/api/v2/sync/*` | `SyncController` |
| `/api/v2/log/*` | `LogController` |
| `/api/v2/rss/*` | `RssController` |
| `/api/v2/search/*` | `SearchController` |
| `/api/v2/torrentcreator/*` | `TorrentCreatorController` |

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

WinBit ships its own native Vue SPA (built from `webui/`) as the WebUI experience, served by Kestrel as embedded static content.

## Compatibility oracle

CI runs a subset of `qbittorrent-api` Python client scenarios against a live WinBit instance. Any behavioral drift fails the build.

## Not implemented

- `torrents/info` with the rarely-used `hashes` filter edge cases (document in M10).
- Legacy `/api/v1/*` routes. v2 only.
