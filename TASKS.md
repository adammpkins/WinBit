# TASKS.md — WinBit milestone roadmap

Each milestone ships something usable. Each honors the "modern and beautiful" bar for the surface it touches. Check off deliverables as they land; do not mark a milestone complete until its verification section passes.

---

## M1 — Scaffolding  *(in progress)*

**Goal:** Three-project solution, DI host, Mica shell with NavigationView, theme service, illustrated empty state. Build, test, and launch clean.

**Deliverables**
- [x] `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig` at solution root.
- [x] `README.md`, `TASKS.md`, `docs/` stubs.
- [x] `WinBit.Core` class library with stub services (`ITorrentSessionService`, `ISettingsService`, `ILogService`) and common types (`TorrentId`, `Result`, `Paths`).
- [x] `WinBit.Tests` xUnit project with a smoke test that composes `AddWinBitCore` without throwing.
- [x] `WinBit.slnx` referencing all three projects.
- [x] `WinBit` app wired to `WinBit.Core` with `Microsoft.Extensions.Hosting`-based DI.
- [x] `App.xaml.cs` builds `IHost`, starts it on `OnLaunched`, stops on `Exiting`.
- [x] `MainWindow` with Mica backdrop and extended title bar (command buttons for Add-Torrent placeholder, Add-Magnet placeholder, alt-speed toggle placeholder).
- [x] `ShellPage` with `NavigationView`: Transfers, RSS, Search, Logs, Torrent Creator, Statistics, Settings.
- [x] `TransfersPage` with illustrated empty state and Add-Torrent CTA.
- [x] `IThemeService` — light/dark/system switcher with placeholder accent.
- [x] Shared resource dictionaries: `Styles/Typography.xaml`, `Colors.xaml`, `Animations.xaml`.

**Design constraints (this milestone)**
- Window is chromeless + Mica. No default title bar.
- Empty state shows an illustration, a headline, a sub-headline, and a primary button.
- NavigationView items use Segoe Fluent Icons, not raw glyphs.
- Theme switch takes effect instantly with `ThemeTransition`.

**Verification**
- `dotnet build WinBit.slnx -c Debug -r win-x64` is green.
- `dotnet test` passes the smoke test.
- Launching WinBit shows Mica shell, extended title bar, NavigationView, empty Transfers page with illustrated CTA.
- Theme toggle flips light ↔ dark without restart.
- No first-chance exceptions on startup or shutdown.

---

## M2 — Persistence & settings

**Goal:** `AppSettings` POCO tree, JSON settings store, SQLite torrent-state store, SettingsPage with Fluent sub-pages.

**Deliverables**
- [x] `AppSettings` covering Downloads, Connection, Speed, BitTorrent, Rss, WebUi, Advanced, UiState.
- [x] `JsonSettingsStore` with atomic write + debounced save (500 ms after last change).
- [x] `SqliteTorrentStateStore` with WAL mode, migrations (`SqlMigrations/001_init.sql`), serialized writer queue.
- [x] `Paths.cs` creates `%LOCALAPPDATA%\WinBit\` tree on first run.
- [x] `SettingsPage` with `NavigationView` of sub-pages (Downloads, Connection, Speed, BitTorrent, RSS, WebUI, Advanced).
- [x] Each sub-page uses `SettingsCard` / `SettingsExpander`.
- [x] `IThemeService` persists through `ISettingsService`.
- [x] Unit tests: JSON round-trip, atomic save survives crash, SQLite init, upsert/remove, concurrent write safety.

**Design constraints**
- Every setting row is a `SettingsCard` — no bare labels + controls.
- Sub-pages scroll smoothly with `ThemeTransition`.
- Validation errors render inline, not in dialogs.

**Verification**
- Unit tests green.
- Change a setting → close app → reopen → setting preserved.
- Theme choice persists.

---

## M3 — Torrent engine (MonoTorrent)

**Goal:** `TorrentSessionService` wraps MonoTorrent's `ClientEngine`. Add from file/magnet/URL. Start/stop/recheck/reannounce. Fast-resume round-trip.

**Deliverables**
- [x] `TorrentSessionService` implementing `ITorrentSessionService` fully.
- [x] `TorrentHandle`, `TorrentSnapshot`, `PeerInfo`, `TrackerInfo`, `AddTorrentParams`, `TorrentState`.
- [x] `StatusPollingLoop` at 1 Hz raising batched `TorrentUpdated`.
- [x] Fast-resume blob persistence via `ITorrentStateStore`.
- [x] `UrlDownloader` for adding from HTTP(S) URLs.
- [x] Engine lifecycle hooked into `WinBitHostedService`.
- [x] Spike report in `docs/torrent-engine.md`: MonoTorrent coverage of BEP 52 (v2 torrents), super seeding, encryption modes, UPnP, choking algorithms. Document gaps.
- [x] Tests: add magnet, observe state via polling loop.
- [x] Tests: engine-level fast-resume round-trip — serialize a MonoTorrent `FastResume`, write via `ITorrentStateStore`, reload into a new manager, confirm no re-check.

**Design constraints (this milestone)**
- Polling overhead invisible to user — no input lag, no GC spikes.

**Verification**
- Add a magnet via the API, watch state transition to Downloading, restart app, torrent resumes without re-checking (if fast-resume valid).
- Engine starts/stops cleanly with `IHost.StartAsync` / `StopAsync`.

---

## M4 — Transfer list & properties panel

**Goal:** The central UI. DataGrid with live updates, properties pivot, Add-Torrent editor dialog, drag-drop.

**Deliverables**
- [x] `TransfersPage` hosts `WinUI.TableView` bound to an `AdvancedCollectionView` over `TransferRowViewModel` rows (CommunityToolkit DataGrid is not available for WinAppSDK 1.8).
- [x] Row columns: Name, Size, Progress (inline bar), State (pill), Seeds, Peers, Down, Up, Ratio, ETA, Added, Completed, Category, Tags.
- [x] Column reorder, resize, sort persist via `AppSettings.UiState`.
- [x] `StatePill` control — icon + label + theme-aware pill per `TorrentState`.
- [x] `PiecesBar` (Win2D) control.
- [x] Properties `Pivot` below grid: General, Trackers, Peers, Content, Speed.
- [x] `AddMagnetDialog` — magnet URI input + save-path picker + start-immediately toggle, wired to `ITorrentSessionService.AddAsync` with inline `InfoBar` error surfacing.
- [x] `AddTorrentDialog` — .torrent file picker + flat file preview + save-path text input. (Nested folder tree + save-path combobox with recent roots are polish follow-ups.)
- [x] `AddTorrentDialog` polish: save-path combobox with recent roots.
- [x] `AddTorrentDialog` polish: nested folder-tree preview.
- [x] `DownloadFromUrlDialog` — HTTP(S) URL fetch via `UrlDownloader`, then hands bytes to the engine.
- [x] Add-dialogs richer editor: category preset picker — `AddMagnetDialog`, `AddTorrentDialog`, `DownloadFromUrlDialog` show a Category combobox; selection resolves the TMM save path via `TmmPathResolver` and `AddTorrentParams.Category` flows through.
- [x] Add-dialogs richer editor: tag chips — each add dialog hosts a `DropDownButton` + multi-select `ListView` backed by `ITagService`; selection flows into `AddTorrentParams.Tags`.
- [x] Add-dialogs richer editor: share-limits editor — inline share-limit controls on each add dialog, persisted via `IShareLimitOverrideService` against the new torrent id.
- [x] `SpeedGraph` (Win2D) scrolling line chart on Speed tab — dual series (download/upload) with theme-resolved line + gradient-fill rendering.
- [x] `SpeedGraph` polish: peak callouts per series.
- [x] Context menu (pause, resume, remove, force recheck, force reannounce, open folder, copy magnet).
- [x] Drag-drop `.torrent` files onto window opens AddTorrentDialog.
- [x] Row updates via INPC only; zero collection rebuilds on tick.

**Design constraints**
- Inline progress bars look Fluent, not web-generic. Rounded corners, accent color, subtle gradient.
- State pills have icon + label + theme-aware background.
- ConnectedAnimation from row → properties panel on selection.
- Add-Torrent dialog spans 70% of window, is acrylic-backed, resizable.

**Verification**
- 500+ rows scroll smoothly at 60 fps.
- Memory steady over a 1 h session.
- Drag-drop a `.torrent` → dialog opens with its content.

---

## M5 — Categories, tags, share limits, per-torrent speed limits

**Deliverables**
- [x] `ICategoryService` + `ITagService` + persistence.
- [x] Category/tag sidebar filter tree.
- [x] Category/tag editor dialogs.
- [x] Share limits dialog (global scope): ratio, seeding time, inactive seeding time, match mode, action on limit.
- [x] Per-torrent share-limit override store: `PerTorrentShareLimitOverride` + `IShareLimitOverrideService` with JSON persistence and global-merge logic.
- [x] Per-torrent share-limit evaluator — pure `ShareLimitEvaluator` porting `processTorrentShareLimits` with parity fixtures.
- [x] Per-torrent share-limit enforcement hosted service — `ShareLimitEnforcementLoop` background service ticks every 60 s, tracks per-torrent seeding/inactive-seeding time, and dispatches Stop/Remove/RemoveWithContent/EnableSuperSeeding via `ITorrentSessionService`.
- [x] Engine-level content deletion — extend `ITorrentSessionService.RemoveAsync` (or add a sibling) so share-limit `RemoveWithContent` can actually delete on-disk files.
- [x] Super-seeding toggle — surface MonoTorrent's super-seeding on `ITorrentSessionService` so share-limit `EnableSuperSeeding` can engage it.
- [x] Per-torrent share-limit UI — context-menu "Share limits…" entry + dialog wired to `IShareLimitOverrideService`.
- [x] Per-torrent speed limit dialog.
- [x] Auto Torrent Management (TMM) path resolution matching qBittorrent's category-options behavior.
- [x] Parity unit tests for TMM path rules.

**Verification**
- Assigning a category moves files to that category's save path when TMM is on.
- Share limit triggers configured action (pause/remove/super-seed).

---

## M6 — Filters, status bar, statistics

**Deliverables**
- [x] Status filter sidebar (Downloading/Seeding/Completed/Paused/Active/Inactive/Errored).
- [x] Tracker filter sidebar grouped by host.
- [x] Session status bar: DHT nodes, global down/up, connection count, alt-speed toggle.
- [x] `StatsPage`: all-time upload/download, shared, session ratio, DHT nodes, free space.

**Verification**
- Switching filters updates grid in <50 ms with 500 rows.

---

## M7 — Speed controls, scheduler, IP filter, logs

**Deliverables**
- [x] Global down/up speed limits + alt profile in Settings/Speed.
- [x] `BandwidthScheduler` (`IHostedService`) — time-of-day rules, parity-tested against qBittorrent's bandwidth scheduler.
- [x] `PeerGuardianParser` for `.p2p` blocklists; `IpFilterService` wires into engine.
- [x] Execution log page bound to `ILogService`.
- [x] Peer log page (banned peers, reason).
- [x] Scheduler rule editor in Settings/Speed — enable toggle, start/end time pickers, day selector wired to `SchedulerEnabled`, `SchedulerStartTime`, `SchedulerEndTime`, `SchedulerDays` in `AppSettings.Speed`.

**Verification**
- Scheduler flips alt mode at the scheduled time.
- Banned peer IP appears in peer log.

---

## M8 — Networking, watched folders, torrent creator  *(complete)*

**Deliverables**
- [x] Proxy settings (SOCKS5/HTTP) with optional auth.
- [x] UPnP / NAT-PMP port forwarding toggle via `IPortForwardingService`.
- [x] Protocol encryption mode selector.
- [x] DHT / PEX / LSD toggles.
- [x] `WatchedFolderService` with debounced `FileSystemWatcher` + per-folder options.
- [x] Watched Folders settings page — list existing folders, add (folder picker + per-folder options), remove; options: save-path override, start immediately, recursive, delete source on add.
- [x] `TorrentCreatorPage` using MonoTorrent's `TorrentCreator`.

**Verification**
- Drop `.torrent` into watched folder → auto-add within 1 s.
- Created `.torrent` validates via external client.

---

## M9 — RSS + auto-downloader *(complete)*

**Deliverables**
- [x] RSS 2.0 + Atom feed parser (`RssFeedParser`, pure Core).
- [x] Feed-tree model + `rss/feeds.json` persistence (`RssService.GetTree/Save`).
- [x] Refresh loop `IHostedService` polling feeds at per-feed / global interval.
- [x] `RssPage` with feed tree, article list, manual-download button.
- [x] `IAutoDownloaderService` — rule CRUD + `rss/rules.json` persistence.
- [x] `AutoDownloaderPage` with rule CRUD + live-tester.
- [x] Auto-dispatch loop — evaluate new articles against rules and enqueue matches via `ITorrentSessionService`.
- [x] `RuleMatcher` with must-contain, must-not-contain, episode filter, smart episode filter, re-download protection. Parity-tested against qBittorrent's RSS auto-download rule.

**Verification**
- Public RSS feed fetches; matching rule auto-adds torrents.

---

## M10 — Web UI *(complete)*

**Deliverables**
- [x] In-process Kestrel host via `WebUiService`.
- [x] `/api/v2/app/*` — version, webapiVersion, buildInfo, defaultSavePath.
- [x] `/api/v2/auth/*` — login / logout with cookie session.
- [x] `/api/v2/torrents/*` read + control — info, delete, pause, resume, recheck.
- [x] `/api/v2/torrents/add` — file / URL / magnet upload.
- [x] `/api/v2/transfer/*` — session speeds, speed limits mode.
- [x] `/api/v2/log/*` — main, peers (read-only).
- [x] `/api/v2/sync/*` — maindata incremental poll.
- [x] `/api/v2/rss/*` — feed list + rule CRUD.
- [x] `/api/v2/rss/matchingArticles` — rule → matching article titles from the cache.
- [x] `/api/v2/rss/moveItem` — move a folder/feed to a new path (needs `IRssService.MoveItemAsync`).
- [x] `/api/v2/rss/markAsRead` — per-article read state (in-memory; disk persistence tracked separately).
- [x] `/api/v2/rss/refreshItem` — force a single feed refresh (needs hook on `RssRefreshLoop`).
- [x] Persist RSS article read-state across restart (SQLite `rss_read` table).
- [x] `/api/v2/torrentcreator/*` — addTask, status, downloadTorrent, deleteTask.
- [x] `/api/v2/search/*` — plugin bridge (stub until M12 search service exists).
- [x] PBKDF2 password hashing + cookie session (shipped alongside `/api/v2/auth/*`).
- [x] Optional HTTPS via user-supplied PFX / self-signed cert.
- [x] IP subnet whitelist bypass for LAN clients.
- [x] Ship qBittorrent's HTML admin UI as static files (since we implement its API).
- [x] Native WinUI-style web client replacing qBittorrent's HTML — Fluent-flavored SPA served from the same Kestrel host, toggleable in Settings/WebUI.
- [x] `qbittorrent-api` (Python) compatibility as CI oracle.

**Verification**
- `qbittorrent-api` can list, start, stop, remove torrents against a running WinBit.

---

## M11 — Tray, notifications, power, shell integration *(complete)*

**Deliverables**
- [x] `WinBitTrayIcon` via `H.NotifyIcon.WinUI` — show/hide, Add-Magnet, alt-speed, Exit.
- [x] Close-to-tray option (configurable).
- [x] Toast notifications via Windows AppNotifications — completion (name + save path; click opens folder).
- [x] Toast notifications — torrent errors (transition into Error state).
- [x] Surface MonoTorrent error message on `TorrentSnapshot.ErrorMessage` and forward into the error toast.
- [x] Toast notifications — download-rate-low warning on long-running torrents.
- [x] `PowerManagementService` preventing sleep while active torrents exist.
- [x] `.torrent` file association and `magnet:` URI protocol handler.
- [x] Default-client prompt on startup — detect whether WinBit owns `.torrent` / `magnet:`, offer an in-app dialog to register (or the Windows "Default apps" deep link) when it doesn't.
- [x] Single-instance enforcement — second launch forwards magnet to running instance.

**Verification**
- Close to tray → app persists, tray icon visible.
- Completion pops a toast; clicking opens folder.
- System doesn't sleep while a torrent is active.

---

## M12 — Search + polish *(complete)*

**Deliverables**
- [x] Search plugin host framework — `ISearchPlugin` / `ISearchPluginHost` / concurrent merged streams.
- [x] Ship at least one concrete `ISearchPlugin` — Torznab/Jackett feed plugin with parser + startup registrar.
- [x] Live-reconfigure Torznab plugins when `SearchSettings.TorznabFeeds` changes.
- [x] `SearchPage`: multi-plugin search with progress, filters, one-click download.
- [x] Localization scaffolding (`.resw` files + a locale picker in Settings).
- [x] About dialog (version + credits + project links).
- [x] First-run wizard (guided defaults for save path, default-handler prompt, Web UI toggle).
- [x] Update checker (compare local version to latest GitHub release, offer download).
- [x] Accent color picker and theme refinement.
- [x] Full regression pass of M4..M11.
- [x] `docs/development.md` finalized with packaging + MSIX steps.

**Verification**
- Search returns results from at least one working provider.
- Localization swaps on the fly.
- All prior milestones still pass their verification.

---

## Backlog / post-M12

Features we deliberately hold until after feature parity ships:

- qBittorrent resume-data import (if users request it).
- Multi-profile (home/work) support. Only if this is something other torrent clients offer. 
- Plugin SDK for C# search providers.
- ARM64 first-class support and NativeAOT publishing.
- `Python.NET` embedding of qBittorrent's Nova3 plugins. Deferred from M12 — blocked on user decisions about bundled vs system Python, plugin curation, and in-process sandboxing. Torznab (shipped) satisfies the M12 verification gate.
- ~~libtorrent-rasterbar engine bindings~~ **SHIPPED (2026-04-27)** — LibtorrentSharp is now the active engine on `main`; MonoTorrent has been removed. See `docs/torrent-engine.md` for the full decision record.

- **MonoTorrent removal** *(complete 2026-04-27)* — clean up all MonoTorrent code now that LibtorrentSharp is the active engine:
  - [x] Delete `WinBit.Core/BitTorrent/TorrentSessionService.cs` (743-line MonoTorrent implementation).
  - [x] Port `WinBit.Core/BitTorrent/EncryptionMapper.cs` to libtorrent `settings_pack` encryption keys; update callers. (No live callers after deletion; `LibTorrentSessionService` maps inline. File deleted.)
  - [x] Port `WinBit.Core/BitTorrent/TorrentErrorFormatter.cs` to libtorrent error codes; update callers. (No live callers; `LibTorrentSessionService` formats inline. File deleted.)
  - [x] Delete or port `WinBit.Core/Networking/DhtBootstrapSeeder.cs` (used MonoTorrent's `IDhtEngine`). Deleted.
  - [x] Delete or port `WinBit.Core/Networking/DhtNetworkProbe.cs` (used MonoTorrent's `IDhtEngine`; currently broken on libtorrent — hangs on "NotReady" forever). Deleted.
  - [x] Remove `<PackageReference Include="MonoTorrent" />` from `WinBit.Core/WinBit.Core.csproj`.
  - [x] Remove `AdvancedSettings.UseLibtorrentEngine` flag from `AppSettings` and DI registration conditional.
  - [x] Delete/update tests: `TorrentErrorFormatterTests.cs`, `EncryptionMapperTests.cs`, `TorrentCreatorServiceTests.cs`, MonoTorrent-specific blocks in `SqliteStoreTests.cs`.
  - [x] `TorrentCreatorService.cs` — stub out (disable UI entry point, log "not yet supported") until Phase G of `LIBTORRENT_TASKS.md` ships.

  **WinBit integration gaps (engine/libtorrent-bindings — must close before `g-flip`):**
  - [x] Cold-start torrent loader: on `StartAsync`, call `ITorrentStateStore.GetAllAsync()` and re-add each saved torrent with its stored fast-resume blob; handle missing save path (re-check fallback) and missing torrent file gracefully per-torrent.
  - [x] Peers tab: surface `TorrentHandle.GetPeers()` through `ITorrentSessionService` → `TorrentPropertiesViewModel` → Peers pivot (IP, client string, flags, upload/download speed, progress).
  - [x] Trackers tab: surface tracker URL, working state, seeds/leechers/downloaded counts via tracker alerts and `GetTrackers()`.
  - [x] Content tab: surface per-file list and priority (display) for multi-file torrents from the libtorrent engine.
  - [x] Content tab: per-file download progress (requires lt_file_progress native binding — tracked in LIBTORRENT_TASKS.md).
  - [x] Content tab: per-file progress shows 0.0% for seeded/resumed torrents — `lts_file_progress()` returns 0 for torrents loaded from resume data; investigate libtorrent `is_seed()` / `valid_metadata()` timing or apply per-torrent progress fallback when `TorrentState == Seeding`.
  - [x] Content tab empty state: when a torrent is selected but files haven't polled yet, it shows "Select a torrent to view its contents" — change text to "Loading…" or similar so the two states are distinguishable.
  - [x] Pieces tab: feed piece availability map from libtorrent into the existing `PiecesBar` Win2D control.
  - [x] Bulk piece-bitfield API in LibtorrentSharp — `GetPiecesAsync` currently calls `HavePiece(i)` in a loop (O(n) P/Invoke calls); a native bulk bitfield copy would be O(1). Tracked here so it's not rediscovered. See LIBTORRENT_TASKS.md Phase G for the binding expansion.
  - [x] `SetPeerDiscoveryAsync` PEX: `LibTorrentSessionService` should call `TorrentHandle.UnsetFlags(TorrentFlags.DisablePex)` / `SetFlags` — the binding already exposes these (`f-handle-flags` complete) but the service doesn't use them.
  - [x] `SessionStats.OpenConnections` / `DhtNodes` always zero: wire session-stats alert parsing into `GetSessionStats()` so the status bar shows live peer-connection and DHT-node counts.

- [x] **Transfers page horizontal scrollbar does nothing** — the `WinUI.TableView` renders a horizontal scrollbar when columns overflow, but scrolling it has no effect. Investigate whether `TableView` requires a fixed-width container, a custom `ScrollViewer` template, or explicit column-width bindings to make horizontal scroll work; fix and verify via FlaUI (`windows_scroll` or drag) and screenshot.

- [x] **General tab** — fill in real torrent metadata (info hash, save path, comment, creation date, fast-resume status). The tab currently shows a placeholder from M4 that was never wired up.

- [x] **Accent color / theme changes require restart** — live accent-color and light/dark switching was working at M12 but has regressed. Changing the accent color or theme in Settings should take effect immediately across all open controls without a restart. Investigate `AccentService.Apply` and `IThemeService` — ensure they push updates through the resource dictionary live, and verify that all themed controls (including `StatePill`, `SpeedGraph`, custom brushes) respond to the change. Verify with FlaUI screenshot before/after toggle.

- **Vue 3 WebUI — outstanding work** — the Vue SPA (`webui/`) replaces the old native HTML client and is functional for basic torrent management. Gaps vs. full parity:

  *Transfers view*
  - [x] Column sort — clicking a column header should toggle asc/desc sort; the sort state should persist in `UiState.TransfersGrid` (backend already stores `SortDirection`).
  - [x] State filter sidebar — filter rows by status category (All / Downloading / Seeding / Completed / Paused / Errored) like qBittorrent's left panel; `GET /api/v2/torrents/info?filter=` already supports this.
  - [x] Category / tag filter sidebar — clicking a category or tag narrows the list; show count badge per entry.
  - [x] Add `.torrent` file upload — the Add Magnet dialog only handles magnet URIs; add a file input that posts to `POST /api/v2/torrents/add` with `multipart/form-data` (torrents field).
  - [x] Column visibility toggle — right-click column header to show/hide columns; persist to `UiState.TransfersGrid.Columns`.
  - [x] Torrent detail tabs — the current expandable row shows minimal info; wire up full tabs: **General** (hash, save path, comment, creation date), **Trackers** (URL, status, seeds/leeches), **Peers** (IP, client, progress, speeds), **Content** (file tree with priority dropdowns), **Pieces** (bitfield bar).

  *Log view*
  - [x] Log detail pane — clicking a log row shows the full message in a side/bottom panel (useful for long messages truncated in the table).

  *Settings view*
  - [x] Connection settings — listen port, UPnP toggle; write through `POST /api/v2/app/setPreferences`.
  - [x] Speed limits — global up/down rate fields, alt-speed rate fields, scheduler (days + time window).
  - [x] Downloads settings — default save path (folder picker or text field), pre-allocate disk, auto-TMM.
  - [x] BitTorrent settings — DHT / PeX / LSD toggles, encryption mode selector.
  - [x] WebUI settings — bind address, remote access toggle, port, auth username/password change.
  - [x] Settings save wires to `POST /api/v2/app/setPreferences` — the Appearance section (theme/accent) already does this but the other sections are stubs.

  *Missing views*
  - [x] RSS view — feed tree (add/remove/refresh), article list with read/unread state; mirrors the RSS tab in qBittorrent.
  - [x] Search view — Torznab plugin search form + results table with one-click add; `GET /api/v2/search/start` + polling `GET /api/v2/search/results`.

  *Status bar / global polish*
  - [x] Speed graph in status bar — sparkline of last 60s download/upload speeds, updated from `sync/maindata` `server_state`.
  - [x] DHT node count and connection status badge in the status bar (already in `server_state.dht_nodes` / `connection_status`).
  - [x] Global speed-limit quick-toggle — click the down/up speed readout to set a temporary cap (like qBittorrent's status bar controls).
  - [x] Remote access — `BindAddress` plumbing already exists in `WebUiSettings`; expose an "Allow remote access" toggle in Settings/WebUI that sets `0.0.0.0` vs `127.0.0.1` and restarts Kestrel.

- **qBittorrent feature parity gaps** — user-visible actions missing from WinBit:
  - [x] Rename torrent (post-add).
  - [x] Rename file within a torrent (post-add).
  - [x] Rename folder within a torrent (post-add) — libtorrent has no native folder rename; requires renaming all files whose RelativePath starts with the old folder prefix.
  - [x] Per-file download priority (selective download) — set file to Normal / High / Maximum / Do Not Download after the torrent is added.
  - [x] Sequential download toggle (post-add) — mirrors the existing `AddTorrentParams.Sequential` but there's no UI action to toggle it on a running torrent.
  - [x] First/last piece priority toggle (post-add) — same gap as sequential.
  - [x] Relocate save path for an existing torrent (move storage).
  - [x] Force-start per torrent (bypass the download queue limit).
  - [x] Add / edit / remove trackers on an existing torrent.
  - [x] Add / edit / remove web seeds on an existing torrent.
  - [x] Export `.torrent` file from an added torrent.
  - [x] Manually add peers to a torrent.

- **Test suite regressions** — pre-existing failures detected during WebUI backlog work (not introduced by any single recent commit):
  - [x] `WebUiWhitelistTests` — `IsWhitelistedIp_returns_false_when_list_empty` returns True instead of False; `Empty_whitelist_keeps_requiring_cookie` returns 200 instead of 401. Whitelist auth logic appears inverted.
  - [x] WebUI asset fallback/MIME routing fixes in test host.
  - [x] `NativeClientTests` — `Hashed_assets_are_served_with_immutable_cache_header` has null `MaxAge`. Cache-control header not set on asset responses in test host.
  - [x] WebUI routing — Vue SPA serves at `/` and API endpoints take precedence over static asset routes.
  - [x] `AppEndpointsTests.TransfersGridHiddenColumns_roundtrips_through_setPreferences_and_getPreferences` — returns 401 Unauthorized; test auth setup may not be setting the SID cookie correctly after a setPreferences call. Investigate test fixture.

- **Post-v0.1 quality improvements** — identified via QA pass after full TASKS.md completion:
  - [x] `SettingsService` unit tests — zero test coverage; add JSON round-trip, `UpdateAsync` mutation + event ordering, concurrent update serialization, and `AppSettings` default-value assertions.
  - [x] `LibTorrentSessionService` edge-case tests — add tests for resume-blob round-trip validation, handle-map cleanup on torrent removal, and alert-pump batching with multiple pending alerts.
  - [x] WebUI Log view — add time-range filter (last hour / today / all) and severity isolation (Error only / Warning+ / All) matching qBittorrent's log filter behavior.
  - [x] WebUI Transfers — add `/stop` and `/start` route aliases on the torrents endpoint (qBittorrent v5 renamed `/pause`→`/stop` and `/resume`→`/start`; JS API was getting 404).
  - [x] WebUI Transfers — per-torrent pause/resume returns 200 but torrent state never transitions to Paused; investigate `LibTorrentSessionService.PauseAsync` and snapshot state mapping (pre-existing, also affects the `/pause` route).
  - [x] Content tab: per-file progress shows 0.0% for resume-loaded in-progress torrents (magnet path hardcoded 0L)
  - [x] Speed tab: wire data pipeline — SpeedGraph control was render-ready but had no speed polling, ViewModel properties, or XAML bindings
  - [x] Transfer list "Added" column always showed "—" — `TorrentSnapshot` lacked date fields; added `AddedUtc`/`CompletedUtc` to snapshot and a `_dateLookup` cache in `LibTorrentSessionService`
  - [x] Transfer list "Completed" column always shows "—" — `TorrentStateRecord.CompletedUtc` is never written when a torrent finishes downloading mid-session; wire `TorrentFinished`/`TorrentState.Seeding` transition to upsert `CompletedUtc` and update `_dateLookup`
  - [x] Tracker filter sidebar shows all tracker hostnames including erroring/dead ones — with 2 torrents carrying large tracker lists the sidebar grows to 100+ useless entries; should only accumulate hosts from trackers with `Working` status (matching qBittorrent's sidebar behavior)
  - [x] Tracker error log entries show empty `err=` — `TrackerErrorAlert.ErrorMessage` is the tracker's failure_reason (HTTP response body); empty for network errors (ECONNREFUSED, timeout). Fall back to `Alert.Message` (libtorrent's `alert::message()`) which always includes the system error reason.
  - [x] Log flooded with tracker-announce, tracker-reply, and peer-disconnect entries at Normal severity — qBittorrent does not surface these in the main log (they appear in the Trackers/Peers detail tabs). Suppress peer-alert and tracker-announce/reply log writes; keep tracker-error and significant session events only.
  - [x] Diagnostic log entries flood the main log every 5 s — "Libtorrent diag" header, alert histogram, per-torrent state+flags dumps, seeding-health, and peer-detail lines are developer-internal telemetry that qBittorrent never shows in its main log. Remove the entire `EmitDiag`/`EmitPeerDiag` subsystem (counters, fields, methods, `diagDue` guard). Fix tracker error `err=` field to show just the human-readable error reason rather than the full `alert::message()` (which repeats torrent name, URL, endpoint, protocol version, and retry count). Remove Speed-tab developer placeholder text "Rolling 10-minute window populates once the selection + per-tab poll lands." from `TransfersPage.xaml`.
  - [x] "Add web seed" button disabled even when a torrent is selected — `OnPropertiesPivotSelectionChanged` called `SetSelectedTorrent` with `ViewModel.SelectedTorrentRow` which is stale on first TableView click (TwoWay binding doesn't fire). Fix: read `TransfersGrid.SelectedItem` directly instead.
  - [x] Accessibility: Content tab list items expose `TorrentFileEntry { Index = N, Name = ..., ... }` as their automation name (default `ToString()`). Set `AutomationProperties.Name` to the file name in the Content tab `DataTemplate` so screen readers announce the filename.
  - [x] Accessibility: Transfer list rows expose `WinBit.ViewModels.Transfers.TransferRowViewModel` as their automation name. Set `AutomationProperties.Name` to the torrent name on each row so screen readers announce it correctly.
  - [x] Accessibility: Tracker tab rows expose `WinBit.ViewModels.Transfers.TrackerRowViewModel` as their automation name. Override `ToString()` to return the tracker URL so screen readers announce the tracker address.
  - [x] Accessibility: Peers tab rows expose `WinBit.ViewModels.Transfers.PeerRowViewModel` as their automation name. Override `ToString()` to return the peer's IP:port address so screen readers announce it correctly.
  - [x] Log flooded with "skipping tracker announce (unreachable)" tracker error entries — libtorrent emits `tracker_error_alert` when it skips an announce for a known-unreachable tracker; these carry no actionable info and flood the log when a torrent has many dead trackers. Suppress them alongside the already-silenced tracker-announce/reply alerts.
  - [x] Log still flooded with duplicate tracker error entries — multiple torrents sharing the same dead tracker, plus multi-endpoint UDP/HTTP attempts, fire several alerts per tracker URL per announce cycle. Add a per-URL 5-minute cooldown so only the first error per tracker is logged; subsequent identical failures within the window are silently dropped.
  - [x] Tray icon Critical error on startup — `H.NotifyIcon` throws `FileNotFoundException` in `TaskbarIcon.OnIconSourceChanged` when loading the tray icon image URI. Investigate whether the icon asset path is wrong for the MSIX-packaged layout and fix the URI so the tray icon loads without an unhandled exception.
  - [x] RSS feed list empty state — the left panel of the RSS page was completely blank when no feeds were configured, violating the "illustrated, inviting, and actionable" bar. Added a `FeedEmptyState` StackPanel with RSS glyph, "No feeds" heading, and "Add feed…" hint that shows when `TreeItems` is empty.
  - [x] Log flooded with `UdpError` Warning entries from loopback (127.0.0.1, ::1) — libtorrent fires `udp_error_alert` when LSD/DHT socket operations fail to loopback; these are not actionable. Added a typed `UdpErrorAlert` case that suppresses loopback-origin errors and only forwards real external UDP failures to `LogNoisyAlert`.
  - [x] Transfer list right-click context menu never opens — `WinUI.TableView` marks `RightTapped` as Handled before bubbling (same as `Tapped`), so `RightTapped="..."` XAML handler and the `ContextFlyout` auto-open both fail silently. Fixed by switching to `AddHandler(UIElement.RightTappedEvent, handler, handledEventsToo: true)` and calling `flyout.ShowAt()` explicitly in the handler.
  - [x] Search page empty state — the results card is completely blank on initial load (StatusText is null, no prompt shown), violating the "illustrated, inviting, and actionable" bar. Replace the plain centered TextBlock with two illustrated StackPanels: a pre-search state (search glyph + "Search for torrents" heading + subtitle) and a post-search no-results/no-providers state (search glyph + StatusText message).
  - [x] Log flooded with `PortmapError` Warning entries on startup — libtorrent fires `portmap_error_alert` (surfaced as `PortmapErrorAlert`) for every network interface where UPnP/NAT-PMP finds no router. "No router found" is not actionable; suppress those entries silently while letting real portmap errors through.
  - [x] "Toast notifications unavailable: Unspecified error" Warning logged on every startup — the toast notification COM server registration may not be properly set up for the MSIX debug build. Investigate `IToastNotificationService` registration and silence or fix the Warning so it only appears when toast notifications are genuinely expected to work.
  - [x] Content tab shows "No file data available" when switching from another details tab (e.g. Peers → Content) while a torrent is already selected — the file list only populates on fresh torrent selection, not on tab-become-visible with an already-selected torrent. Fix: trigger a content refresh when the Content tab is activated with a pre-selected torrent.
  - [x] Watched Folders empty state is plain text ("No watched folders. Click Add folder to get started.") — violates the "illustrated, inviting, and actionable" bar. Replace with a StackPanel containing a folder glyph (E8B7), "No watched folders" heading, and an actionable subtitle matching the RSS/Search pattern.
  - [x] Peers tab Flags column always shows "—" — `PeerInfoFormatter.BuildFlags` only checked `IsSeeder` and `IsEncrypted`, discarding all other libtorrent peer_info flags. Port qBittorrent's full `determineFlags()` character set (D/d/U/u/K/?/O/S/I/H/X/L/E/e/P/h) using the 12 new boolean properties added to `PeerInfo` and mapped from `LibtorrentSharp.Enums.PeerFlags` + `PeerSource`.
  - [x] General tab Pieces field shows "—" for magnet-added torrents — `GetTorrentDetailAsync` MagnetHandle branch sets `totalPieces` from `magnet.NumPieces` but never sets `pieceLength`, because `MagnetHandle` lacked a `PieceLength` property. Fix: add `PieceLength` to `MagnetHandle` (via `GetTorrentInfo` → `TorrentInfoPieceLength` → `FreeTorrentInfo`) and wire it in the service's else branch.
  - [x] General tab ALL fields blank for resume-loaded torrents on restart — three compounding bugs: (1) `GetTorrentDetailAsync` returned null immediately when `stateRecord` was null, but `AddedDate`/`CompletionDate` are the only state-record fields — all other info (info hash, save path, pieces) is available on the handle; fixed by removing the early return and making `TorrentDetailInfo.AddedDate` nullable. (2) `MagnetHandle.PieceLength` (new property) threw `SEHException` for resume-loaded seeding handles because `get_torrent_info()` is not safe on those handles — `torrent_info` is not retained in the native session after downloading completes via resume blob; fixed by catching `SEHException` in `PieceLength` and returning 0.
