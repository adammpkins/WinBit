# LibtorrentSharp — design

Companion to `docs/torrent-engine.md` (see [Engine alternatives evaluation (2026-04)](./torrent-engine.md#engine-alternatives-evaluation-2026-04)). This document defines the architecture and scope of **LibtorrentSharp**, a .NET binding to libtorrent-rasterbar that WinBit is building on `engine/libtorrent-bindings`.

## Goal

A comprehensive .NET binding that wraps the existing **C++ libtorrent-rasterbar** library. Long-term target: **full-fidelity coverage** of libtorrent's public client-facing surface so the binding is useful to anyone building a BitTorrent-powered .NET app, not just WinBit. Short-term: WinBit's `ITorrentSessionService` is the first-consumer forcing function, so the surface gets filled in WinBit-needed order.

Explicit non-goals:
- **Not** a C# reimplementation of the BitTorrent protocol. libtorrent stays the protocol engine.
- **Not** a reflection of libtorrent's *internal* API — plugin API (`session_plugin`, `torrent_plugin`), test hooks, and raw bdecode/bencode are out of scope. `settings_pack` stays string-keyed passthrough (that *is* full fidelity — there are ~200 keys, and libtorrent's docs are the canonical reference).
- **Not** WinBit-coupled. No `WinBit.*` references leak into LibtorrentSharp. The project is shaped as a standalone NuGet from day one.
- **Not** cross-platform in the first pass. Windows x64 + arm64 are the initial native targets; Linux and macOS can be added later (csdl already proves they build).

## Ownership & extraction plan

LibtorrentSharp lives inside the WinBit repository **for now**, under `libtorrentsharp/`, as a self-contained sub-project:

```
libtorrentsharp/
    LibtorrentSharp/              # managed project (AnyCPU, net8.0)
    LibtorrentSharp.Native/       # native C++ project (CMake + vcpkg, Windows x64/arm64)
    README.md
    NOTICE                        # Apache-2.0 attribution to Albie Spriddell (csdl)
```

When the surface stabilizes and the binding can stand on its own, the `libtorrentsharp/` directory extracts to a dedicated GitHub repository, publishes to NuGet, and WinBit consumes it as a package ref. The in-repo location is a temporary staging area — nothing in LibtorrentSharp may reference `WinBit.*` types, settings, or conventions.

## Starting point: csdl (fork & extend)

[`aspriddell/csdl`](https://github.com/aspriddell/csdl) (Apache-2.0) provides a working reference:
- A C ABI shared library (`native/`) with 17 functions over libtorrent types as opaque pointers.
- A C# P/Invoke layer (`csdl/`) with `TorrentClient`, `TorrentInfo`, `TorrentManager`, `TorrentStatus`, `SettingsPack`.
- vcpkg manifest pinning libtorrent ≥ 2.0.11, CMake build, CI-built x64+arm64 binaries for Windows/macOS/Linux.

WinBit vendors the csdl source under the repo (not a submodule — we diverge meaningfully), renames the native export prefix from `csdl_*` to `lts_*`, carries a `NOTICE` with Apache-2.0 attribution for Albie Spriddell, and extends the C ABI with every function WinBit needs that csdl omits.

## Architecture

```
+------------------------------------------+
|  WinBit.Core                             |
|                                          |
|   ITorrentSessionService                 |  (unchanged)
|      ^                                   |
|      |                                   |
|   LibTorrentSessionService ----+         |  (new, feature-flagged)
+------------------------------- | --------+
                                 |
+--------------------------------v---------+
|  LibtorrentSharp  (managed)     |
|                                          |
|   TorrentSession   TorrentInfo           |  (C# P/Invoke layer, vendored + extended csdl)
|   TorrentHandle    TorrentStatus         |
|   SettingsPack     Alerts                |
|           |                              |
|           | DllImport ("lts")      |
+---------- v -----------------------------+
                                           
+------------------------------------------+
|  LibtorrentSharp.Native  (C++)  |
|                                          |
|   extern "C" {                           |  (vendored + extended csdl-native)
|     lts_create_session(...)        |
|     lts_create_torrent_magnet(...) |
|     ...                                  |
|   }                                      |
|           |                              |
|           | links libtorrent             |
+---------- v -----------------------------+
                                           
+------------------------------------------+
|  libtorrent-rasterbar 2.0.x (via vcpkg)  |
+------------------------------------------+
```

**Why P/Invoke + C ABI rather than C++/CLI?**
- Cleaner managed/unmanaged boundary, no mixed-mode assembly.
- NativeAOT-compatible (post-M12 backlog item for WinBit).
- Mirrors how csdl already works — minimal restructuring.
- The C++/CLI alternative would add MSBuild C++ tooling to the build matrix and doesn't buy much.

## Project layout in the solution

New in `WinBit.slnx`:

- **`libtorrentsharp/LibtorrentSharp/LibtorrentSharp.csproj`** — C# class library, `net8.0`, AnyCPU. Starts from vendored csdl C# sources with the namespace renamed to `LibtorrentSharp`. Adds new P/Invoke declarations as the C ABI grows. **No references to `WinBit.*`** — the binding is an independent library. Ships the native binaries via `runtimes/win-x64/native/` and `runtimes/win-arm64/native/`.
- **`libtorrentsharp/LibtorrentSharp.Native/`** — native shared-library project (`lts.dll`). C++ sources vendored from csdl's `native/` plus new files for the added surface. CMake + vcpkg. Built separately from the managed project; outputs are copied into the managed project's `runtimes/<rid>/native/` at the end of the native build. Not added to `WinBit.slnx` directly (CMake isn't a .slnx citizen); lives as a sibling buildable from its own `CMakeLists.txt`.
- **`WinBit.Core/BitTorrent/LibTorrentSessionService.cs`** — new alternate `ITorrentSessionService` implementation, DI-registered behind a feature flag (`AppSettings.Advanced.UseLibtorrentEngine` or similar). Never enabled on `main` until the binding ships. **This is the only WinBit-aware piece** — the bridge from LibtorrentSharp's public API to WinBit's contracts.
- **`WinBit.Tests/Libtorrent/`** — test fixtures exercising the WinBit adapter; pure binding tests live in a `LibtorrentSharp.Tests` project sibling.
- **`libtorrentsharp/NOTICE`** — Apache-2.0 attribution to Albie Spriddell (csdl).

## C ABI roadmap

csdl's 17 existing functions stay (renamed). These are the additions WinBit needs, prioritized:

### P0 — needed to run a single torrent end-to-end

| Function | libtorrent call | Why |
|---|---|---|
| `lts_add_magnet(session*, const char* magnet_uri, const char* save_path)` | `parse_magnet_uri` + `session::add_torrent` | Magnet support — missing from csdl entirely. |
| `lts_request_resume_data(torrent_handle*)` | `torrent_handle::save_resume_data` | Async; completes via `save_resume_data_alert`. |
| `lts_add_torrent_with_resume(session*, const char* resume_data, int32_t length, const char* save_path)` | `read_resume_data` + `session::add_torrent` | Restart without re-check. |
| `lts_force_recheck(torrent_handle*)` | `torrent_handle::force_recheck` | Context-menu Force Recheck. |
| `lts_pause_torrent` / `lts_resume_torrent` | `torrent_handle::pause` / `resume` | Distinct from stop — libtorrent's auto-managed flag differs. |
| `lts_move_storage(torrent_handle*, const char* path, int32_t flags)` | `torrent_handle::move_storage` | Category TMM moves + manual move. |

### P1 — Peers, Trackers tabs; share-limit enforcement

| Function | libtorrent call | Why |
|---|---|---|
| `lts_get_peers(torrent_handle*, peer_info_list*)` + destroyer | `torrent_handle::get_peer_info` | M4 Peers tab. |
| `lts_get_trackers(torrent_handle*, tracker_info_list*)` + destroyer | `torrent_handle::trackers` | M4 Trackers tab. |
| `lts_get_torrent_status_full(torrent_handle*, extended_status*)` | `torrent_handle::status` | Extended `torrent_status`: ratio, eta, active_time, finished_time, seeding_time, save_path, flags, error_string. csdl's struct is minimal. |
| `lts_set_torrent_upload_limit` / `download_limit` | `torrent_handle::set_upload_limit` / `set_download_limit` | Per-torrent rate limits (M5). |

### P2 — share-limit actions, streaming, inspection

| Function | libtorrent call | Why |
|---|---|---|
| `lts_set_super_seeding(torrent_handle*, bool)` | `torrent_handle::super_seeding` | M5 share-limit action. |
| `lts_set_sequential(torrent_handle*, bool)` | `torrent_handle::set_sequential_download` | Streaming / first-pass UX. |
| `lts_set_file_piece_priority(torrent_handle*, file_index, priority)` | `torrent_handle::file_priority` | First-last piece priority. |
| `lts_get_port_mappings(session*, port_mapping_list*)` | `session::get_status()` port map enumeration | UPnP inspection (M8 parity). |

### Settings passthrough

Everything else — DHT, LSD, PEX, encryption, proxy, UPnP, global rate limits, listen interface, port ranges — goes through the existing `apply_settings(settings_pack*)` path with string keys. This matches csdl's `SettingsPack` and keeps the ABI small.

## C# surface

`LibtorrentSharp` mirrors the roadmap above. `LibTorrentSessionService` (in `WinBit.Core`) is the adapter that maps `ITorrentSessionService` calls onto this layer. It also houses:

- An `AlertPump` translating libtorrent alerts (status updates, errors, resume-data-ready, peer events) into the existing WinBit event model (`TorrentUpdated`, log entries).
- Fast-resume orchestration: request resume data on shutdown + every 60 s, collect via alert, persist via `ITorrentStateStore` (existing).
- Mapping `TorrentState` (libtorrent enum) to WinBit's `TorrentState` — parallel to the existing MonoTorrent mapping.
- `EncryptionMapper` equivalent: maps WinBit `EncryptionMode` to the settings_pack keys (`allowed_enc_level`, `prefer_rc4`, etc.).

## Build pipeline

- **Dependency install:** `vcpkg install libtorrent magic-enum --triplet=x64-windows-static` (and `arm64-windows-static`).
- **Configure:** `cmake -B build -S LibtorrentSharp.Native -DCMAKE_TOOLCHAIN_FILE=<vcpkg>/scripts/buildsystems/vcpkg.cmake`.
- **Build:** `cmake --build build --config Release`.
- **Copy outputs:** `lts.dll` lands in `LibtorrentSharp/runtimes/win-x64/native/` (and `win-arm64/native/`) as part of the csproj build event.
- **MSIX packaging:** the runtimes folder is picked up by the existing publish profile; no changes to `Package.appxmanifest`.

CI is not wired in the first pass. Manual Windows build until the binding proves out, then we add a GitHub Actions matrix.

## Testing strategy

Three layers, each gated by what the binding can actually do at the point the test ships. Expands on step 7 of the feature-parity plan below.

### Agentic execution (non-negotiable)

Every gate in this section — build, test, verify — is **CLI-invocable** and emits **agent-readable outputs**. No IDE steps, no "open this in Visual Studio", no Test Explorer screenshots.

- Managed tests: `dotnet test libtorrentsharp/LibtorrentSharp.Tests/LibtorrentSharp.Tests.csproj --logger "trx;LogFileName=TestResults.trx" --logger "console;verbosity=detailed"`. TRX is a readable artifact; console output is captured in the agent's tool result.
- Native build: `cmake --build libtorrentsharp/LibtorrentSharp.Native/build --config Release`. Artifacts land at a predictable path (`libtorrentsharp/LibtorrentSharp.Native/build/Release/lts.dll`) so the agent can verify existence + mtime.
- Network tests: results go to the same TRX stream. Per-torrent diagnostics (peer counts, alert dumps) go to stdout, prefixed so they survive log scraping. No "watch the UI and see what happens."
- If a test needs a fixture (`.torrent` files, resume blobs), it lives in `LibtorrentSharp.Tests/Fixtures/` and is committed — no out-of-band setup.

If a gate can't be expressed this way, it doesn't belong in the Testing strategy — it belongs in a manual QA checklist somewhere else, and we note that gap explicitly rather than smuggling it in.

### Layer 1 — Managed-only (no native DLL required)

Runs on every commit. Covers enum mappers, settings-key validation, alert-type dispatch logic, hash parse/format — anything that does not cross the P/Invoke boundary. Lives in `LibtorrentSharp.Tests`, default trait (no filter needed to run).

### Layer 2 — Native smoke (requires `lts.dll`, no network)

First test lands with step `sanity-test`:

- **Constructor smoke:** instantiate `TorrentClient`, assert no `DllNotFoundException`, dispose cleanly.
- **Settings round-trip:** apply a `SettingsPack` with known keys, read back, assert echo.
- **`TorrentInfo` parse:** load a small static `.torrent` fixture from `LibtorrentSharp.Tests/Fixtures/`, assert info-hash + file list.

Marked `[Trait("Category", "Native")]`. Skipped automatically when `lts.dll` isn't on the runtime path so managed-only CI on machines without vcpkg stays green.

### Layer 3 — Network integration (opt-in, requires internet + DHT)

Marked `[Trait("Category", "Network")]`. **Not** run by default — opt-in via test filter. Each row below lands in the same commit as the preconditioning ABI step ships:

| Test | Unlocks after step | Assertion |
|---|---|---|
| Magnet-to-metadata | `p0-magnet` | Add a well-seeded public magnet (e.g. current Ubuntu release ISO), wait ≤60 s, assert metadata received and peer count > 0. |
| Download-progress | above passing | Poll `TorrentStatus` for 30 s after the above, assert `total_download > 0` and state advances past `downloading_metadata`. |
| Resume round-trip | `p0-resume` | Add → progress → request resume → dispose session → reattach with resume blob on a fresh session → assert state skips re-check. |
| Pause/resume semantics | `p0-pause` | Auto-managed flag transitions match libtorrent's documented behavior (pause is distinct from stop). |
| Move storage | `p0-move` | `move_storage` to new path → wait for `storage_moved_alert` → assert files moved on disk. |

Network tests use a shared fixture that returns `Skip` (not fail) if the seed magnet can't reach > 0 peers within 15 s, so a flaky local network doesn't red the suite. Public magnets preferred over a private seeder to avoid infra cost. CI wiring deferred — manual run before each milestone flag flip until the binding extracts to its own repo.

### When each layer runs

- **Layer 1 + 2** (`Category != Network`): every commit, local + CI.
- **Layer 3** (`Category == Network`): manual, before flipping the feature flag at the end of each Pn milestone.

### Explicitly out of scope

- libtorrent's internal behavior (BEP conformance, piece-selection strategy, tracker-scrape accuracy) — that's upstream's test suite, not ours.
- MSIX packaging — lives in `WinBit.Tests`, not here.
- arm64 runtime tests — added when the arm64 triplet lands.

## Feature-parity plan

Parity with today's MonoTorrent integration, in order:

1. **Bootstrap:** scaffold the two projects, vendor csdl sources, rename exports, build on Windows x64, sanity-check with csdl's existing functions end-to-end.
2. **P0 ABI additions** + managed wrappers. At this point `LibTorrentSessionService` can add a magnet, track progress, save/load resume, pause/resume. Feature-flagged off.
3. **Wire the `Transfers` page** against the flagged service in a dev build. Validate rows update at 1 Hz through the existing `StatusPollingLoop` abstraction.
4. **P1 additions** — Peers/Trackers tabs light up, share-limit evaluator gets its inputs, per-torrent rate limits work.
5. **P2 additions** — share-limit *actions* work (super-seed/stop/remove), sequential/first-last flags land.
6. **arm64 build** — add the triplet, verify MSIX runtimes layout, second-DLL-in-package sanity check.
7. **Test pass** — port existing `TorrentCreatorServiceTests`, `EncryptionMapperTests`, `TorrentErrorFormatterTests` patterns; add integration fixtures for resume round-trip.
8. **Flip the flag** — decision moment. Either ship libtorrent as the default engine (and delete MonoTorrent), or document the blocker and park.

## Risk register

- **Native build friction on Windows.** vcpkg's libtorrent triplet has historically been finicky with Boost + OpenSSL resolution. If `libtorrent:x64-windows-static` doesn't install cleanly on the first try, time sink. Mitigation: fall back to `:x64-windows` (dynamic) and accept the Boost-DLL shipping cost; or pin to a known-good vcpkg baseline (csdl's `bc3512a` is a starting candidate).
- **MSIX + native DLLs.** WinAppSDK apps bundle native runtimes via the `runtimes/<rid>/native/` convention, but Store signing has historically rejected unsigned non-OS binaries under some circumstances. Mitigation: sign the DLL with the same cert as the MSIX; validate publish-profile output structure before committing the pipeline.
- **arm64 libtorrent availability.** vcpkg has `arm64-windows-static` but not every port compiles. Verify Boost + libtorrent + magic_enum all build on the arm64 triplet before declaring arm64 support.
- **Alert pump throughput.** libtorrent emits many alerts; a naive `AlertRaised` callback on the P/Invoke path can cause UI jank or GC pressure. Mitigation: buffer alerts on the native side, pump them in batched polls matching our existing 1 Hz `StatusPollingLoop` cadence.
- **Feature gaps we didn't anticipate.** Super-seeding semantics, BEP-specific behaviors, choking algorithm knobs all differ subtly from MonoTorrent. Mitigation: document every behavioral diff against `docs/torrent-engine.md#gaps--caveats` as we discover them.

## Where this lives

- Branch: `engine/libtorrent-bindings` (this file committed here first; project scaffolding follows in subsequent commits).
- Docs: `docs/libtorrent-binding.md` (this file) + updates to the spike appendix in `docs/torrent-engine.md` as milestones land.
- `TASKS.md` Backlog bullet is the single entry tracking this initiative; promote to a numbered milestone only once we decide to ship it as the default engine.

## Full-fidelity roadmap (beyond WinBit needs)

WinBit drives the prioritized C ABI above. Once WinBit can run on LibtorrentSharp end-to-end, the remaining surface gets filled in for community-quality release. Target coverage:

- **session** — full public method surface (~40 methods): async DHT ops, port mapping control, IP filter, listen-interface management, stats snapshot, post_session_stats, async save_state, async load_state, extension / DHT put/get if useful.
- **torrent_handle** — full public method surface (~60 methods): all `set_flags` / `unset_flags` with the full `torrent_flags_t` bitset, piece priorities (not just file-level), read_piece / have_piece, connect_peer, clear_error, rename_file, file storage APIs, merkle_tree access for V2 torrents.
- **Alerts** — full hierarchy (~60 concrete types), idiomatic C# classes inheriting from `Alert`, dispatched via `IAsyncEnumerable<Alert>` on the session.
- **Structures** — `torrent_status`, `peer_info`, `announce_entry`, `announce_endpoint`, `file_storage`, `storage_params`, `add_torrent_params` with every field marshalled.
- **Enums** — `torrent_state`, `storage_mode_t`, `connect_state_t`, `move_flags_t`, `pause_flags_t`, etc. — faithful C# enums.
- **Hash types** — `sha1_hash`, `sha256_hash`, `info_hashes` as proper value types with `Equals` / `GetHashCode` / string conversion.
- **Magnet parsing** — `parse_magnet_uri` exposed as a static API returning an `AddTorrentParams`.

**Excluded from the roadmap:**
- Plugin API (`session_plugin`, `torrent_plugin`, extension hooks). The C++ plugin model doesn't translate cleanly to C# and the use case is thin for a managed client.
- Test / internal / experimental APIs.
- Raw bencode/bdecode. Point users at `BencodeNET` on NuGet.

Scope accepted: **~300–500 API points** across session + torrent_handle + alerts + structs. A realistic single-maintainer plan is 2–3 months for the full surface. WinBit pays the fixed cost (native build, vcpkg, packaging, marshaling conventions) once; subsequent API additions are incremental.

## Vendoring playbook (next session handoff)

The scaffold commit (`b713769` on `engine/libtorrent-bindings`) leaves empty-but-buildable projects. The next task is vendoring csdl's reference sources. Keep this split across two commits for clean attribution:

### Commit 1 — Vendor csdl C# sources with namespace rename

Fetch from [`aspriddell/csdl`](https://github.com/aspriddell/csdl) (commit baseline: whatever `master` shows at vendoring time; record the SHA in the commit message for provenance). Copy into `libtorrentsharp/LibtorrentSharp/` preserving the subdirectory structure (`Alerts/`, `Enums/`, `Native/`, `Utils/`).

Files to vendor (21 total):

| csdl source path | LibtorrentSharp path |
|---|---|
| `csdl/TorrentClient.cs` | `LibtorrentSharp/TorrentClient.cs` |
| `csdl/TorrentClientConfig.cs` | `LibtorrentSharp/TorrentClientConfig.cs` |
| `csdl/TorrentInfo.cs` | `LibtorrentSharp/TorrentInfo.cs` |
| `csdl/TorrentManager.cs` | `LibtorrentSharp/TorrentManager.cs` |
| `csdl/TorrentStatus.cs` | `LibtorrentSharp/TorrentStatus.cs` |
| `csdl/SettingsPack.cs` | `LibtorrentSharp/SettingsPack.cs` |
| `csdl/Alerts/PeerAlert.cs` | `LibtorrentSharp/Alerts/PeerAlert.cs` |
| `csdl/Alerts/PerformanceWarningAlert.cs` | `LibtorrentSharp/Alerts/PerformanceWarningAlert.cs` |
| `csdl/Alerts/SessionAlert.cs` | `LibtorrentSharp/Alerts/SessionAlert.cs` |
| `csdl/Alerts/TorrentRemovedAlert.cs` | `LibtorrentSharp/Alerts/TorrentRemovedAlert.cs` |
| `csdl/Alerts/TorrentStatusAlert.cs` | `LibtorrentSharp/Alerts/TorrentStatusAlert.cs` |
| `csdl/Enums/AlertCategories.cs` | `LibtorrentSharp/Enums/AlertCategories.cs` |
| `csdl/Enums/AlertType.cs` | `LibtorrentSharp/Enums/AlertType.cs` |
| `csdl/Enums/FileDownloadPriority.cs` | `LibtorrentSharp/Enums/FileDownloadPriority.cs` |
| `csdl/Enums/PeerAlertType.cs` | `LibtorrentSharp/Enums/PeerAlertType.cs` |
| `csdl/Enums/PerformanceWarningType.cs` | `LibtorrentSharp/Enums/PerformanceWarningType.cs` |
| `csdl/Enums/TorrentState.cs` | `LibtorrentSharp/Enums/TorrentState.cs` |
| `csdl/Native/NativeEvents.cs` | `LibtorrentSharp/Native/NativeEvents.cs` |
| `csdl/Native/NativeMethods.cs` | `LibtorrentSharp/Native/NativeMethods.cs` |
| `csdl/Native/NativeStructs.cs` | `LibtorrentSharp/Native/NativeStructs.cs` |
| `csdl/Utils/ListenInterface.cs` | `LibtorrentSharp/Utils/ListenInterface.cs` |
| `csdl/Utils/ListenInterfaceExtensions.cs` | `LibtorrentSharp/Utils/ListenInterfaceExtensions.cs` |

Transformations applied during the vendor:
1. **Namespace:** `namespace csdl` → `namespace LibtorrentSharp` (and sub-namespaces `csdl.Alerts` → `LibtorrentSharp.Alerts`, etc.).
2. **Usings:** `using csdl;` / `using csdl.Alerts;` / `using csdl.Enums;` / `using csdl.Native;` → `using LibtorrentSharp;` / `.Alerts` / `.Enums` / `.Native`.
3. **Header:** prepend the Apache-2.0 header retained in each file (csdl files already have a one-line `// csdl - a cross-platform libtorrent wrapper for .NET` header; preserve it as a provenance marker, and add a `// Derived from csdl by Albie Spriddell. See NOTICE for attribution.` line).
4. **Do NOT rename types yet.** `TorrentClient`, `TorrentManager`, etc. stay as csdl has them. Any idiomatic rename (e.g., `TorrentClient` → `LibtorrentSession`, `TorrentManager` → `TorrentHandle`) is a separate commit after the vendor lands.

Delete `libtorrentsharp/LibtorrentSharp/AssemblyMarker.cs` (the scaffolding placeholder) in the same commit.

### Commit 2 — Rename DllImport entrypoints and native library

In the vendored `Native/NativeMethods.cs`, change:
- `[DllImport("csdl", ...)]` → `[DllImport("lts", ...)]`
- csdl's exported entry-point symbol names (e.g., `create_session`, `attach_torrent`) stay the same on both sides for this commit — our C ABI matches csdl's for these first 17 functions.

This completes the consumer-side rename. The native DLL name on disk is `lts.dll` (Windows) / `liblts.so` (Linux) / `liblts.dylib` (macOS), matching the CMake target in `LibtorrentSharp.Native/CMakeLists.txt`.

### Commit 3 — Vendor csdl native C++ sources

Mirror the same treatment for `aspriddell/csdl/native/`:

| csdl-native source | LibtorrentSharp.Native path |
|---|---|
| `native/include/events.h` | `LibtorrentSharp.Native/include/events.h` |
| `native/include/library.h` | `LibtorrentSharp.Native/include/library.h` |
| `native/include/locks.hpp` | `LibtorrentSharp.Native/include/locks.hpp` |
| `native/include/settings.h` | `LibtorrentSharp.Native/include/settings.h` |
| `native/include/struct_align.h` | `LibtorrentSharp.Native/include/struct_align.h` |
| `native/include/structs.h` | `LibtorrentSharp.Native/include/structs.h` |
| `native/src/events.cpp` | `LibtorrentSharp.Native/src/events.cpp` |
| `native/src/library.cpp` | `LibtorrentSharp.Native/src/library.cpp` |
| `native/src/settings.cpp` | `LibtorrentSharp.Native/src/settings.cpp` |

Transformations:
1. `CSDL_EXPORT` macro → `LTS_EXPORT` (update the `include/lib_export.h` reference — CMake already emits `lts_export.h` per the scaffold). Global rename.
2. `#include "lib_export.h"` → `#include "lts_export.h"`.
3. Delete the `placeholder.cpp` added in the scaffold; its single export (`lts_version`) is no longer needed once the real sources land.
4. Update `CMakeLists.txt` `add_library(lts SHARED ...)` source list to reference the vendored files.

Preserve original copyright headers (`// library.hpp — Created by Albie on 29/02/2024`) and prepend a `// Derived from csdl by Albie Spriddell. See NOTICE for attribution.` line.

### After vendoring

- First local vcpkg build: `vcpkg install libtorrent magic-enum --triplet=x64-windows-static`, then `cmake -B build -S LibtorrentSharp.Native -DCMAKE_TOOLCHAIN_FILE=<vcpkg>/scripts/buildsystems/vcpkg.cmake`, then `cmake --build build --config Release`. Multi-hour first-run compile — plan accordingly.
- Copy the resulting `lts.dll` into `libtorrentsharp/LibtorrentSharp/runtimes/win-x64/native/lts.dll`. Add `<Content Include="runtimes/**/*" PackageCopyToOutput="true" />` to the csproj so consumers get the DLL.
- Write a tiny sanity-check unit test that calls `TorrentClient`'s constructor and ensures no DllNotFoundException.
- Only then start adding the P0 C ABI extensions (magnet, resume, recheck, pause/resume, move_storage).

## Validation log

Manual dev-build runs that exercise the libtorrent adapter under the
`AdvancedSettings.UseLibtorrentEngine = true` flag. `LIBTORRENT_TASKS.md`'s Phase B
references each entry below. The autonomous `/libtorrent-next` loop drops scaffolds
here (launch command, observation checklist) but cannot run the manual steps — flip
the corresponding `[~]` to `[x]` in `LIBTORRENT_TASKS.md` once observations are
recorded.

### b-smoke — engine starts under the flag

**Build status.** `dotnet build WinBit.slnx -c Debug -p:Platform=x64` was green at the
adapter-status commit (`0cbb3e0`). Re-run before launch to be sure.

**Enabling the flag.** The setting isn't on the Settings page yet, so hand-edit JSON
before launch:

```pwsh
$settings = "$env:LOCALAPPDATA\WinBit\settings.json"
# If the file doesn't exist, launch WinBit once with the flag off so it's created,
# then quit and apply the patch below.
$json = Get-Content $settings -Raw | ConvertFrom-Json
$json.Advanced.UseLibtorrentEngine = $true
$json | ConvertTo-Json -Depth 32 | Set-Content $settings
```

**Launch.** From the repo root:

```pwsh
dotnet run --project WinBit.csproj -c Debug -p:Platform=x64
```

**Manual checklist.** Capture observations inline below.

- [ ] App window opens without an unhandled exception.
- [ ] Logs page (or Output window) shows `Libtorrent engine started (port: …, UPnP: …, LSD: …, downloads: …)`.
- [ ] Transfers page renders the empty-state illustration (no exception, no broken
      grid). Status bar shows zero rates / zero connections.
- [ ] Quit cleanly. Logs show `Libtorrent engine stopped`.

**Observations:** _pending the dev-build session — fill this in then flip `b-smoke`
to `[x]` in `LIBTORRENT_TASKS.md`._

**Known gaps that are not regressions.** `OpenConnections` and `DhtNodes` in the
status bar stay at 0 — both require `f-session-stats` (`session_stats_alert` pump).
PEX toggle in Settings is a no-op for the libtorrent path until the C ABI exposes
per-torrent `set_flags`. Both are documented under their respective task rows.

### b-magnet-e2e — well-seeded magnet, end-to-end

**Pre-reqs.** `b-smoke` observations recorded; engine starts cleanly under the flag.

**Magnet to use.** Ubuntu's release magnet is the most reliable public test target —
swarm has thousands of seeders and the swarm doesn't go quiet:

```
magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=ubuntu-14.04.1-desktop-amd64.iso
```

If the dev box can't reach UDP trackers, swap in any other recently-released distro
ISO magnet. The check is "metadata + peers + bytes," not "Ubuntu specifically."

**Procedure.** Open the running app's **Add → From magnet link** dialog, paste the
URI, accept the default save path, and click Add.

**Manual checklist.**

- [ ] Add dialog accepts the URI without an inline-error InfoBar.
- [ ] A new row appears in the Transfers grid within ~2 seconds. State pill reads
      `Downloading` (libtorrent uses the same enum for metadata-fetch and payload).
- [ ] Within 60 seconds: peers > 0, downloaded bytes > 0, name resolves from the
      info-hash to the real torrent name (the metadata arrived).
- [ ] Tabs **Peers** and **Trackers** populate with at least one row each. Numbers
      are non-zero where they should be (ratio, speed, last-announce).
- [ ] Right-click → **Pause** flips the row to `Paused` immediately. Right-click →
      **Resume** flips it back to `Downloading`.
- [ ] Right-click → **Remove** drops the row. The Transfers grid returns to the
      empty state if no other torrents are loaded.

**Observations:** _pending the dev-build session — fill this in then flip
`b-magnet-e2e` to `[x]` in `LIBTORRENT_TASKS.md`._

**If the test fails:** capture the Logs page output around the failure and file a
bug against the responsible Phase A row (e.g. `a-snapshots` if the row never
updates, `a-meta` if Trackers tab stays blank). Don't flip the box.

### b-parity-tabs — every detail tab renders real values

**Pre-reqs.** `b-magnet-e2e` observations recorded; the test torrent is still loaded
and has had ~30 seconds to populate metadata + at least one peer.

**Procedure.** Click the row in the Transfers grid, then walk each detail tab in
order. The libtorrent path should fill the same fields the MonoTorrent path does;
any blank that should have a value is a regression against a specific Phase A
row — file it under that row in the WinBit backlog (`TASKS.md` libtorrent-engine
section).

**Per-tab checklist.**

- **General**
  - [ ] Name resolves (matches the magnet's `dn=` or the .torrent metadata).
  - [ ] Save path matches what was passed to AddAsync.
  - [ ] State pill matches the row's pill (Downloading / Seeding / etc.).
  - [ ] Time-active counter ticks up.
  - [ ] Ratio renders (0.000 is fine; -1.#J is a regression).
- **Trackers**
  - [ ] At least one tracker row.
  - [ ] Status column not blank (Updating / Working / Failure / NotContacted).
  - [ ] Seeds and Leeches columns are integers (not "—" / blank).
- **Peers**
  - [ ] At least one peer row within 60 s on a public swarm.
  - [ ] IP column populated (v4 or v6).
  - [ ] Up/Down rate columns are non-negative numbers.
- **Content**
  - [ ] File list renders once metadata arrives. Names match expected.
  - [ ] Sizes are non-zero where the file is non-empty.
- **Speed**
  - [ ] Graph control draws without exception (empty graph at session start is OK).
  - [ ] At least one data point appears within ~5 ticks (5 s) of the row showing
        download activity.

**Observations:** _pending the dev-build session — fill this in then flip
`b-parity-tabs` to `[x]` in `LIBTORRENT_TASKS.md`._

**Known gaps that aren't regressions.** Any tab that's also blank under MonoTorrent
isn't a libtorrent regression — note it in observations and skip the file. The
share-limit IsForced column will read `False` for paused-via-engine-default
torrents until `a-actions` consumers explicitly call ResumeAsync, since libtorrent
treats fresh adds as paused-by-default.

### b-lifecycle — fast-resume survives shutdown + restart

**Pre-reqs.** `b-magnet-e2e` observations recorded; the test torrent has been
running long enough to have downloaded ≥ a few pieces (so the resume blob carries
non-trivial state). Look for `total_download` > 0 in the General tab.

**Procedure.**

1. With the torrent actively downloading, quit WinBit cleanly via the system tray
   menu **or** the close button — both should trigger
   `WinBitHostedService` → `PersistFastResumeAsync`.
2. Confirm the SQLite store recorded the blob:

   ```pwsh
   $db = "$env:LOCALAPPDATA\WinBit\state.db"
   sqlite3 $db "SELECT info_hash, length(fast_resume), resume_ver FROM torrent WHERE fast_resume IS NOT NULL;"
   ```

   You should see one row per added torrent, each with `length(fast_resume) > 0`
   and `resume_ver = 1` (the current `ResumeBlobVersion`).
3. Re-launch WinBit (same `dotnet run` command from `b-smoke`).
4. Watch the Logs page during startup.

**Manual checklist.**

- [ ] Quit logs include `Libtorrent engine stopped` (no exceptions during
      `PersistFastResumeAsync`).
- [ ] `state.db` query returns the expected row(s) with non-zero blob length.
- [ ] Re-launch logs include `Loaded magnet … from saved resume blob → …` (or
      the `Loaded torrent … from saved resume blob → …` variant for file adds)
      for each previously-running torrent.
- [ ] Re-launched torrents skip the `Checking` state and go directly to
      `Downloading`/`Seeding`. (Watch the State pill on the row in the first
      few seconds.)
- [ ] No spurious "fast resume blob rejected" warnings in the logs.

**Observations:** _pending the dev-build session — fill this in then flip
`b-lifecycle` to `[x]` in `LIBTORRENT_TASKS.md`._

**If the test fails:**

- "Resume blob rejected" warning means libtorrent didn't like the blob — likely a
  format mismatch. Bump `LibTorrentSessionService.ResumeBlobVersion` and restart
  with a clean store. File a bug under `a-resume`.
- "Resume row missing for info-hash X" but the torrent was running before quit
  means the autosave path isn't completing. File a bug under `a-resume` (persist
  half: `PersistFastResumeAsync` is not awaiting alerts long enough, or
  `WinBitHostedService` isn't calling it on shutdown).
- Re-check happens despite the blob loading successfully — file under `a-resume`
  (load-on-add half: `AttachTorrentWithResume` may have returned an invalid
  handle and silently fell back).
