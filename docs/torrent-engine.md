# Torrent engine (libtorrent-rasterbar)

How WinBit wraps libtorrent-rasterbar via LibtorrentSharp and how qBittorrent concepts map across.

## Choice and rationale

## Engine alternatives evaluation (2026-04)

### Scope

In April 2026 we re-evaluated the engine choice against three alternatives: **BitSwarm**, other maintained .NET libraries, and a from-scratch engine. Trigger: a question about whether MonoTorrent is dated/unmaintained and whether BitSwarm (which claims BEP 52 support) would be a better foundation. This section captures the finding so the decision is not relitigated without new evidence.

### MonoTorrent status (April 2026)

- Stable **v3.0.2 released 2024-08-04**; pre-release **v3.0.3-beta-0049 released 2024-09-14**. Repository is 3,760 commits deep with 36 open issues — normal OSS hygiene, not stagnation.
- **BEP 52 (BitTorrent V2) is implemented.** Hybrid V1/V2 torrents are supported; `MonoTorrent.InfoHashes` already exposes both V1 (SHA-1) and V2 (SHA-256) hashes (see "Gaps & caveats" below).
- License: MIT.
- Real weakness: the project is essentially **one maintainer** (Alan McGovern). Bus factor is the concrete risk, not code quality.

### BitSwarm — unsuitable

- Last release **2021-03-05**, roughly five years dormant.
- Designed as a streaming *downloader*. Does **not support seeding / uploading** — the author's own remark: "an arrogant and selfish beggar." A qBittorrent replacement without upload is a non-starter.
- Missing: encryption (MSE), IPv6, proxy, UPnP / NAT-PMP, rate limiting.
- License: LGPL-3.0.

### Other .NET libraries

- **TorrentCore** — self-described "work-in-progress with likely bugs and missing features." Not production-grade.
- **TorrentSwifter** — explicit "toy project for learning."
- **System.Net.Torrent / bzTorrent / torrent-client-for-net** — deprecated or unmaintained.

In short: MonoTorrent is the only production-grade **.NET** option.

### libtorrent-rasterbar — the global standard

- C++ library, BSD-3-Clause, **v2.0.12 released 2026-03-13**.
- Used by qBittorrent itself, Deluge, and the majority of non-Windows clients. Multi-maintainer. ~20 years of real-world swarm exposure.
- **Gap for WinBit:** no production-grade C# binding exists. Candidates to triage (none validated yet):
    - `aspriddell/csdl` — server-focused C# wrapper; claims native libs for Windows/macOS/Linux x64+arm64.
    - `ligenq/LibtorrentDotNet` — C++/CLI wrapper, Windows-only.
    - `vktr/libtorrent-net` — slim .NET bindings, older.
    - `LibtorrentRTWrapper` (NuGet) — Windows C++ wrapper.
- Adopting libtorrent means either using one of these wrappers and inheriting its maintenance health, or writing our own C++/CLI or P/Invoke binding — plus shipping native `x64` + `arm64` binaries inside the MSIX.

### Decision

**Switched to libtorrent-rasterbar (2026-04-27).** The LibtorrentSharp binding on `engine/libtorrent-bindings` reached sufficient parity with `ITorrentSessionService` (cold-start loader, peers/trackers tabs, sequential download, session stats) to replace MonoTorrent on `main`. MonoTorrent has been removed. One gap at switch time: `TorrentCreatorService` has no libtorrent equivalent yet — it is disabled pending Phase G of `LIBTORRENT_TASKS.md`.

## Concept mapping

| qBittorrent | libtorrent (LibtorrentSharp) | WinBit.Core |
|---|---|---|
| `BitTorrent::Session` | `session` via `LibtorrentSharp.Session` | `ITorrentSessionService` |
| `BitTorrent::Torrent` | `torrent_handle` | `TorrentHandle` |
| `BitTorrent::TorrentInfo` | `torrent_info` | (opaque — exposed via `TorrentHandle` getters) |
| `BitTorrent::AddTorrentParams` | `add_torrent_params` | `AddTorrentParams` |
| resume data (bencoded / DB) | `add_torrent_params::resume_data` blob | SQLite BLOB via `ITorrentStateStore` |
| `BitTorrent::Tracker` | `announce_entry` | `TrackerInfo` |
| `BitTorrent::PeerInfo` | `peer_info` | `PeerInfo` |
| `BitTorrent::InfoHash` | `info_hash_t` | `InfoHash` |

## Status polling

LibtorrentSharp exposes real-time state via libtorrent alerts + a synchronous status call. WinBit uses `StatusPollingLoop` (1 Hz `PeriodicTimer`) in `WinBit.Core.Hosting` to snapshot every torrent into `TorrentSnapshot[]` and batch-raise `TorrentUpdated` once per tick.

- Snapshots include: state, progress, bytes down/up, speed down/up, ratio, ETA, seed count, peer count.
- Per-tab polls (peers, trackers, pieces) run at 3 s and only while the tab is visible.

## Fast-resume

- Blob stored in SQLite (`torrent_state` table, column `fast_resume BLOB`).
- Schema version column guards format changes: on version mismatch, discard the blob and re-check (the engine will hash-check from scratch — no corruption possible).
- MonoTorrent blobs from before the engine swap were discarded on first launch (version mismatch); torrents re-checked automatically.
- Autosave every 60 s + on graceful shutdown.

## Gaps & caveats (post engine-swap, libtorrent-rasterbar)

> Historical MonoTorrent gap notes from M3 are no longer applicable. Libtorrent covers all features MonoTorrent lacked (sequential download, per-protocol settings, super-seeding force, etc.). Known libtorrent gaps at the switch point:

- **TorrentCreatorService** — MonoTorrent had `TorrentCreator`; libtorrent exposes torrent creation through `create_torrent` in C++, not yet wrapped in LibtorrentSharp. The "Create torrent" feature is non-functional until Phase G of `LIBTORRENT_TASKS.md` ships. **WinBit action:** disable the create-torrent UI entry point until the binding catches up.

- **DhtBootstrapSeeder / DhtNetworkProbe** — these previously used MonoTorrent's `IDhtEngine`. LibtorrentSharp exposes DHT via `session_stats` alerts; the hosted services need porting. Tracked in Backlog `TASKS.md`.

- **IP filter** — libtorrent has `ip_filter` / `set_ip_filter`; not yet wired through LibtorrentSharp. The M7 `PeerGuardianParser` deliverable will produce an `IPBlockSet`; once the binding exposes the filter hook, plumb it through `LibTorrentSessionService`.

## Error handling

- Add-torrent failure (invalid file, unreachable magnet, disk full) returns `Result.Failure(...)` — VMs render as an `InfoBar` on the Add dialog, not a toast.
- Engine crashes are logged to `ILogService` and surfaced as a modal recovery dialog on next launch.

## Testing

- Unit tests in `WinBit.Tests/` cover engine-agnostic Core behavior (persistence, settings, RSS, etc.). No MonoTorrent test scaffolding remains.
- LibtorrentSharp binding tests live in `libtorrentsharp/LibtorrentSharp.Tests/`. Network-category tests `Skip` when `lts.dll` is absent — run them manually before any flag flip.
- End-to-end: add magnet, observe snapshot transitions, restart app, verify resume without re-check.

## Appendix: libtorrent-rasterbar spike results (historical)

This appendix covers the research and pivot that led to WinBit owning its own binding. The spike is complete; LibtorrentSharp is now the production engine on `main`.

### Step 1 — C# wrapper triage (2026-04)

Four candidates surveyed; data drawn from public GitHub / NuGet pages, source inspection via `gh api`.

| Wrapper | Last release | Platforms | Seeding | License | Status |
|---|---|---|---|---|---|
| [`aspriddell/csdl`](https://github.com/aspriddell/csdl) | v1.2.1 (2024-09-21) | Windows, macOS, Linux — **x64 + arm64** | ✅ | Apache-2.0 | Active; 321 commits, 2+ devs; libtorrent ≥ 2.0.11 |
| [`ligenq/LibtorrentDotNet`](https://github.com/ligenq/LibtorrentDotNet) | v1.1.0 (2024-12-07) | Windows x64 **only** | Unknown | BSD-3 | Recent but thin — only 12 total commits; C++/CLI |
| [`vktr/libtorrent-net`](https://github.com/vktr/libtorrent-net) | No releases | Unknown | Unknown | MIT | **Abandoned** — 7 commits, no releases, no NuGet |
| [`sakib1361/LibTorrentNet`](https://github.com/sakib1361/LibTorrentNet) (NuGet `LibtorrentRTWrapper`) | v1.0.2 (2025-10-28) | Windows, net9.0 | ❌ streaming-focused | MIT | Streaming-only, same disqualifier as BitSwarm |

#### Frontrunner: csdl

Source layout under `csdl/`: `TorrentClient.cs` (10 KB, session surface), `SettingsPack.cs`, `TorrentClientConfig.cs`, `TorrentInfo.cs`, `TorrentManager.cs`, `TorrentStatus.cs`, plus `Alerts/`, `Enums/`, `Native/`. The native companion lives at `aspriddell/csdl-native` (referenced via `vcpkg.json`, targets libtorrent ≥ 2.0.11 + `magic-enum`).

**What csdl exposes on the C# surface:**

- `TorrentClient` — session create/dispose, `AttachTorrent(TorrentInfo, savePath)`, `DetachTorrent`, `UpdateSettings(SettingsPack)`, `AlertRaised` event, `ActiveTorrents` enumeration.
- `TorrentInfo` — load from file path or byte[]; exposes `TorrentMetadata` (name, creator, total size, info-hash SHA-1 **and** SHA-256) and `Files`.
- `TorrentManager` — `Start()` / `Stop()`, `GetCurrentStatus()`, `ReannounceAllTrackers(interval, force)`, per-file `Priority`.
- `TorrentStatus` — `State`, `Progress`, `PeerCount`, `SeedCount`, `BytesUploaded`, `BytesDownloaded`, `UploadRate`, `DownloadRate`.
- `SettingsPack` — raw key/value passthrough to libtorrent's `settings_pack`. This is the escape hatch: **anything libtorrent configures via `settings_pack` (DHT, LSD, PEX, encryption, proxy, UPnP, global rate limits, port ranges, etc.) is reachable by string key**.

**Gaps vs. `ITorrentSessionService` (confirmed missing in the current C# surface):**

1. **No magnet link support.** `TorrentInfo` only loads from .torrent file/bytes. libtorrent has `parse_magnet_uri` + `add_magnet_uri`; csdl doesn't pipe it through.
2. **No fast-resume save / load.** libtorrent has `save_resume_data` + `add_torrent_params::resume_data`; csdl exposes neither. WinBit's M3 fast-resume persistence would need adding to the native + managed layers.
3. **No force-recheck API.** `force_recheck()` on `torrent_handle` is not wrapped.
4. **No peer list enumeration.** Needed for the M4 Peers tab.
5. **No tracker list enumeration.** Needed for the M4 Trackers tab.
6. **Minimal `TorrentStatus` struct.** Missing ratio, ETA, per-file progress beyond priority, active-time / seeding-time (M5 share-limit inputs).
7. **No per-torrent rate limits.** Only global via `settings_pack`. MonoTorrent's per-torrent `MaximumDownloadRate` / `MaximumUploadRate` would have to be emulated.
8. **No super-seeding toggle, no sequential download flag, no first/last piece priority.**
9. **No move-storage** operation.

**Verdict:** csdl is a reasonable *starting reference* — a small, readable, Apache-2.0, actively maintained wrapper with real x64+arm64 native binaries — but **not a drop-in replacement** for MonoTorrent. Reaching parity with `ITorrentSessionService` would require forking both `csdl` and `csdl-native` to:

- Extend the C ABI (`csdl-native`) with: magnet add, save/load resume data, force recheck, peer/tracker enumeration, per-torrent rate limits, super-seeding, sequential/first-last piece flags, richer status struct, move-storage.
- Extend the C# wrapper (`csdl`) to expose those new natives.

Rough sizing: **~2–4 weeks of C++ + C# wrapper work before Step 3** (prototype `LibTorrentSessionService`) could begin, then the ≥5 days of Step 3 itself. Total to an equivalent of today's MonoTorrent integration: **6–8 weeks**, before any WinBit-side rewrite.

The alternative — writing our own binding of libtorrent directly via a custom C ABI + P/Invoke — is in the same ballpark, maybe slightly more upfront for a cleaner long-term surface.

#### Other candidates — briefly

- **LibtorrentDotNet** — recent, C++/CLI, but 12 commits total and no arm64 disqualifies it against csdl for WinBit's roadmap ("ARM64 first-class support" is a post-M12 backlog item). Would inherit the same scope-of-extension problem. No real advantage over csdl.
- **libtorrent-net** — dead.
- **LibtorrentRTWrapper** — streaming-only; same non-starter as BitSwarm.

### Step 1 decision

Use `aspriddell/csdl` as the reference implementation for Step 2 (minimal native build). If Step 2 confirms the vcpkg + CMake build reproduces on our Windows dev box, Step 3 (`LibTorrentSessionService` prototype) starts by forking csdl to fill the magnet + resume + recheck gaps — those three are the minimum to demo a live torrent through `ITorrentSessionService`.

### Pivot (2026-04): scope upgraded from "spike" to "own the binding"

After Step 1 surfaced csdl's gaps, the direction shifted: rather than settle for a thin download-focused wrapper, **WinBit will own a comprehensive .NET binding to libtorrent-rasterbar**. To be explicit: the binding wraps the existing C++ libtorrent-rasterbar library over a C ABI and P/Invoke — **we are not reimplementing libtorrent in C#**. libtorrent itself stays the protocol engine.

- Start from csdl's structure (C ABI in a `-native` package, P/Invoke layer in the managed project) as a known-working vcpkg + CMake reference.
- Extend the C ABI to cover everything `ITorrentSessionService` needs that csdl omits (magnets, resume data, recheck, peer/tracker enumeration, per-torrent rate limits, super-seeding, sequential / first-last, move-storage, richer status struct).
- Attribution: csdl is Apache-2.0 (Albie Spriddell); any vendored code carries a `NOTICE` with original copyright preserved.
- Home for the work: branch `engine/libtorrent-bindings`, cut from `spike/libtorrent-rasterbar-eval`. Scaffold + binding code developed there; merged to `main` in April 2026 once parity was sufficient.

This changed the TASKS.md Backlog entry from a bounded spike into a post-M12 engine initiative that shipped. MonoTorrent has been removed from `main`.

### Steps 2–5 — complete

The owned-binding track shipped. Steps 2–5 (minimal native build → prototype `LibTorrentSessionService` → close integration gaps → merge to `main`) all landed on `engine/libtorrent-bindings` before the branch was merged. See `LIBTORRENT_TASKS.md` for the detailed task history.
