# WinBit Web UI compatibility oracle

Exercises the qBittorrent v2 API surface WinBit exposes against the real
[`qbittorrent-api`](https://pypi.org/project/qbittorrent-api/) Python client —
the same library Sonarr, Radarr, and Lidarr use. Drives WinBit from an
external process so any accidental signature drift from the qBittorrent
contract fails the CI oracle.

## Run locally

```bash
# 1. Start the host (terminal 1)
dotnet run --project tools/webui-compat/WinBit.WebUiCompatHost.csproj

# 2. Drive it with the Python test (terminal 2)
cd tools/webui-compat
python -m venv .venv && . .venv/Scripts/activate   # or .venv/bin/activate
pip install -r requirements.txt
python compat_test.py
```

The host defaults to `http://127.0.0.1:18080` and uses a fresh scratch
directory under `%TEMP%`. Override with `WEBUI_PORT` and `DATA_ROOT` env vars.

## CI

See `.github/workflows/webui-compat.yml` — the workflow matches the manual
steps above and fails the build if the Python oracle reports an error.
