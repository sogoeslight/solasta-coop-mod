# Solasta Coop Mod v0.1.22

- Changes the initialization used by 0.1.21, which could prevent the game from reaching the main menu. New gameplay hooks now initialize after a location is loaded.
- Reworks pre-dialogue autosaves to intercept the trigger call instead of detouring empty virtual methods.
- Adds detailed initialization logs and a startup regression fixture covering empty native-shaped methods.
- Sets default exploration movement speed to **×1.75** and combat speed to **×1.5**. Existing custom settings are retained.
- Corrects the native loader log version label.

All 0.1.21 features remain available. Replace the previous **winhttp.dll** beside **Solasta.exe** on every player's computer. Settings are under **F8 → Speed / Loot / Dialogue**.

Validation: Windows build, source checks and Mono/API/Harmony fixtures including startup initialization. A live Solasta startup and two-player session must still be checked in the game.
