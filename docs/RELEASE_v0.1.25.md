# Solasta Coop Mod v0.1.25

- **Lobby party order.** After all heroes are selected, the host clicks **Move slot N** above a character card, then **Swap with M** above another to swap positions. Clicking the selected source cancels.
- **Shared arrangement, same owners.** Guests and later arrivals see the chosen arrangement. Native character bindings, player ownership, character snapshots and slot/GUID mappings remain intact.
- **Carryover into the game.** New and loaded games apply the arrangement through Solasta's native party-swap method. Subsequent manual in-game reordering is respected.
- **Lobby safeguards.** Edits are blocked during modal dialogs, launch, pending updates and room changes. The original card layout is restored when leaving the lobby or changing its roster.
- **Diagnostics.** F10 reports include lobby party-order status.

Replace **winhttp.dll** beside **Solasta.exe** on every player's computer. F6 preview remains off by default, dialogue defaults to Follow host, exploration speed to **×1.75** and combat speed to **×1.5**.

Validation: source checks, managed compilation and 18 Mono/API/Harmony fixture suites. Lobby checks cover all 24 four-hero permutations, host/guest publishing, GUID and ownership retention, stale data, pending writes and the real Solasta 1.5.97 party-swap method. The live lobby layout and a two-player session remain untested here.
