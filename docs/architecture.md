# Architecture

## Solution layout

```
WinBit\                 # WinUI 3 app host (views, viewmodels, DI composition root)
WinBit.Core\            # Pure C# class library (net8.0) — no Windows UI deps
WinBit.Tests\           # xUnit against WinBit.Core
```

Full directory tree lives in the plan file. Highlights:

- `WinBit.Core/BitTorrent/` — MonoTorrent wrapper (`ITorrentSessionService`, handles, snapshots, IP filter).
- `WinBit.Core/Settings/` — `AppSettings` POCO tree + JSON store.
- `WinBit.Core/Persistence/` — SQLite store for torrent state + fast-resume; JSON for everything else.
- `WinBit.Core/Rss/` — feeds, articles, auto-downloader rules.
- `WinBit.Core/WebUi/` — in-process Kestrel with qBittorrent-v2-compatible REST API.
- `WinBit.Core/Hosting/` — `AddWinBitCore(IServiceCollection)` + hosted services (polling, watched folders, RSS, bandwidth scheduler, WebUI).
- `WinBit/Views` / `WinBit/ViewModels` — Fluent UI.
- `WinBit/Controls` — custom controls (`SpeedGraph`, `PiecesBar`, `StatePill`, `TagChip`, `EmptyState`).

## Core service interfaces

Signatures are sketched in the plan file. Milestones fill them in:

| Service | Milestone | Notes |
|---|---|---|
| `ITorrentSessionService` | M3 | MonoTorrent `ClientEngine` wrapper |
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

## Cross-cutting

- **Logging:** `ILogService` ring buffer feeds the Execution Log page and the Peer Log page. Entries also forwarded to `ILogger<T>` for stdout/file sinks when needed.
- **Events:** Core services expose events (`TorrentAdded`, `TorrentUpdated`, etc.). VMs subscribe via `ObservableRecipient`; subscriptions are disposed on navigation away.
- **Error handling:** `Result<T>` union for expected failures (invalid magnet, unreachable URL, file not found). Exceptions only for bugs.
