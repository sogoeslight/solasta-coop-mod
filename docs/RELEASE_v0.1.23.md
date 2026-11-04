# Solasta Coop Mod v0.1.23

- Fixes a reproduced native Mono crash when empty-loot initialization runs after existing shared-loot hooks. Removes its additional Harmony detours, the stage where the supplied 0.1.22 log stopped during save loading.
- Preserves automatic closing of empty chests, corpses and ground-loot windows by observing native UI state. Pending transfers and the opening grace period are respected; each window closes through its native cancel handler.
- Preserves filtering of already opened empty-container highlights through the original visibility method after gameplay updates. Unopened containers keep their highlights; refilled containers and disabling the option restore native visibility.
- Keeps the requested default exploration speed **×1.75** and combat speed **×1.5**, along with saved custom preferences.

Replace **winhttp.dll** beside **Solasta.exe** on every player's computer. Settings remain under **F8 → Speed / Loot / Dialogue**.

Validation: source checks, managed compilation and 15 Mono/API/Harmony fixture suites, including the real Solasta 1.5.97 highlight method, existing shared-loot generic callbacks and isolated loot-window behavior. The old installer aborts in that combined fixture; the replacement passes. Live save loading, Unity rendering and multiplayer still require in-game verification.
