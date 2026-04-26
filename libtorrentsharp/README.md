# LibtorrentSharp

A .NET binding for [libtorrent-rasterbar](https://github.com/arvidn/libtorrent), the C++ BitTorrent library that powers qBittorrent, Deluge, and many other clients.

**Status: early scaffolding.** This project is currently incubated inside the [WinBit](https://github.com/adampmcdev/winbit) repository under `libtorrentsharp/` while the API surface stabilizes. It will extract to a standalone repository and publish to NuGet once the public surface is ready for a v0.1 release.

See [`docs/libtorrent-binding.md`](../docs/libtorrent-binding.md) in the parent repository for the architecture, roadmap, and priorities.

## Architecture

- **`LibtorrentSharp/`** — managed C# library (net8.0, AnyCPU). P/Invoke into a native shared library (`lts.dll` on Windows). Idiomatic .NET surface: proper `IDisposable`, value types for hashes, `IAsyncEnumerable<Alert>` for the alert pump.
- **`LibtorrentSharp.Native/`** — C++ shared library exposing a C ABI (`extern "C"`) over libtorrent. Built via CMake + vcpkg. Windows x64 + arm64 in the first pass; Linux and macOS follow.

LibtorrentSharp wraps the existing C++ libtorrent — it is **not** a C# reimplementation of the BitTorrent protocol.

## Goals

- Full-fidelity coverage of libtorrent's public client-facing surface (`session`, `torrent_handle`, alerts, status/peer/tracker structs, add_torrent_params, hash types, magnet parsing).
- Zero consumer-project coupling. The binding has no dependency on any specific client.
- NativeAOT-friendly. P/Invoke boundary chosen over C++/CLI for this reason.

## Non-goals

- Plugin API (`session_plugin`, `torrent_plugin`) — C++ extension points don't translate cleanly to managed code.
- Raw bencode/bdecode — use [`BencodeNET`](https://www.nuget.org/packages/BencodeNET/) instead.
- Cross-platform in v0.1. Windows x64 + arm64 first.

## License

Apache-2.0. Derived from [csdl](https://github.com/aspriddell/csdl) by Albie Spriddell. See [`NOTICE`](./NOTICE) for attribution.
