# Solasta Coop Mod v0.1.21

- Accelerates actual combat movement and its baked movement waits, alongside animations and VFX.
- Adds a separate exploration movement slider. Both sliders cover ×1–×3 in 0.25 steps, with ×1, ×2 and ×3 presets and ×2 defaults.
- Synchronizes speed and dialogue policy from the host. Settings persist between launches.
- Closes empty chest, corpse and ground-loot windows after pending transfers finish.
- Adds a toggle to hide highlights on opened empty containers, while keeping unopened containers visible.
- Adds native autosaves before dialogue interactions and scripted trigger acknowledgement, when the game allows saving. Active dialogue choices and rolls are not separately restorable.
- Adds Vanilla, Follow host and Random tie dialogue modes. Random tie uses equal player votes and one host-authored result; two different votes have a 50/50 chance. Follow host needs no guest click and waits for the host's answer.
- Fixes switching between Warlock invocation and pact spell preview selectors.

Install the single **winhttp.dll** beside **Solasta.exe** on every player's computer, then open **F8 → Speed / Loot / Dialogue**. All players need version **0.1.21**.

Validation: managed compilation, native API/IL checks, vote-policy regression tests and Harmony fixtures. Live two-player gameplay is not available in the build environment.
