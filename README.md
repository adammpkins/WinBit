# WinBit

A modern, beautiful, Windows-native BitTorrent client. WinBit is a ground-up rebuild of [qBittorrent](https://www.qbittorrent.org/) in C# / WinUI 3, aimed at feature parity with a Fluent Design front end.

## Why

qBittorrent is functionally excellent but visually dated — it wears its Qt heritage and optimizes for cross-platform pragmatism over native feel. WinBit keeps the feature set and rebuilds the experience around Windows 11: Mica backdrops, extended title bars, Segoe Fluent iconography, tasteful motion, dark/light theming, and controls that feel at home next to Settings and File Explorer.

## Status

Milestone **M1 — Scaffolding** in progress. See [`TASKS.md`](./TASKS.md) for the full roadmap (M1..M12) and [`plans/`](./docs/) for design details.

## Design mission

> **Modern and beautiful.**

This is a first-class design constraint, not a finish-line polish item. It drives control choice, layout, motion, and scope decisions throughout every milestone. The concrete rules live in [`docs/ui-design-language.md`](./docs/ui-design-language.md).

## Architecture at a glance

Three-project solution:

| Project | Purpose |
|---|---|
| `WinBit.Core` | Pure C# class library. BitTorrent engine wrapper (MonoTorrent), settings, persistence (SQLite + JSON), RSS, WebUI (Kestrel), logging. No Windows UI dependencies. |
| `WinBit` | WinUI 3 desktop app. Views, viewmodels, DI composition root, Fluent controls. |
| `WinBit.Tests` | xUnit tests against `WinBit.Core`. |

Full map in [`docs/architecture.md`](./docs/architecture.md).

## Feature goals

Full qBittorrent parity by M12: transfer list, categories/tags, share limits, bandwidth scheduler, RSS + auto-downloader, IP filter, UPnP/NAT-PMP, DHT/PEX/LSD, proxy, Web UI (qBittorrent v2 API compatible), search engine plugins, watched folders, torrent creator, execution log, tray + toasts + sleep prevention, `.torrent` / `magnet:` file associations.

## Build & run

**Prerequisites**

- Windows 11 (or Windows 10 build 17763+).
- Visual Studio 2022 17.10+ with the *Windows App SDK C# Templates* workload **or** .NET 8 SDK + Windows SDK build tools.

**Build**

```pwsh
dotnet build WinBit.slnx -c Debug -r win-x64
```

**Test**

```pwsh
dotnet test WinBit.slnx
```

**Run**

From Visual Studio, set `WinBit` as the startup project and press F5. From the CLI:

```pwsh
dotnet run --project WinBit -c Debug
```

More detail (packaging, MSIX, Python plugin host) in [`docs/development.md`](./docs/development.md).

## Source reference

The full qBittorrent C++ source lives at [`qbittorrent/`](./qbittorrent/) for cross-reference. WinBit does **not** consume it at build time — it's a specification and behavior oracle, especially for the RSS auto-downloader, bandwidth scheduler, category TMM rules, and WebUI API shapes.

## Documentation

- [`TASKS.md`](./TASKS.md) — milestone roadmap.
- [`docs/architecture.md`](./docs/architecture.md) — solution layout + service interfaces.
- [`docs/ui-design-language.md`](./docs/ui-design-language.md) — colors, typography, motion, control rules.
- [`docs/torrent-engine.md`](./docs/torrent-engine.md) — MonoTorrent mapping, gaps, caveats.
- [`docs/persistence.md`](./docs/persistence.md) — SQLite schema, JSON shapes, file-system layout.
- [`docs/webui-api.md`](./docs/webui-api.md) — REST surface (qBittorrent v2 parity).
- [`docs/rss-autodownloader.md`](./docs/rss-autodownloader.md) — rule semantics.
- [`docs/development.md`](./docs/development.md) — build, run, test, package, troubleshoot.

## License

WinBit inherits qBittorrent's GPL obligations where it ports behavior. License text will be finalized in M12 before any distribution; until then treat this repository as source-available for development only.
