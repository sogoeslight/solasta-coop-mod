#!/usr/bin/env python3
"""Source-level invariants only, does not compile or load Unity."""
from pathlib import Path

root = Path(__file__).resolve().parent
retry = (root / 'ConnectionRetry.cs').read_text()
preview = (root / 'ObserverPreview.cs').read_text()
hud = (root / 'CoopView.cs').read_text()
verify_retry = (root / 'VerifyConnectionRetry.cs').read_text()
verify_hud = (root / 'VerifyObserverHud.cs').read_text()
loot = (root / 'LootShare.cs').read_text()
wire = (root / 'TravelWire.cs').read_text()
combat = (root / 'CombatSpeed.cs').read_text()

checks = {
    'CreateRoom bypasses retry capture': 'if (__originalMethod.Name == "CreateRoom")' in retry and 'never retried' in retry,
    'Only JoinRoom is replayed': 'if (request.Name != "JoinRoom")' in retry,
    'Native confirmation required': 'internal static void ConfirmErrorDismissed()' in retry,
    'Tick blocks unacknowledged replay': 'needsAcknowledgement || !acknowledged || NativeModalBlocksInput()' in retry,
    'User-facing acknowledgement button': 'GUILayout.Button("Окно ошибки Photon закрыто, повторить вход")' in hud,
    'Warlock select invocation': 'parameter == "SelectInvocation"' in preview,
    'Warlock pact spells': 'parameter == "SelectPactSpell"' in preview,
    'Read-only cast hook retained': '"PreviewItemClicked"' in preview,
    'HUD enabled by default': 'observerHud.SetEnabled(true)' in hud,
    'No experimental HUD label': 'Штатная панель союзника (просмотр, эксперимент)' not in hud,
    'Host duplicate room fixture': 'CreateRoom never retries' in verify_retry,
    'Retry ack fixture': 'does NOT retry while Photon error is unacknowledged' in verify_retry,
    'Warlock read-only fixture': 'Warlock invocation spell selector stays clickable' in verify_hud,
    'Native loot event channel': 'EventCode = 191' in loot and 'EventCode = 190' in wire,
    'Native UI calls are present': 'ShowContainerLoot' in loot and 'ShowProximityLoot' in loot,
    'No JPEG loot streaming': 'CaptureScreenshotAsTexture' not in loot and 'EncodeToJPG' not in loot,
    'F11 removed completely': 'KeyCode.F11' not in hud,
    'Travel nearly full screen': 'UnityEngine.Screen.width * 0.015f' in (root / 'TravelShare.cs').read_text() and 'UnityEngine.Screen.height * 0.025f' in (root / 'TravelShare.cs').read_text(),
    'Restore loot slot state': 'Restore loot slot state' in loot and 'ShouldDisableSlots' in loot,
    'Ignore out of order loot packets': 'seq <= lastSequence' in loot,
    'Deferred native loot open': 'pendingOpen' in loot and 'nextOpenAttempt' in loot,
    'Native UI is enabled at startup': 'lootShare.SetEnabled(true)' in hud,
    'Loot interaction restricted to local hero': 'TryToLootSlotBox' in loot and 'GuardLootAction' in loot and 'IsLocallyControlled(chosen)' in loot,
    'Loot opens with local character': 'ShowContainerLoot", localHero, container' in loot and 'ShowProximityLoot", localHero' in loot,
    'Remote chest UI range removed locally': 'IgnoreRemoteDistance' in loot and 'get_ComputeHeroDistanceRestriction' in loot,
    'Remote ground loot uses opener position': 'SubstituteProximitySource' in loot and 'CharacterSelectionChanged' in loot,
    'No gameplay RPC from loot share': 'LootItemFromContainer' not in loot and 'LootItemAtPositionByCharacter' not in loot,
    'Native loot source compiled': 'source / "LootShare.cs"' in (root / 'build.py').read_text(),
    'Safety disables HUD after game actor mapping errors': 'gameCharacterMappingError' in hud,
    'Battle snapshot records active turn identity': 'activeGuid=' in (root / 'RuntimeDiagnostics.cs').read_text(),
    'Combat speed x2 default': 'Factor { get; private set; } = 2f' in combat,
    'Combat speed restricts to battles': 'if (!inBattle || Factor <= 1f)' in combat,
    'Combat speed does not change game timescale': 'Time.timeScale =' not in combat and 'Time.fixedDeltaTime =' not in combat,
    'Combat animation and VFX acceleration': 'AccelerateAnimators();' in combat and 'AccelerateLegacyAnimations();' in combat and 'AccelerateParticles();' in combat,
    'Combat speed restores on exit': 'CombatSpeed.RestoreAll();' in hud,
    'Combat speed in build': 'source / "CombatSpeed.cs"' in (root / 'build.py').read_text(),
    'Combat speed slider visible': 'GUILayout.HorizontalSlider(CombatSpeed.Factor, 1f, 3f)' in hud,
    'Unity animation dependencies': 'UnityEngine.AnimationModule' in (root / 'build.py').read_text() and 'UnityEngine.ParticleSystemModule' in (root / 'build.py').read_text(),
}
for name, ok in checks.items():
    print(('PASS' if ok else 'FAIL') + ' ' + name)
if not all(checks.values()):
    raise SystemExit(1)
