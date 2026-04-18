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

## Gaps & caveats (M3 spike, MonoTorrent 3.0.2)

Verified against the MonoTorrent XML docs shipped with `MonoTorrent` 3.0.2 (`lib/net6.0/`).

- **BEP 52 (v2) / hybrid torrents — full support.** `MonoTorrent.InfoHashes` exposes both `V1` (SHA-1) and `V2` (SHA-256); `V1OrV2` prefers V1 when both exist. `MonoTorrent.TorrentType` covers `V1Only`, `V2Only`, and `V1V2Hybrid`. Per-file Merkle tree roots ship with `TorrentFile.PiecesRoot` (SHA-256). Adding by v2-only magnet works via the standard `ClientEngine.AddAsync(MagnetLink, …)`. **WinBit action:** persist both hashes in `InfoHashes` and use `V1OrV2` for display/identity. No UI feature to disable.

- **Encryption modes — clean mapping.** `EngineSettings.AllowedEncryption` is an ordered `IList<EncryptionType>` of {`PlainText`, `RC4Header`, `RC4Full`}. Preference is set by list order; requirement is set by *exclusion*.
    - qBittorrent *Prefer* → `[RC4Header, RC4Full, PlainText]` (default).
    - qBittorrent *Require* → `[RC4Header, RC4Full]` (plaintext rejected).
    - qBittorrent *Disable* → `[PlainText]`.
    - **WinBit action:** ship all three options; the Settings/BitTorrent `Encryption` combo maps directly.

- **UPnP / NAT-PMP — single toggle, both protocols.** `EngineSettings.AllowPortForwarding` (bool) activates the built-in `IPortForwarder`, which discovers UPnP and NAT-PMP devices jointly. Individual active mappings are inspectable via `ClientEngine.PortMappings`. MonoTorrent doesn't expose per-protocol toggles. **WinBit action:** keep the single *UPnP / NAT-PMP* toggle in Settings/Connection; log mapping failures to `ILogService` so users can diagnose from the Logs page. `Open.Nat` fallback not needed — in-box support is sufficient.

- **Super-seeding — partial / automatic only.** `TorrentSettings.AllowInitialSeeding` is the closest analogue. Semantics: Initial Seeding (BEP-16 "superseed") engages **only when there are no other seeders in the swarm** and the local torrent is complete. qBittorrent's UI exposes a manual Super-Seeding toggle that can be forced even when other seeders exist. **Gap:** MonoTorrent does not let us force super-seeding while other seeders exist. **WinBit action:** expose the toggle labelled *Allow initial seeding* with a tooltip explaining the automatic behavior; omit the "force" path from parity with a TODO if users request it.

- **Choking algorithms — not configurable.** `ChokeUnchokeManager` is internal. `TorrentSettings.UploadSlots` caps simultaneous unchoked peers, but there is no hook for qBittorrent's `fast_extent`, `anti_leech`, or custom unchoke modes. **Gap:** WinBit will not offer advanced choking-algorithm selection. **WinBit action:** drop those controls from the Advanced settings page; surface only `UploadSlots`.

- **uTP / TCP — both enabled by default.** `ConnectionType` in MonoTorrent covers TCP and uTP with automatic fallback; no user-facing knob is required to match qBittorrent's "Enable uTP" / "Mixed mode" defaults.

- **Piece picking extents — supported.** `TorrentSettings` surfaces the relevant toggles. Sequential download and first/last piece priority are configured via `TorrentManager.SetFilePriorityAsync` + custom piece-picker selection (`ChangePickerAsync`). **WinBit action:** map the M4 Add-Torrent dialog's sequential / first-last checkboxes straight through.

- **IP filter — delegate-based.** MonoTorrent accepts a peer-connection filter at engine construction. The `PeerGuardianParser` M7 deliverable will produce an `IPBlockSet` that this delegate consults on every incoming peer. **WinBit action:** plumb the parser through `EngineSettingsBuilder` when implementing M7.

### Features with no MonoTorrent parity that we accept

- Custom choking modes ("fast extent", "anti-leech").
- Forced super-seeding while other seeders are present.
- Per-protocol UPnP / NAT-PMP toggles.

The Settings sub-pages and Add-Torrent dialog must not expose controls for any of the above — exposing a switch that does nothing is worse than omitting the feature.

## Error handling

- Add-torrent failure (invalid file, unreachable magnet, disk full) returns `Result.Failure(...)` — VMs render as an `InfoBar` on the Add dialog, not a toast.
- Engine crashes are logged to `ILogService` and surfaced as a modal recovery dialog on next launch.

## Testing

- Unit tests use MonoTorrent's test scaffolding to spin up a loopback tracker + peer.
- End-to-end: add magnet, observe snapshot transitions, restart app, verify resume without re-check.
