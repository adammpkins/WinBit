"""
qbittorrent-api compatibility oracle for WinBit's Web UI.

Exercises the subset of the qBittorrent v2 API that Sonarr / Radarr rely on:
login, app metadata, torrents.info, torrents.add (magnet), pause / resume, delete.
Run against a live WinBit.WebUiCompatHost process. The host's URL is taken from
the WEBUI_URL env var (default http://127.0.0.1:18080).
"""

from __future__ import annotations

import os
import sys
import time
import traceback

try:
    import qbittorrentapi
except ImportError:
    print("qbittorrent-api is not installed. `pip install qbittorrent-api`.", file=sys.stderr)
    sys.exit(1)


WEBUI_URL = os.environ.get("WEBUI_URL", "http://127.0.0.1:18080")
DEFAULT_USERNAME = os.environ.get("WEBUI_USERNAME", "admin")
DEFAULT_PASSWORD = os.environ.get("WEBUI_PASSWORD", "adminadmin")
SAMPLE_MAGNET = (
    "magnet:?xt=urn:btih:c12fe1c06bba254a9dc9f519b335aa7c1367a88a"
    "&dn=archlinux&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337"
)


def wait_for_host(url: str, timeout: float = 60.0) -> None:
    """Poll /api/v2/app/version until it answers 200."""
    import urllib.request
    import urllib.error

    deadline = time.time() + timeout
    last_error = None
    while time.time() < deadline:
        try:
            with urllib.request.urlopen(f"{url}/api/v2/app/version", timeout=2) as resp:
                if resp.status == 200:
                    return
        except (urllib.error.URLError, ConnectionError, TimeoutError) as exc:  # pragma: no cover
            last_error = exc
        time.sleep(0.5)
    raise TimeoutError(f"WinBit WebUI never came up at {url}: {last_error}")


def main() -> int:
    print(f"Waiting for WinBit WebUI at {WEBUI_URL} ...")
    wait_for_host(WEBUI_URL)

    client = qbittorrentapi.Client(
        host=WEBUI_URL,
        username=DEFAULT_USERNAME,
        password=DEFAULT_PASSWORD,
        VERIFY_WEBUI_CERTIFICATE=False,
    )

    # --- login & version ----------------------------------------------------
    client.auth_log_in()
    app_version = client.app_version()
    api_version = client.app_web_api_version()
    print(f"app_version={app_version!r} web_api_version={api_version!r}")
    assert app_version.startswith("WinBit/"), f"unexpected app version {app_version!r}"
    assert api_version.startswith("2."), f"unexpected api version {api_version!r}"

    # --- build info ---------------------------------------------------------
    build = client.app_build_info()
    print(f"build_info keys: {sorted(build.keys())}")
    for key in ("qt", "libtorrent", "platform", "bitness"):
        assert key in build, f"buildInfo missing {key}"

    # --- empty list ---------------------------------------------------------
    info = client.torrents_info()
    print(f"initial torrents.info count = {len(info)}")

    # --- add a magnet -------------------------------------------------------
    pre = {t.hash for t in client.torrents_info()}
    client.torrents_add(urls=SAMPLE_MAGNET, save_path=None)

    added_hash = None
    deadline = time.time() + 15
    while time.time() < deadline:
        post = client.torrents_info()
        new = {t.hash for t in post} - pre
        if new:
            added_hash = next(iter(new))
            break
        time.sleep(0.5)
    assert added_hash is not None, "magnet add did not produce a new torrent within 15s"
    print(f"added torrent hash={added_hash}")

    # --- pause / resume -----------------------------------------------------
    client.torrents_pause(torrent_hashes=added_hash)
    time.sleep(1.0)
    client.torrents_resume(torrent_hashes=added_hash)
    time.sleep(1.0)

    # --- delete -------------------------------------------------------------
    client.torrents_delete(delete_files=False, torrent_hashes=added_hash)
    deadline = time.time() + 15
    while time.time() < deadline:
        remaining = {t.hash for t in client.torrents_info()}
        if added_hash not in remaining:
            break
        time.sleep(0.5)
    else:
        print(f"torrent {added_hash} still present after delete", file=sys.stderr)
        return 2

    # --- logout -------------------------------------------------------------
    client.auth_log_out()

    print("qbittorrent-api compatibility check passed.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # pragma: no cover
        traceback.print_exc()
        sys.exit(1)
