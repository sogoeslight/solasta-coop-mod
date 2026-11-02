# Solasta Coop View

**Experimental quality-of-life mod for [Solasta: Crown of the Magister](https://store.steampowered.com/app/1096530/Solasta_Crown_of_the_Magister/) co-op**, focused on character observation, travel, inventory interaction, camera controls and diagnostics.

**Current version: `0.1.19` (experimental)** | Windows x64 | DLL built by GitHub Actions

> [!WARNING]
> Community experiment, **not affiliated with Tactical Adventures**. Features involving multiplayer commands, remote loot, auto-follow and combat animation timing are not verified in a live two-player test for this source snapshot. Back up the host save, use the same version on all players, and disable experimental features if the game desynchronizes.

## Table of contents

- [Features](#features)
- [Key bindings](#key-bindings)
- [Installation](#installation)
- [Build from source (Windows)](#build-from-source-windows)
- [Testing and troubleshooting](#testing-and-troubleshooting)
- [Repository layout](#repository-layout)
- [Source provenance and licenses](#source-provenance-and-licenses)

## Features

| Area | Capability | Default / control | Important limitations |
| --- | --- | --- | --- |
| Co-op spectator | Read-only native teammate action, spell and ability panels, including warlock selectors | Enabled, F8 | Viewing must not execute teammate actions; disabled defensively after character-mapping errors |
| Tooltips | Detailed ability/item descriptions without holding Alt | Enabled, F8 | Uses game UI hooks |
| Target range | Distance and reach while targeting spells or attacks | F7 | Visual aid, uses native game range evaluators |
| Camera | Maximum camera zoom-out multiplier | **×1.4**, adjustable ×1–×3 in F8 | Camera control, not character movement |
| World exploration | Locally controlled heroes follow a teammate's moves, including host waypoint messages and positional fallback | On-screen toggle / F8 | Movement is pathfinding-dependent; no control during combat/dialogues |
| Travel | View host's travel display, approximate 97% × 95% screen area, synchronized UI preview | F6, F8 | Visual sharing, not desktop control |
| Fast travel | Automatically accept **fast-travel votes only** | Enabled, F8 → Travel | Does not accept general host waiting or other voting dialogs |
| Post-loading confirmation | Automatically press Continue on the **native LoadingModal** after a save or location loads | Enabled, F8 → Travel; 0.65-second ready delay | No synthetic keypresses; only when the native Continue button is interactive; does not accept dialogs or other votes |
| Loot | Native chest/corpse/ground loot UI opens for the other participant; interactive looting using locally controlled heroes | Enabled, F8 → Loot | **Experimental:** UI-distance bypass does not guarantee server acceptance; duplication/race/desync possible |
| Item transfer | Increase character-to-character transfer recipient range | Enabled, F8 → Transfer | Server-side range validation may still apply |
| Combat animation | Visual Animator, legacy Animation and particle playback speed | **×2** default, F8 → Combat (×1–×3 by 0.25) | Does not alter real movement speed, turn logic or Photon timers; animation events may affect gameplay |
| Network | Opt-in Photon TCP choice and controlled retries of **JoinRoom**, with explicit acknowledgement | F8 → Network | Does not rerun CreateRoom; cannot replace network-level DPI tools |
| Troubleshooting | Connection/turn diagnostics, logs and diagnostic report | F9 verbose, F10 report | Logs do not automatically repair desync |

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

## Build from source (Windows)

The repository deliberately does **not** include the game's proprietary assemblies, Unity binaries or a prebuilt `winhttp.dll`.

Requirements:

- Installed, supported copy of **Solasta: Crown of the Magister**, with `Solasta_Data/Managed` available.
- **Python 3**, **.NET SDK 8**, and **MSYS2 MinGW-w64 x86_64 GCC**.
- On Windows, run **PowerShell** in the repository root.

```powershell
winget install --id Python.Python.3.12 -e
winget install --id Microsoft.DotNet.SDK.8 -e
winget install --id MSYS2.MSYS2 -e
```

After installing MSYS2, install the MinGW-w64 compiler from an MSYS2 shell:

```bash
pacman -Syu
pacman -S --needed mingw-w64-x86_64-gcc
```

Run this command, replacing the example path with the **actual** `Managed` folder from your own game installation:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-windows.ps1 -ManagedPath 'D:\SteamLibrary\steamapps\common\Solasta_COTM\Solasta_Data\Managed'
```

Alternatively, set the `SOLASTA_MANAGED` environment variable or use the script's Steam-default discovery. The script accepts `-DotnetPath` and `-GccPath` overrides for custom setups.

Successful output: **`build/winhttp.dll`**. The script must actually produce this file; a `Write-Host` message alone does not indicate success.

## Testing and troubleshooting

**Static source checks** (do not compile or launch the game):

```powershell
py -3 .\source\source_sanity_check.py
py -3 -m py_compile .\source\build.py .\source\verify.py
```

`source/verify.py` provides additional Mono-based checks but needs game assemblies and Mono configured separately.

For multiplayer testing, start with a **backup save**, verify the mod starts on both systems, then enable and test **one experimental feature at a time**. In particular:

- **Remote loot:** take a single inexpensive item from the second UI, verify it disappears exactly once and reaches the correct hero on **both computers**. Test corpses and long distance separately.
- **Auto-follow:** test ordinary paths first, then doors, obstacles and scene transitions; do not use in combat.
- **Combat speed:** begin at ×1 and try ×2 once basic co-op turns are stable; if action transitions behave strangely, turn it back to ×1.
- **Desync:** collect diagnostics from **both** clients (F9/F10) and include exact time, map, action, and any native game errors. `Couldn't recover character used in action` can indicate failed multiplayer character mapping.
- **Networking:** Photon `JoinRoom` retry needs manual confirmation that the game error dialog has been dismissed. Repeating `CreateRoom` can produce `32766` (duplicate room name).

See [Troubleshooting guide](docs/TROUBLESHOOTING.md) and [implementation overview](docs/ARCHITECTURE.md).

## Repository layout

```text
source/                      C# gameplay/UI patches and diagnostics
source/doorstop/             Bundled and modified Unity Doorstop source
source/vendor/harmony/       0Harmony library, license and upstream details
source/build.py              C# payload + native Windows proxy build
source/source_sanity_check.py  Static source checks
build-windows.ps1            Windows build entry point
.github/workflows/           Lightweight source validation (no game libraries)
docs/                        Architecture, troubleshooting, publishing notes
```

## Source provenance and licenses

The commit history in this repository is a **reconstruction from archived source snapshots 0.1.10 through 0.1.18**, grouped into feature-oriented commits. These are **not original development timestamps or verified release milestones**. Review older commits as historical source snapshots, not a list of guaranteed working builds.

- Authored C# code and `build.py`: repository [MIT license](LICENSE).
- Unity Doorstop: bundled source with its **separate LGPL-2.1 license** and [`MODIFICATIONS.md`](source/doorstop/MODIFICATIONS.md).
- Harmony: bundled binary with its own license and [upstream provenance](source/vendor/harmony/UPSTREAM.txt).
- Solasta / Unity game DLLs: **not redistributed**. You must supply them from your own installed game for compilation.

**Status:** v0.1.19 experimental release, no guaranteed multiplayer compatibility or official support. Auto-continue is based on the game's native LoadingModal callbacks, and must still be validated in live co-op.
