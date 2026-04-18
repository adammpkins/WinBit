# TASKS.md — WinBit milestone roadmap

Each milestone ships something usable. Each honors the "modern and beautiful" bar for the surface it touches. Check off deliverables as they land; do not mark a milestone complete until its verification section passes.

---

## M1 — Scaffolding  *(in progress)*

**Goal:** Three-project solution, DI host, Mica shell with NavigationView, theme service, illustrated empty state. Build, test, and launch clean.

**Deliverables**
- [x] `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig` at solution root.
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
- [ ] `TorrentSessionService` implementing `ITorrentSessionService` fully.
- [ ] `TorrentHandle`, `TorrentSnapshot`, `PeerInfo`, `TrackerInfo`, `AddTorrentParams`, `TorrentState`.
- [ ] `StatusPollingLoop` at 1 Hz raising batched `TorrentUpdated`.
- [ ] Fast-resume blob persistence via `ITorrentStateStore`.
- [ ] `UrlDownloader` for adding from HTTP(S) URLs.
- [ ] Engine lifecycle hooked into `WinBitHostedService`.
- [ ] Spike report in `docs/torrent-engine.md`: MonoTorrent coverage of BEP 52 (v2 torrents), super seeding, encryption modes, UPnP, choking algorithms. Document gaps.
- [ ] Tests: add magnet to loopback tracker, observe state, save/load fast-resume.

**Design constraints (this milestone)**
- Polling overhead invisible to user — no input lag, no GC spikes.

**Verification**
- Add a magnet via the API, watch state transition to Downloading, restart app, torrent resumes without re-checking (if fast-resume valid).
- Engine starts/stops cleanly with `IHost.StartAsync` / `StopAsync`.

---

## M4 — Transfer list & properties panel

**Goal:** The central UI. DataGrid with live updates, properties pivot, Add-Torrent editor dialog, drag-drop.

**Deliverables**
- [ ] `TransfersPage` hosts CommunityToolkit `DataGrid` bound to `AdvancedCollectionView<TransferRowViewModel>`.
- [ ] Row columns: Name, Size, Progress (inline bar), State (pill), Seeds, Peers, Down, Up, Ratio, ETA, Added, Completed, Category, Tags.
- [ ] Column reorder, resize, sort persist via `AppSettings.UiState`.
- [ ] `StatePill` and `PiecesBar` (Win2D) controls.
- [ ] Properties `Pivot` below grid: General, Trackers, Peers, Content, Speed.
- [ ] `AddTorrentDialog`, `AddMagnetDialog`, `DownloadFromUrlDialog` — tabbed editors with file preview tree, save-path combobox, category presets, tag chips, share-limits.
- [ ] `SpeedGraph` (Win2D) scrolling line chart on Speed tab.
- [ ] Context menu (pause, resume, remove, force recheck, force reannounce, open folder, copy magnet).
- [ ] Drag-drop `.torrent` files onto window opens AddTorrentDialog.
- [ ] Row updates via INPC only; zero collection rebuilds on tick.

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
- [ ] `ICategoryService` + `ITagService` + persistence.
- [ ] Category/tag sidebar filter tree.
- [ ] Category/tag editor dialogs.
- [ ] Share limits dialog (global + per-torrent): ratio, seeding time, action on limit.
- [ ] Per-torrent speed limit dialog.
- [ ] Auto Torrent Management (TMM) path resolution mirroring qBittorrent's category options.
- [ ] Parity unit tests for TMM path rules.

**Verification**
- Assigning a category moves files to that category's save path when TMM is on.
- Share limit triggers configured action (pause/remove/super-seed).

---

## M6 — Filters, status bar, statistics

**Deliverables**
- [ ] Status filter sidebar (Downloading/Seeding/Completed/Paused/Active/Inactive/Errored).
- [ ] Tracker filter sidebar grouped by host.
- [ ] Session status bar: DHT nodes, global down/up, connection count, alt-speed toggle.
- [ ] `StatsPage`: all-time upload/download, shared, session ratio, DHT nodes, free space.

**Verification**
- Switching filters updates grid in <50 ms with 500 rows.

---

## M7 — Speed controls, scheduler, IP filter, logs

**Deliverables**
- [ ] Global down/up speed limits + alt profile in Settings/Speed.
- [ ] `BandwidthScheduler` (`IHostedService`) — time-of-day rules, parity-tested against qBittorrent's bandwidth scheduler.
- [ ] `PeerGuardianParser` for `.p2p` blocklists; `IpFilterService` wires into engine.
- [ ] Execution log page bound to `ILogService`.
- [ ] Peer log page (banned peers, reason).

**Verification**
- Scheduler flips alt mode at the scheduled time.
- Banned peer IP appears in peer log.

---

## M8 — Networking, watched folders, torrent creator

**Deliverables**
- [ ] Proxy settings (SOCKS5/HTTP) with optional auth.
- [ ] UPnP / NAT-PMP port forwarding toggle via `IPortForwardingService`.
- [ ] Protocol encryption mode selector.
- [ ] DHT / PEX / LSD toggles.
- [ ] `WatchedFolderService` with debounced `FileSystemWatcher` + per-folder options.
- [ ] `TorrentCreatorPage` using MonoTorrent's `TorrentCreator`.

**Verification**
- Drop `.torrent` into watched folder → auto-add within 1 s.
- Created `.torrent` validates via external client.

---

## M9 — RSS + auto-downloader

**Deliverables**
- [ ] `RssService` — feed tree, refresh loop, RSS 2.0 + Atom parsing.
- [ ] `RssPage` with feed tree, article list, manual-download button.
- [ ] `AutoDownloaderPage` with rule CRUD + live-tester.
- [ ] `RuleMatcher` with must-contain, must-not-contain, episode filter, smart episode filter, re-download protection. Parity-tested against qBittorrent's RSS auto-download rule.

**Verification**
- Public RSS feed fetches; matching rule auto-adds torrents.

---

## M10 — Web UI

**Deliverables**
- [ ] In-process Kestrel host via `WebUiService`.
- [ ] Routes matching qBittorrent v2 API: `/api/v2/torrents/*`, `/api/v2/app/*`, `/api/v2/transfer/*`, `/api/v2/rss/*`, `/api/v2/search/*`, `/api/v2/log/*`, `/api/v2/torrentcreator/*`, `/api/v2/sync/*`, `/api/v2/auth/*`.
- [ ] PBKDF2 password hashing, cookie session, optional HTTPS (user cert), IP subnet whitelist.
- [ ] Ship qBittorrent's HTML admin UI as static files (since we implement its API).
- [ ] Native WinUI-style web client replacing qBittorrent's HTML — Fluent-flavored SPA served from the same Kestrel host, toggleable in Settings/WebUI.
- [ ] `qbittorrent-api` (Python) compatibility as CI oracle.

**Verification**
- `qbittorrent-api` can list, start, stop, remove torrents against a running WinBit.

---

## M11 — Tray, notifications, power, shell integration

**Deliverables**
- [ ] `WinBitTrayIcon` via `H.NotifyIcon.WinUI` — show/hide, Add-Magnet, alt-speed, Exit.
- [ ] Close-to-tray option (configurable).
- [ ] Toast notifications via Windows AppNotifications: completion, errors, download-rate low on long torrents.
- [ ] `PowerManagementService` preventing sleep while active torrents exist.
- [ ] `.torrent` file association and `magnet:` URI protocol handler.
- [ ] Single-instance enforcement — second launch forwards magnet to running instance.

**Verification**
- Close to tray → app persists, tray icon visible.
- Completion pops a toast; clicking opens folder.
- System doesn't sleep while a torrent is active.

---

## M12 — Search + polish

**Deliverables**
- [ ] Search plugin host. Preferred: `Python.NET` embedding qBittorrent's Nova3 plugins. Fallback: C# ports of top 5 plugins (`ISearchPlugin`).
- [ ] `SearchPage`: multi-plugin search with progress, filters, one-click download.
- [ ] Localization scaffolding (`.resw` files + a locale picker in Settings).
- [ ] About dialog, first-run wizard, update checker.
- [ ] Accent color picker and theme refinement.
- [ ] Full regression pass of M4..M11.
- [ ] `docs/development.md` finalized with packaging + MSIX steps.

**Verification**
- Search returns results from at least one working provider.
- Localization swaps on the fly.
- All prior milestones still pass their verification.

---

## Backlog / post-M12

Features we deliberately hold until after feature parity ships:

- qBittorrent resume-data import (if users request it).
- Multi-profile (home/work) support.
- Plugin SDK for C# search providers.
- ARM64 first-class support and NativeAOT publishing.
