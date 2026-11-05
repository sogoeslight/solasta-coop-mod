# Solasta Coop Mod

Quality-of-life improvements for [Solasta: Crown of the Magister](https://store.steampowered.com/app/1096530/Solasta_Crown_of_the_Magister/) co-op. Configure the mod with **F8**.

**Version 0.1.27** | Windows x64 | [GitHub download](https://github.com/sogoeslight/solasta-coop-mod/releases/latest) | [Nexus Mods](https://www.nexusmods.com/solastacrownofthemagister/mods/301)

## Features

| Feature | What it does | Control |
| --- | --- | --- |
| Lobby party order | Swaps hero positions for the room and carries the order into the game, preserving hero ownership | On each lobby card, below the owner dropdown |
| Combat speed | Accelerates actual movement, movement waits, animations and VFX | F8 → Speed, ×1–×3, default ×1.5 |
| Exploration speed | Accelerates character movement around locations; cinematic movement keeps native timing | F8 → Speed, ×1–×3, default ×1.75 |
| Dialogue votes | Vanilla, follow the host without guest clicks, or equal votes with a random tie-break (50/50 for two different votes) | F8 → Dialogue, default Follow host |
| Dialogue autosave | Native checkpoint before dialogue interactions and trigger acknowledgement, when the game allows saving | F8 → Dialogue, on |
| Empty loot | Closes a chest, corpse or ground-loot window after the final item transfer completes | F8 → Loot, on |
| Empty container highlights | Hides the highlight of an already opened, empty container | F8 → Loot, on |
| Shared loot | Opens interactive native chest, corpse and ground loot for other players | F8 → Loot, on |
| Teammate panels | Native read-only teammate action, spell and ability panels | F8, on |
| Tooltips | Detailed descriptions without holding Alt | F8, on |
| Target range | Shows distance and reach while targeting spells or attacks | F7 |
| Camera zoom | Extends maximum zoom-out distance | F8 → Camera, default ×1.4 |
| Auto-follow | Follows a teammate around locations, including shared host waypoints | On-screen button / F8 → Transfer |
| Shared travel | Shows the host's travel screen and player cursors | F6 / F8 → Travel, preview off by default |
| Auto-confirmations | Accepts fast-travel votes and continues ready loading screens | F8 → Travel, on |
| Item transfers | Extends character-to-character transfer selection range | F8 → Transfer, on |
| Connection settings | Photon TCP selection and guarded room-join retries | F8 → Network |
| Diagnostics | Logs, verbose mode and diagnostic reports | F9 / F10 |

Speed sliders have a 0.25 step and ×1, ×2 and ×3 presets. The host controls speed and dialogue policy for the room. Guests use the host's settings and a single host-authored vote result. Preferences are remembered between launches.

Version 0.1.24 starts the dialogue preference with **Follow host** once, including upgrades from the previous default. Choices made afterward are remembered. Auto-follow can update its own exploration walk when the host changes destination, discards stale waypoints after pauses, and retries transient errors. It preserves local ownership and manual actions.

Dialogue checkpoints precede a conversation. The game does not restore an individual choice or roll inside an active dialogue. Scripted scenes that forbid saving are skipped. Unopened containers stay highlighted; looting and other interactions remain available when empty highlights are hidden.

In a multiplayer lobby, select all heroes first. The host clicks **Move slot N** on one character card, below the owner dropdown, then **Swap with M** on another. Clicking the selected slot again cancels. No F8 toggle is required. Guests see the same arrangement, and each hero stays with the same player. The order carries into a new or loaded game through native party sorting; subsequent manual in-game reordering remains available.

## Installation

1. Close Solasta on all computers.
2. Download `winhttp.dll` from the [latest GitHub Release](https://github.com/sogoeslight/solasta-coop-mod/releases/latest), or extract it from the Nexus ZIP.
3. Copy `winhttp.dll` into the game folder, next to `Solasta.exe`.
4. Use the same mod version for every co-op player.
5. Launch the game and press **F8**.

## Key bindings

| Key | Action |
| --- | --- |
| F6 | Shared travel preview |
| F7 | Targeting range |
| F8 | Mod settings |
| F9 | Verbose logging |
| F10 | Diagnostic report |

## Licenses

Authored code: [MIT](LICENSE). Modified Unity Doorstop loader: [LGPL-2.1](source/doorstop/LICENSE), with [source changes](source/doorstop/MODIFICATIONS.md). Harmony: [license and provenance](source/vendor/harmony/UPSTREAM.txt). Game assemblies are not distributed. This is an unofficial community mod, not affiliated with Tactical Adventures.
