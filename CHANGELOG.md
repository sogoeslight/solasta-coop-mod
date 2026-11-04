# Changelog

## 0.1.26

- Uses stable saved hero names for identity in the lobby, remote snapshots and loaded party. Lobby ordering now carries over even when clients display differently filtered names.
- Adds fixture coverage using filtered-name scenarios and the real game's GuiCharacter, hero snapshots and loaded hero identity.

## 0.1.25

- Adds host-controlled party ordering directly above the multiplayer lobby character cards. Select one slot, then another to exchange their positions; select the source again to cancel.
- Shares the chosen order with guests and later arrivals while preserving native character bindings, player ownership, snapshots and slot/GUID mappings.
- Carries the arrangement into a new or loaded game through Solasta's existing party-swap method. Later manual in-game ordering is respected.
- Blocks edits while the lobby is covered by a modal, launching, awaiting a previous update or leaving a room. Restores the native layout when the lobby closes or its roster changes.
- Adds lobby-order diagnostics and validation of all 24 four-hero permutations, host/guest editing, ownership retention, stale data and the real game swap implementation.

## 0.1.24

- Starts the F6 travel preview hidden. Guests request screen frames only after enabling the preview.
- Makes Follow host the initial dialogue mode, including the first upgrade from the previous preference generation. Later mode changes are remembered and the host's policy is synchronized to guests.
- Fixes follow retargeting being blocked by IsCharacterActing during the follower's own exploration walk. New targets use the native replacement command with a cooldown; manual movement and other actions remain protected.
- Drops stale host waypoints when the host stops or cancels movement, exploration pauses, combat starts or the location changes. Tracks elevation changes and retries transient command failures without requiring the toggle to be restarted.
- Adds auto-follow and host-movement state to diagnostic reports and periodic verbose logs, including idle and blocked periods.
- Adds timed host/guest regression fixtures for repeated waypoints, manual movement, interactions, pauses, stale routes, delayed network echoes, transient failures, ownership and packet ordering.

## 0.1.23

- Fixes a reproduced native Mono crash during empty-loot initialization after existing shared-loot hooks have been installed. Removes the additional loot-window and highlight Harmony detours; the supplied 0.1.22 save-load log stopped inside this stage.
- Keeps automatic empty-loot closing by observing native windows, waiting for pending transfers and a stable empty inventory, and invoking the native cancel handler once.
- Keeps opened-empty-container highlight filtering through the original visibility method after gameplay updates. Disabling the option or refilling the container restores native highlighting; unopened containers remain visible.
- Adds regression checks using the real Solasta 1.5.97 highlight implementation, existing shared-loot generic callbacks and isolated loot windows. The old installer aborts in this fixture; the new implementation passes. Default exploration speed remains ×1.75 and combat speed ×1.5.

## 0.1.22

- Defers new gameplay hook initialization until a location is loaded, keeping the Bootstrap scene on the previous startup path.
- Replaces autosave detours on empty virtual acknowledgement methods with a call-site hook before trigger acknowledgement.
- Adds initialization-stage logging and a startup regression fixture with a genuinely empty virtual game-shaped method.
- Sets default exploration movement speed to ×1.75 and combat speed to ×1.5, while retaining saved custom values.
- Corrects the native loader log version label.

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
