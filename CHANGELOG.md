# Changelog

This project is a reconstructed source history from archived development snapshots. Changes below describe intended functionality, not confirmed multiplayer behavior or release certification.

## 0.1.19 (experimental)
- Automatically continue the native loading confirmation after loading a save or entering a location (LoadingModal), with a 0.65-second readiness delay and an F8 toggle.
- Keep the existing fast-travel vote auto-accept as a separate feature; normal votes, warnings and dialogs are not touched.
- Translate all mod-owned UI and status text to English.
- Build and distribute only winhttp.dll through GitHub Actions and Releases.

## 0.1.18
- Add configurable local combat animation playback from x1 to x3, default x2; no change to global time scale or Photon timers.

## 0.1.17
- Experimental interactive native shared loot: players can attempt to loot chests, corpses and ground items using locally controlled characters; relaxes UI-only range restrictions.

## 0.1.16
- Relay host movement waypoints through Photon for follower movement during exploration, with positional fallback.

## 0.1.15
- Add fast-travel vote auto-accept and improve follower target selection and responsiveness.

## 0.1.14
- Add character auto-follow movement commands and retry native remote loot UI opening.
- Fix UnityEngine.Object versus object ambiguity in the C# build.

## 0.1.13
- Add initial camera follow button and inventory recipient range adjustment.

## 0.1.12
- Replace experimental screenshot-streamed loot preview with native loot UI prototype, disable F11 screenshot streaming.
- Default camera zoom-out multiplier to x1.4.

## 0.1.11
- Add coop turn/desync diagnostics and first experimental shared-loot implementation.

## 0.1.10 and earlier
- Add Photon TCP/retry diagnostics, observer UI for teammate spells/actions, target ranges, expanded tooltips, travel previews and source tests.

See [README](README.md), [architecture](docs/ARCHITECTURE.md) and [troubleshooting](docs/TROUBLESHOOTING.md) for details and limitations.
