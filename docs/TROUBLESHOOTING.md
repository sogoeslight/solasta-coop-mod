# Troubleshooting

Solasta Coop View is an experimental mod. Features have not been validated in a full two-player test on this source snapshot. Before troubleshooting, back up the host's save and confirm both players use the same build.

## Mod does not start

- Verify `winhttp.dll` was built successfully and placed alongside `Solasta.exe`, not in `System32` or `Solasta_Data/Managed`.
- Make sure both the managed C# payload and native proxy were produced by the same `build-windows.ps1` run.
- Examine logs under `Downloads/SolastaCoopView`, and report the first startup exception.

## Connection or room join fails

- The TCP option applies to the next connection, not an already established connection.
- Retry only `JoinRoom` after acknowledging and dismissing the game error. Repeating `CreateRoom` can cause `GameIdAlreadyExists` (32766).
- Gather matching network logs from host and client. Note whether the failure occurred at Master Server connection, Game Server authentication, or room join.

## Native shared loot UI does not open

- Confirm the same mod build is installed on both players' PCs and shared loot is enabled in F8.
- Test a simple chest before corpses or ground loot. Record whether the container was discovered, whether the modal opened, and the retry/status message.
- If looting succeeds, verify the item moved exactly once and appears on the correct character on both screens.
- Distance bypasses in the UI do not necessarily bypass server validations. Avoid valuable items until tested.

## Auto-follow misses movements

- Use auto-follow during exploration, not combat, dialogues or loading transitions.
- First test a clear, walkable area without large height changes. The host's waypoint and the second player's local command must both be valid.
- Auto-follow does not teleport characters and cannot guarantee pathfinding around locked doors or unusual terrain.

## Combat timing or turns desynchronize

- Disable experimental features individually, starting with combat speed (set x1), remote looting and auto-follow.
- Preserve native game turn sequencing and network timers.
- The error `Couldn't recover character used in action` should be investigated through per-player actor/turn logs rather than bypassing authoritative commands.

## Useful information when reporting an issue

Provide mod version, game version, whether the computer is host or client, reproduction steps, UTC/local timestamp, relevant logs from both players, and which experimental settings were enabled. Remove account identifiers and secrets from logs before sharing.
