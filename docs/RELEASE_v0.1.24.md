# Solasta Coop Mod v0.1.24

- **F6 preview off by default.** The shared travel preview starts hidden; press F6 to request and display the host's screen.
- **Follow host dialogue default.** The first launch of this preference generation selects Follow host, including upgrades from the old default. Later choices are saved. Guests use the host's room policy.
- **Repeated auto-follow movement.** A follower's own active exploration walk can receive a new target instead of being blocked by its busy flag. Native replacement commands respect a cooldown, local ownership, manual movement and non-movement actions.
- **Follow recovery.** Stale waypoints expire after a stopped or cancelled host route and are cleared for dialogue, combat and location changes. Elevation differences are respected; transient command failures retry without disabling follow.
- **Follow diagnostics.** F10 reports and periodic verbose logs include current follow status, waypoints, positions, blocked commands, recovery count and host-movement channel state.

Replace **winhttp.dll** beside **Solasta.exe** on every player's computer. Exploration speed remains **×1.75**, combat speed **×1.5**. Existing speed preferences and the 0.1.23 loot initialization fix are retained.

Validation: managed compilation, source checks and 16 Mono/API/Harmony fixture suites. The old 0.1.23 follower fails the repeated-waypoint regression; the replacement passes timed host/guest scenarios, real adapter callbacks, manual input, interactions, pauses, ownership and packet validation. Live Unity pathfinding and a two-player game session remain untested here.
