# Development

Build, run, test, package.

## Prerequisites

- **Windows 11** (or Windows 10 build 17763+).
- **.NET 8 SDK** (`dotnet --list-sdks` must show `8.x`).
- **Windows App SDK 1.8** tooling. Easiest via Visual Studio 2022 17.10+ with the *Windows App SDK C# Templates* workload. CLI-only works too; see [Microsoft's CLI setup](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/set-up-your-development-environment).
- **Windows 10 SDK** build 19041 or later.

## Build

```pwsh
dotnet build WinBit.slnx -c Debug -r win-x64
```

Release:

```pwsh
dotnet build WinBit.slnx -c Release -r win-x64
```

## Run

From Visual Studio: set `WinBit` as startup project, F5.

From CLI:

```pwsh
dotnet run --project WinBit -c Debug
```

## Test

```pwsh
dotnet test WinBit.slnx
```

Unit tests live in `WinBit.Tests`. The smoke test (M1) verifies `AddWinBitCore` composes. Later milestones add parity tests (M7, M9), persistence tests (M2), engine tests (M3), and WebUI tests (M10).

## Publishing (post-M12)

MSIX packaging via the single-project MSIX tooling baked into `WinBit.csproj`:

```pwsh
dotnet publish WinBit -c Release -r win-x64 -p:PublishProfile=win-x64.pubxml
```

Unpackaged deployment is also supported (useful when MSIX sandbox limits interfere with shell integration or Python embedding).

## Troubleshooting

- **`PublishTrimmed` errors on Debug builds** — trimming is disabled in Debug by project config. Ensure the configuration actually builds Debug.
- **WinUI 3 designer in Visual Studio crashes** — known VS issue; close the designer and edit XAML as text.
- **Mica not rendering** — requires Windows 11 22H2+; on older builds the backdrop gracefully falls back to solid.
- **SQLite `SQLITE_BUSY`** — a writer loop is mis-sharing its connection. Routes must go through `SqliteWriteQueue`; reads should use the read-only pool.

## Workflow for new features

## Python search plugins (M12)

Deferred decision. Expected path: `Python.NET` hosts qBittorrent's Nova3 plugins. Runtime Python 3.10+ will need to be discoverable. If embedded Python proves too heavy, fallback: C# ports of top 5 plugins under `WinBit.Core/Search/Plugins/`.
