# Solasta Coop Mod v0.1.26

- **Swap lobby hero positions.** Once all heroes are selected, the host clicks **Move slot N** above one character card, then **Swap with M** above another. Clicking the selected source cancels.
- **Shared arrangement, unchanged owners.** Guests and later arrivals see the chosen arrangement. Each hero stays under the same player's control; native snapshots and slot/GUID mappings remain intact.
- **Carryover into the game.** New and loaded sessions apply the arrangement through native party sorting. Stable saved hero identities work even when clients display differently filtered names. Subsequent manual in-game ordering is respected.
- **Guarded editing.** Controls wait during modal input, launch, pending writes and room changes. F10 reports include lobby-order status.

This includes the lobby-order feature introduced in 0.1.25 and its filtered-name compatibility correction. Replace **winhttp.dll** beside **Solasta.exe** on every player's computer.

Existing defaults remain: F6 preview off, dialogue Follow host, exploration **×1.75**, combat **×1.5**.

Validation: source checks, managed compilation and all 18 Mono/API/Harmony fixture suites, including every four-hero permutation, host/guest publishing, ownership retention and the real Solasta 1.5.97 party-swap and display-name implementations. Live lobby rendering and a two-player session remain untested here.
