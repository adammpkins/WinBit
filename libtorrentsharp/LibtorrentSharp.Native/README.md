# LibtorrentSharp.Native

Native C ABI over libtorrent-rasterbar. Produces `lts.dll` on Windows (and `liblts.so` / `liblts.dylib` on Linux/macOS later).

## Build

Prerequisites: CMake 3.21+, [vcpkg](https://github.com/microsoft/vcpkg), MSVC v143+ with the "Desktop development with C++" workload (the `VC.Tools.x86.x64` component must be installed — vcpkg probes `vcvarsall.bat` and aborts if it's missing).

Run from the `libtorrentsharp/LibtorrentSharp.Native/` directory (or from the repo root — both forms are shown):

```shell
# One-time vcpkg install of libtorrent + magic-enum (x64; arm64 deferred)
vcpkg install libtorrent magic-enum --triplet=x64-windows-static

# Configure + build (run from repo root)
cmake -B libtorrentsharp/LibtorrentSharp.Native/build \
      -S libtorrentsharp/LibtorrentSharp.Native \
      -G "Visual Studio 18 2026" -A x64 \
      -DCMAKE_TOOLCHAIN_FILE=C:/vcpkg/scripts/buildsystems/vcpkg.cmake \
      -DVCPKG_TARGET_TRIPLET=x64-windows-static
cmake --build libtorrentsharp/LibtorrentSharp.Native/build --config Release
```

Output: `build/Release/lts.dll` (~9 MB). Copy to `../LibtorrentSharp/runtimes/win-x64/native/lts.dll` for the managed project to pick it up.

The vcpkg tree is pinned via the `builtin-baseline` in `vcpkg.json` (currently `5e6578b`, 2026-04-20). Manifest-mode builds (the toolchain file above) resolve `libtorrent` 2.0.11 and `magic-enum` 0.9.7 from that baseline into `build/vcpkg_installed/`. Cold first install takes minutes to ~1 hour depending on binary-cache hits for the Boost / OpenSSL chain; warm reinstalls are seconds.

### CRT linkage

The `x64-windows-static` triplet compiles libtorrent and its dependencies with the static CRT (`/MT`). `CMakeLists.txt` sets `CMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded$<$<CONFIG:Debug>:Debug>` immediately after `project()` to match — without this the link fails with dozens of `LNK2038: mismatch detected for 'RuntimeLibrary'` errors between our objects and libtorrent's archive. If you switch to a non-static triplet (e.g. `x64-windows`), drop that setting.

## Structure

- `include/` — public C headers (`extern "C"`) consumed by the P/Invoke layer in `LibtorrentSharp/`.
- `src/` — C++ implementation linking libtorrent.
- `vcpkg.json` — dependency manifest pinning libtorrent ≥ 2.0.11 against the `5e6578b` baseline.
- `CMakeLists.txt` — build configuration.

## C ABI stability

The C ABI is the contract between this project and `LibtorrentSharp/`. Breaking changes require a coordinated commit touching both the native headers and the managed `[DllImport]` declarations.
