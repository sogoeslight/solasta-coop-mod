# Changelog

## 0.1.21

- Actual combat movement and baked movement waits now use the speed multiplier.
- Separate combat and exploration sliders from ×1 to ×3, with ×1/×2/×3 presets.
- Host-synchronized speed and dialogue policies, with persistent preferences.
- Automatic close for empty chest, corpse and ground-loot windows after transfers finish.
- Optional suppression of opened empty container highlights.
- Native pre-dialogue checkpoints before interactions and scripted trigger acknowledgement, respecting save restrictions.
- Vanilla, Follow host and Random tie voting; equal votes, a single host-authored outcome and 50/50 resolution for a two-player tie.
- Switching between Warlock invocation and pact spell previews no longer closes the new selector.


This project is a reconstructed source history from archived development snapshots. Changes below describe intended functionality, not confirmed multiplayer behavior or release certification.

## 0.1.20 (experimental)
- Rename the mod to Solasta Coop Mod throughout the player-facing interface, logs, build output and documentation.
- Simplify F8 settings: short toggle labels and concise descriptions, without experimental warnings in the overlay.
- Add explicit toggles for combat animation acceleration, extended camera zoom and host waypoint sharing.
- Add a TCP transport toggle, while keeping room-join retries independently configurable.
- Preserve existing co-op packet identifiers to avoid unnecessary protocol changes.

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
