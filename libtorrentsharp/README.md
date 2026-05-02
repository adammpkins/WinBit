# LibtorrentSharp

A .NET binding for [libtorrent-rasterbar](https://github.com/arvidn/libtorrent), the C++ BitTorrent library that powers qBittorrent, Deluge, and many other clients.

**Status: v0.1.0-alpha** — API is stabilizing; breaking changes may occur between pre-release versions.

## Installation

```
dotnet add package LibtorrentSharp --version 0.1.0-alpha
```

Requires .NET 8.0+ and Windows x64. `lts.dll` is bundled under `runtimes/win-x64/native/` in the package.

## Quick start

```csharp
using LibtorrentSharp;
using LibtorrentSharp.Alerts;

// Create a session
using var session = new LibtorrentSession
{
    DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "downloads")
};

// Add a magnet URI — returns a MagnetHandle until metadata arrives
var magnet = session.Add(new AddTorrentParams
{
    MagnetUri = "magnet:?xt=urn:btih:..."
}).Magnet!;

// Or parse first, then add
var torrent = session.Add(MagnetUri.Parse("magnet:?xt=urn:btih:...")).Magnet!;

// Add a .torrent file — returns a TorrentHandle immediately
var info = new TorrentInfo(File.ReadAllBytes("ubuntu.torrent"));
var handle = session.Add(new AddTorrentParams { TorrentInfo = info }).Torrent!;

// Consume the alert stream
await foreach (var alert in session.Alerts)
{
    switch (alert)
    {
        case MetadataReceivedAlert meta:
            Console.WriteLine($"Metadata received: {meta.Subject.GetCurrentStatus().Name}");
            break;
        case TorrentFinishedAlert finished:
            Console.WriteLine($"Finished: {finished.Subject.GetCurrentStatus().Name}");
            return;
        case TorrentErrorAlert err:
            Console.WriteLine($"Error: {err.ErrorMessage}");
            return;
    }
}
```

## What's covered

| Surface | Status |
|---------|--------|
| Session lifecycle (create, configure, dispose) | ✅ |
| Magnet URI and `.torrent` file adds | ✅ |
| Resume data (save / load via `session_params`) | ✅ |
| Torrent control (pause, resume, remove, recheck, reannounce) | ✅ |
| `TorrentStatus` (progress, rates, state, ETA, peer/seed counts) | ✅ |
| Peer info (`PeerInfo` — flags, rates, connection details) | ✅ |
| Tracker info (`TrackerInfo` — announces, scrape) | ✅ |
| File and piece priorities | ✅ |
| Per-file download progress | ✅ |
| Torrent creation (`TorrentCreator`) | ✅ |
| DHT (BEP-5 `get`/`put`, BEP-44 immutable/mutable items) | ✅ |
| IP filter, port mappings, rate limits | ✅ |
| Listen interface management | ✅ |
| Session stats | ✅ |
| Settings (`SettingsPack`) | ✅ broad key coverage |
| Alert stream (`IAsyncEnumerable<Alert>`) | ✅ ~60 typed subclasses; unmapped types surface as base `Alert` with numeric `Type` |
| Hash value types (`Sha1Hash`, `Sha256Hash`, `InfoHashes`) | ✅ |
| `TorrentFlags` bitset | ✅ |
| Ed25519 key generation and signing (BEP-44) | ✅ |

## Architecture

- **`LibtorrentSharp/`** — managed C# library (`net8.0`, AnyCPU). P/Invoke into a native shared library (`lts.dll` on Windows). Idiomatic .NET surface: `IDisposable`, value types for hashes, `IAsyncEnumerable<Alert>` for the alert pump.
- **`LibtorrentSharp.Native/`** — C++ shared library exposing a C ABI (`extern "C"`) over libtorrent. Built via CMake + vcpkg.

LibtorrentSharp wraps the existing C++ libtorrent — it is **not** a C# reimplementation of the BitTorrent protocol.

## Goals

- Full-fidelity coverage of libtorrent's public client-facing surface (`session`, `torrent_handle`, alerts, status/peer/tracker structs, `add_torrent_params`, hash types, magnet parsing).
- Zero consumer-project coupling. No dependency on any specific BitTorrent client.
- NativeAOT-friendly. P/Invoke boundary chosen over C++/CLI for this reason.

## Known gaps (tracked for v0.2)

| Gap | Issue |
|-----|-------|
| v2-only torrent support (SHA-256-only info hashes) | [#1](https://github.com/adammpkins/LibtorrentSharp/issues/1) |
| Plugin API (`session_plugin` / `torrent_plugin`) | [#2](https://github.com/adammpkins/LibtorrentSharp/issues/2) |
| Managed bencode/bdecode surface (use [BencodeNET](https://www.nuget.org/packages/BencodeNET/) in the meantime) | [#3](https://github.com/adammpkins/LibtorrentSharp/issues/3) |
| Linux and macOS native builds | [#4](https://github.com/adammpkins/LibtorrentSharp/issues/4) |
| `StorageMode` and `ConnectionState` enums | [#5](https://github.com/adammpkins/LibtorrentSharp/issues/5) |
| `AnnounceEndpoint` struct (per-endpoint tracker health) | [#6](https://github.com/adammpkins/LibtorrentSharp/issues/6) |
| Runtime tests for `DhtErrorAlert` / `TorrentConflictAlert` | [#7](https://github.com/adammpkins/LibtorrentSharp/issues/7) |

## License

Apache-2.0. Derived from [csdl](https://github.com/aspriddell/csdl) by Albie Spriddell. See [`NOTICE`](./NOTICE) for attribution.
