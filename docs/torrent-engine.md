# Torrent engine (MonoTorrent)

How WinBit wraps MonoTorrent and how qBittorrent concepts map across.

## Choice and rationale

If MonoTorrent gaps block a feature, document the gap in the "Gaps & caveats" section below; don't silently remove the feature from the UI.

## Concept mapping (filled in M3)

| qBittorrent | MonoTorrent | WinBit.Core |
|---|---|---|
| `BitTorrent::Session` | `ClientEngine` | `ITorrentSessionService` |
| `BitTorrent::Torrent` | `TorrentManager` | `TorrentHandle` |
| `BitTorrent::TorrentInfo` | `Torrent` (metadata) | (opaque — exposed via `TorrentHandle` getters) |
| `BitTorrent::AddTorrentParams` | `TorrentSettingsBuilder` + start-path | `AddTorrentParams` |
| resume data (bencoded / DB) | `FastResume` | SQLite BLOB via `ITorrentStateStore` |
| `BitTorrent::Tracker` | `TrackerTier` / `Tracker` | `TrackerInfo` |
| `BitTorrent::PeerInfo` | `PeerId` | `PeerInfo` |
| `BitTorrent::InfoHash` | `InfoHashes` | `InfoHash` |

## Status polling

MonoTorrent exposes real-time state via `TorrentManager` properties + periodic events. WinBit uses `StatusPollingLoop` (1 Hz `PeriodicTimer`) in `WinBit.Core.Hosting` to snapshot every torrent into `TorrentSnapshot[]` and batch-raise `TorrentUpdated` once per tick.

- Snapshots include: state, progress, bytes down/up, speed down/up, ratio, ETA, seed count, peer count.
- Per-tab polls (peers, trackers, pieces) run at 3 s and only while the tab is visible.

## Fast-resume

- Blob stored in SQLite (`torrent_state` table, column `fast_resume BLOB`).
- Schema version column guards MonoTorrent format changes: on version mismatch, discard the blob and re-check.
- Autosave every 60 s + on graceful shutdown.

## Gaps & caveats (spiked and updated in M3)

*To be filled during M3 spike. Expected areas to investigate:*

- **BEP 52 (v2) / hybrid torrents.** Does MonoTorrent expose both info-hashes on a hybrid torrent? Can it add by v2-only magnet?
- **Super-seeding.** qBittorrent exposes it; MonoTorrent status unknown.
- **Choking algorithms.** MonoTorrent's choker is configurable but may not match qBittorrent's `fast_extent`/`anti_leech` modes. Expose what maps; disable the rest with a tooltip.
- **uTP / TCP balance.** MonoTorrent supports both; verify fallback behavior matches.
- **UPnP / NAT-PMP.** MonoTorrent has built-in port mapping; if stale, fall back to `Open.Nat`.
- **Encryption.** Verify BEP-8 encryption mode matches qBittorrent's three options (disabled, enabled, forced).
- **Piece-pick extents, sequential download, first/last piece priority.** MonoTorrent supports all three; confirm API.
- **IP filter.** MonoTorrent accepts peer filters via `IPeerConnection` filter delegate — wire the `PeerGuardian` parser through it.

## Error handling

- Add-torrent failure (invalid file, unreachable magnet, disk full) returns `Result.Failure(...)` — VMs render as an `InfoBar` on the Add dialog, not a toast.
- Engine crashes are logged to `ILogService` and surfaced as a modal recovery dialog on next launch.

## Testing

- Unit tests use MonoTorrent's test scaffolding to spin up a loopback tracker + peer.
- End-to-end: add magnet, observe snapshot transitions, restart app, verify resume without re-check.
