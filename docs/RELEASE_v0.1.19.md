# Solasta Coop View v0.1.19

**Experimental first binary release**, Windows x64.

Solasta Coop View is an unofficial co-op quality-of-life mod for Solasta: Crown of the Magister. This release contains a single installable file: **winhttp.dll**.

## New in 0.1.19

- **Auto-continue loading screens:** after a save or location finishes loading, automatically activates the game's own continue callback after a short readiness delay. Enabled by default; configurable at **F8 → Travel → Loading screens**. Only the native LoadingModal is affected.
- **English UI:** all mod-owned settings, tooltips, status messages and diagnostics shown in the in-game overlay are now English. The game's own localized item and spell text follows your game language.
- **One-file installation:** all managed mod code is embedded in the compiled winhttp.dll. No supplementary config files, ZIPs or runtime dependencies to download from the release.

## Existing experimental features

- Native read-only teammate action, spell and ability panels, with expanded item/spell tooltips.
- Co-op travel screen preview and player cursor sharing.
- Fast-travel vote auto-accept, independently toggleable from loading screen auto-continue.
- Host waypoint auto-follow for locally controlled heroes during exploration.
- Interactive shared chest, corpse and ground loot in the native interface.
- Camera zoom multiplier (default ×1.4), inventory transfer distance toggle.
- Local battle animation speed controls (default ×2, adjustable ×1–×3).
- Photon transport selection, safe JoinRoom retries, extensive logging and F10 diagnostics.

## Installation

1. Exit Solasta on both machines.
2. Download the attached **winhttp.dll**.
3. Back up any existing winhttp.dll next to Solasta.exe.
4. Place the new DLL in the **game installation root**, next to Solasta.exe, **not in Windows system folders**.
5. Use the **same mod version for every co-op participant**, then launch the game.
6. Open **F8 → Travel** to check auto-continue and fast-travel vote settings.

## Known limitations

**Unverified live multiplayer functionality.** Compilation success does not certify the features. Back up your save before playing. Remote loot and movement may be rejected by game-side checks, desynchronize clients or behave differently after game updates. The new auto-continue is restricted to the native LoadingModal and should not dismiss other votes, dialogs, network errors or warnings. If loading behaves unexpectedly, turn off its F8 toggle and collect both clients' logs.

Not affiliated with Tactical Adventures. The compiled release does not redistribute proprietary Solasta or Unity managed assemblies.
