# Architecture

Authoritative map of WinBit's solution structure and service boundaries.

## Solution layout

Five projects, referenced by `WinBit.slnx`:

```
<repo root>\                           # WinBit WinUI 3 app host — WinBit.csproj,
                                       # App, MainWindow, Views, ViewModels,
                                       # Services, Controls, Infrastructure,
                                       # Styles, Assets, Strings all live here
WinBit.Core\                           # Pure C# class library (net8.0) — no Windows UI deps
WinBit.Tests\                          # xUnit against WinBit.Core
libtorrentsharp\LibtorrentSharp\       # C# binding to libtorrent (post-M12 engine)
libtorrentsharp\LibtorrentSharp.Tests\ # xUnit for the binding (Network tests opt-in)
```

Full directory tree lives in the plan file. Highlights:

- `WinBit.Core/BitTorrent/` — torrent engine layer. `ITorrentSessionService` contract implemented by `LibTorrentSessionService` (libtorrent adapter via the in-repo LibtorrentSharp binding). Plus bandwidth scheduler, encryption/peer-discovery/speed-profile appliers, torrent creator queue, snapshots, state/error formatting, tracker + peer info types.
- `WinBit.Core/Settings/` — `AppSettings` POCO tree + JSON store.
- `WinBit.Core/Persistence/` — SQLite store for torrent state + fast-resume; JSON for everything else.
- `WinBit.Core/Rss/` — feeds, articles, auto-downloader rules.
- `WinBit.Core/WebUi/` — in-process Kestrel with qBittorrent-v2-compatible REST API.
- `WinBit.Core/Hosting/` — `AddWinBitCore(IServiceCollection)` + hosted services (polling, watched folders, RSS, bandwidth scheduler, WebUI).
- `WinBit.Core/{Categories,Tags,Filters,WatchedFolders,Search,Sharing,Stats,Logging,Networking,Notifications,Power,Shell,Threading,Updates,Common}/` — feature-scoped services (see milestone table below for ownership).
- `Views/` / `ViewModels/` at repo root — Fluent UI bound via `CommunityToolkit.Mvvm`.
- `Controls/` at repo root — custom controls (`SpeedGraph`, `PiecesBar`, `StatePill`, `TagChip`, `EmptyState`).
- `libtorrentsharp/LibtorrentSharp/` — `LibtorrentSession`, `TorrentHandle`, `AddTorrentParams`, alert hierarchy (`Alerts/`), enums (`Enums/`), P/Invoke surface (`Native/`), RID-specific `lts.dll`s (`runtimes/`). Architecture in [`libtorrent-binding.md`](./libtorrent-binding.md).

## Core service interfaces

Signatures are sketched in the plan file. Milestones fill them in:

| Service | Milestone | Notes |
|---|---|---|
| `ITorrentSessionService` | M3 | `LibTorrentSessionService` wraps libtorrent-rasterbar via the in-repo LibtorrentSharp binding. Decision record: [`torrent-engine.md`](./torrent-engine.md). |
| `ISettingsService` + `ISettingsStore` | M2 | POCO tree, JSON persistence, debounced save |
| `ITorrentStateStore` | M2/M3 | SQLite, WAL mode, serialized writer |
| `ICategoryService` / `ITagService` | M5 | With TMM path rules |
| `IWatchedFolderService` | M8 | `FileSystemWatcher` + debounce |
| `IRssService` / `IAutoDownloaderService` | M9 | RSS 2.0 + Atom parser, rule matcher |
| `IWebUiService` | M10 | In-process Kestrel, PBKDF2 auth |
| `ISearchService` | M12 | Python.NET host or C# plugins |
| `ILogService` | M1 | Ring buffer + Channel, usable from day one |
| `IPortForwardingService` | M8 | UPnP/NAT-PMP |
| `IPowerManagementService` | M11 | `SetThreadExecutionState` (impl in `WinBit` app) |

## DI composition

- `App.xaml.cs` builds an `IHost` via `Host.CreateApplicationBuilder()`.
- `services.AddWinBitCore(options)` registers every Core service + `IHostedService`.
- `services.AddWinBitApp()` registers UI-only services (dispatcher, navigation, dialog, theme, toast), all viewmodels (transient), all pages (transient).
- `App.Services` is a static accessor on `App` for views to resolve their VMs.

## Threading

Async end-to-end, single UI dispatcher, 1 Hz batched polling, WAL-mode SQLite with single-writer queue.

## Cross-cutting

- **Logging:** `ILogService` ring buffer feeds the Execution Log page and the Peer Log page. Entries also forwarded to `ILogger<T>` for stdout/file sinks when needed.
- **Events:** Core services expose events (`TorrentAdded`, `TorrentUpdated`, etc.). VMs subscribe via `ObservableRecipient`; subscriptions are disposed on navigation away.
- **Error handling:** `Result<T>` union for expected failures (invalid magnet, unreachable URL, file not found). Exceptions only for bugs.
