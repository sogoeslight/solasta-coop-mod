# Solasta Coop Mod

**Experimental quality-of-life mod for [Solasta: Crown of the Magister](https://store.steampowered.com/app/1096530/Solasta_Crown_of_the_Magister/) co-op**, focused on character observation, travel, inventory interaction, camera controls and diagnostics.

**Current version: `0.1.20` (experimental)** | Windows x64 | DLL built by GitHub Actions

> [!WARNING]
> Community experiment, **not affiliated with Tactical Adventures**. Features involving multiplayer commands, remote loot, auto-follow and combat animation timing are not verified in a live two-player test for this source snapshot. Back up the host save, use the same version on all players, and disable experimental features if the game desynchronizes.

## Table of contents

- [Features](#features)
- [Key bindings](#key-bindings)
- [Installation](#installation)
- [Licenses](#licenses)

## Features

| Area | Capability | Default / control |
| --- | --- | --- |
| Co-op spectator | Read-only native teammate action, spell and ability panels | Enabled, F8 |
| Tooltips | Detailed ability/item descriptions without holding Alt | Enabled, F8 |
| Target range | Distance and reach while targeting spells or attacks | F7 |
| Camera | Maximum camera zoom-out multiplier | **×1.4**, adjustable ×1–×3 in F8 |
| World exploration | Locally controlled heroes follow a teammate's moves, including host waypoint messages and positional fallback | On-screen toggle / F8 |
| Travel | View host's travel display, approximate 97% × 95% screen area, synchronized UI preview | F6, F8 |
| Fast travel | Automatically accept **fast-travel votes only** | Enabled, F8 → Travel |
| Post-loading confirmation | Automatically press Continue on the **native LoadingModal** after a save or location loads | Enabled, F8 → Travel; 0.65-second ready delay |
| Loot | Native chest/corpse/ground loot UI opens for the other participant; interactive looting using locally controlled heroes | Enabled, F8 → Loot |
| Item transfer | Increase character-to-character transfer recipient range | Enabled, F8 → Transfer |
| Combat animation | Visual Animator, legacy Animation and particle playback speed | **×2** default, F8 → Combat (×1–×3 by 0.25) |
| Network | Opt-in Photon TCP choice and controlled retries of **JoinRoom**, with explicit acknowledgement | F8 → Network |
| Troubleshooting | Connection/turn diagnostics, logs and diagnostic report | F9 verbose, F10 report |

## Key bindings

| Key | Action |
| --- | --- |
| **F6** | Show/hide shared travel display |
| **F7** | Toggle target range tooltip |
| **F8** | Open configuration overlay |
| **F9** | Toggle verbose diagnostics |
| **F10** | Write diagnostic report |
| **F11** | **Disabled** (old screenshot-loot stream was removed after instability) |

## Installation

1. Close Solasta on both computers.
2. Download `winhttp.dll` from the [latest GitHub Release](https://github.com/sogoeslight/solasta-coop-mod/releases/latest).
3. Copy `winhttp.dll` into the Solasta installation folder, next to `Solasta.exe`.
4. Install the same mod version for **all co-op players** and back up the host save before testing.
5. Launch the game and press **F8** to configure the mod.

**Do not replace Windows' system `winhttp.dll`.** The mod uses a local proxy DLL placed only in the game directory.

## Licenses

- Authored C# code and `build.py`: repository [MIT license](LICENSE).
- Unity Doorstop: bundled source with its **separate LGPL-2.1 license** and [`MODIFICATIONS.md`](source/doorstop/MODIFICATIONS.md).
- Harmony: bundled binary with its own license and [upstream provenance](source/vendor/harmony/UPSTREAM.txt).
- Solasta / Unity game DLLs: **not redistributed**.
