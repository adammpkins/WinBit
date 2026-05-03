# Privacy

WinBit collects **nothing** about you. There is no analytics SDK, no crash reporter phoning home, no usage telemetry — the maintainer has no server to send anything to. All your settings, torrent state, and downloaded data stay on your machine.

## What WinBit does over the network — all under your control

| Activity | When | What's sent | Where |
|---|---|---|---|
| **BitTorrent protocol** | Whenever you have an active torrent | Your IP address and the info-hashes of your torrents | Peers, trackers, and the DHT (this is how BitTorrent works) |
| **RSS feed fetches** | On the schedule you configure in Settings → RSS, for feeds you added | An HTTP `GET` to each feed URL | The RSS publishers you added |
| **Search-indexer queries** | When you run a search in the Search tab, against indexers you configured (Jackett / Prowlarr / Torznab) | The search query | The indexer endpoints you added |
| **Update check** | If you enable it in Settings → Behavior, on app start and on demand | An unauthenticated `GET https://api.github.com/repos/adammpkins/WinBit/releases/latest` | GitHub |
| **Web UI** | If you enable it in Settings → Web UI | The Web UI's HTTP endpoint binds to `127.0.0.1` by default — local only. Only listens beyond localhost if you explicitly change the bind address. | Whoever can reach the bind address you chose |

## Local data

- **Settings**: `%LOCALAPPDATA%\WinBit\settings.json`
- **Torrent state and fast-resume blobs**: `%LOCALAPPDATA%\WinBit\winbit.db` (SQLite)
- **Logs**: `%LOCALAPPDATA%\WinBit\logs\winbit-YYYY-MM-DD.log`
- **Downloads**: wherever you set the save path (default: `%LOCALAPPDATA%\WinBit\downloads`)

Nothing in those files is transmitted anywhere. They exist for the app to remember your preferences, resume torrents efficiently, and let you diagnose problems.

## Third-party services WinBit does not use

- No Google Analytics, Mixpanel, PostHog, Segment, or other usage analytics
- No Sentry, AppCenter, Bugsnag, or other crash reporting
- No advertising SDKs of any kind
- No account system, no sign-in
- No cloud sync, no remote settings backup

## Disabling network features

You can turn off every outbound network activity except the BitTorrent protocol itself:

- **Update check**: Settings → Behavior → uncheck "Check for updates on startup"
- **Web UI**: Settings → Web UI → uncheck "Enable Web UI"
- **DHT / PEX / LSD**: Settings → BitTorrent → uncheck the peer-discovery options
- **Trackers**: don't add torrents that use trackers (or use the in-app tracker editor on a per-torrent basis)
- **RSS feeds and search indexers**: only fetched if you add them

## Questions or concerns

Open an issue at <https://github.com/adammpkins/WinBit/issues>.

## Changes to this policy

This page is part of the WinBit repository and follows its versioning. The git history at <https://github.com/adammpkins/WinBit/commits/main/docs/privacy.md> shows every revision.
