# Solasta Coop Mod v0.1.27

- **Lobby buttons now resolve the correct panel.** The previous implementation requested NewAdventurePanel as a standalone screen, but Solasta stores it inside MainMenuScreen. The fixed path covers both new-adventure and save-loading lobbies.
- **Visible card controls.** After all heroes are selected, the host clicks **Move slot N** below one card's owner dropdown, then **Swap with M** on another. No F8 toggle is required. Hero ownership and shared order carryover remain unchanged.
- **Faster host following.** Received host destinations wake the follower immediately. Replacing its own automatic move is bounded to 0.25 seconds instead of 2.5 seconds; manual movement remains protected and identical targets do not restart walking.
- **Less exploration work.** Walking animation updates use cached world characters instead of scanning every animator in the scene five times per second. Empty-container inventory checks are cached, with per-frame updates restricted to eligible highlights. Lobby checks stop rescanning the party after application or when no policy exists.

Replace **winhttp.dll** beside **Solasta.exe** on every player's computer. Defaults remain F6 preview off, dialogue Follow host, exploration **×1.75**, combat **×1.5**.

Validation: managed compilation, source checks and 18 Mono/API/Harmony fixture suites. The old 0.1.26 build fails both the nested-lobby discovery and faster-response regressions. The fix passes the real GuiManager/MainMenuScreen lookup, new/load lobby fixtures, response timing, manual-input guards, native highlight behavior and scan-avoidance checks. Live UI rendering and the host's FPS change remain unmeasured here.
