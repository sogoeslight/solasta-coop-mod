#!/usr/bin/env python3
"""Source-level invariants only, does not compile or load Unity."""
from pathlib import Path

root = Path(__file__).resolve().parent
retry = (root / 'ConnectionRetry.cs').read_text(encoding="utf-8")
preview = (root / 'ObserverPreview.cs').read_text(encoding="utf-8")
hud = (root / 'CoopView.cs').read_text(encoding="utf-8")
verify_retry = (root / 'VerifyConnectionRetry.cs').read_text(encoding="utf-8")
verify_hud = (root / 'VerifyObserverHud.cs').read_text(encoding="utf-8")
loot = (root / 'LootShare.cs').read_text(encoding="utf-8")
wire = (root / 'TravelWire.cs').read_text(encoding="utf-8")
combat = (root / 'CombatSpeed.cs').read_text(encoding="utf-8")
loading = (root / 'AutoLoadingProceed.cs').read_text(encoding="utf-8")

checks = {
    'Loading confirmation targets native modal only': 'Read.Type("LoadingModal")' in loading and
        '"OnProceedToLocationCb"' in loading and '"OnUpdateHook"' in loading,
    'Loading confirmation checks readiness': '"ReadyToProceed"' in loading and
        '"IsInteractable"' in loading and 'ReadyDelaySeconds' in loading,
    'Loading confirmation F8 setting': 'GUILayout.Toggle(autoLoadingProceed.Enabled' in hud,
    'Loading confirmation compiled': 'source / "AutoLoadingProceed.cs"' in (root / 'build.py').read_text(encoding="utf-8"),
    'CreateRoom bypasses retry capture': 'if (__originalMethod.Name == "CreateRoom")' in retry and 'never retried' in retry,
    'Only JoinRoom is replayed': 'if (request.Name != "JoinRoom")' in retry,
    'Native confirmation required': 'internal static void ConfirmErrorDismissed()' in retry,
    'Tick blocks unacknowledged replay': 'needsAcknowledgement || !acknowledged || NativeModalBlocksInput()' in retry,
    'User-facing acknowledgement button': 'GUILayout.Button("Photon error dialog closed, retry joining")' in hud,
    'Warlock select invocation': 'parameter == "SelectInvocation"' in preview,
    'Warlock pact spells': 'parameter == "SelectPactSpell"' in preview,
    'Read-only cast hook retained': '"PreviewItemClicked"' in preview,
    'HUD enabled by default': 'observerHud.SetEnabled(true)' in hud,
    'No experimental HUD label': 'Native teammate panel (experimental)' not in hud,
    'Host duplicate room fixture': 'CreateRoom never retries' in verify_retry,
    'Retry ack fixture': 'does NOT retry while Photon error is unacknowledged' in verify_retry,
    'Warlock read-only fixture': 'Warlock invocation spell selector stays clickable' in verify_hud,
    'Native loot event channel': 'EventCode = 191' in loot and 'EventCode = 190' in wire,
    'Native UI calls are present': 'ShowContainerLoot' in loot and 'ShowProximityLoot' in loot,
    'No JPEG loot streaming': 'CaptureScreenshotAsTexture' not in loot and 'EncodeToJPG' not in loot,
    'F11 removed completely': 'KeyCode.F11' not in hud,
    'Travel nearly full screen': 'UnityEngine.Screen.width * 0.015f' in (root / 'TravelShare.cs').read_text(encoding="utf-8") and 'UnityEngine.Screen.height * 0.025f' in (root / 'TravelShare.cs').read_text(encoding="utf-8"),
    'Restore loot slot state': 'Restore loot slot state' in loot and 'ShouldDisableSlots' in loot,
    'Ignore out of order loot packets': 'seq <= lastSequence' in loot,
    'Deferred native loot open': 'pendingOpen' in loot and 'nextOpenAttempt' in loot,
    'Native UI is enabled at startup': 'lootShare.SetEnabled(true)' in hud,
    'Loot interaction restricted to local hero': 'TryToLootSlotBox' in loot and 'GuardLootAction' in loot and 'IsLocallyControlled(chosen)' in loot,
    'Loot opens with local character': 'ShowContainerLoot", localHero, container' in loot and 'ShowProximityLoot", localHero' in loot,
    'Remote chest UI range removed locally': 'IgnoreRemoteDistance' in loot and 'get_ComputeHeroDistanceRestriction' in loot,
    'Remote ground loot uses opener position': 'SubstituteProximitySource' in loot and 'CharacterSelectionChanged' in loot,
    'No gameplay RPC from loot share': 'LootItemFromContainer' not in loot and 'LootItemAtPositionByCharacter' not in loot,
    'Native loot source compiled': 'source / "LootShare.cs"' in (root / 'build.py').read_text(encoding="utf-8"),
    'Safety disables HUD after game actor mapping errors': 'gameCharacterMappingError' in hud,
    'Battle snapshot records active turn identity': 'activeGuid=' in (root / 'RuntimeDiagnostics.cs').read_text(encoding="utf-8"),
    'Requested speed defaults': 'DefaultCombatFactor = 1.5f' in combat and 'DefaultExplorationFactor = 1.75f' in combat,
    'Separate native combat and exploration movement': 'CombatSpeed.ExplorationFactor' in (root / 'MovementSpeed.cs').read_text(encoding='utf-8') and 'GetForwardedAdjustedTime' in (root / 'MovementSpeed.cs').read_text(encoding='utf-8'),
    'Combat speed does not change game timescale': 'Time.timeScale =' not in combat and 'Time.fixedDeltaTime =' not in combat,
    'Combat animation and VFX acceleration': 'AccelerateAnimators();' in combat and 'AccelerateLegacyAnimations();' in combat and 'AccelerateParticles();' in combat,
    'Combat speed restores on exit': 'CombatSpeed.RestoreAll();' in hud,
    'Combat speed in build': 'source / "CombatSpeed.cs"' in (root / 'build.py').read_text(encoding="utf-8"),
    'Speed sliders include vanilla x1': 'GUILayout.HorizontalSlider(current, 1f, 3f)' in hud,
    'All optional features have toggles': all(value in hud for value in [
        'GUILayout.Toggle(follow,', 'GUILayout.Toggle(observerHud.Enabled,',
        'GUILayout.Toggle(DetailedTooltips.Enabled,', 'GUILayout.Toggle(showHoverRange,',
        'GUILayout.Toggle(tcp,', 'GUILayout.Toggle(ConnectionRetry.Enabled,',
        'GUILayout.Toggle(travelShare.Enabled,', 'GUILayout.Toggle(autoFastTravelVote.Enabled,',
        'GUILayout.Toggle(autoLoadingProceed.Enabled,', 'GUILayout.Toggle(lootShare.Enabled,',
        'GUILayout.Toggle(Log.Verbose,', 'GUILayout.Toggle(ItemTransferRange.Enabled,',
        'GUILayout.Toggle(autoFollow.Enabled,', 'GUILayout.Toggle(hostMoveShare.Enabled,',
        'GUILayout.Toggle(LootConvenience.AutoClose,', 'GUILayout.Toggle(LootConvenience.HideEmpty,',
        'GUILayout.Toggle(DialogueAutosave.Enabled,', 'GUILayout.Toggle(zoomEnabled,'
    ]),
    'No experimental warnings in overlay': 'experimental' not in hud.lower(),
    'Renamed overlay': '"Solasta Coop Mod "' in hud,
    'Managed output renamed': 'SolastaCoopMod.dll' in (root / 'build.py').read_text(encoding="utf-8"),
    'Unity animation dependencies': 'UnityEngine.AnimationModule' in (root / 'build.py').read_text(encoding="utf-8") and 'UnityEngine.ParticleSystemModule' in (root / 'build.py').read_text(encoding="utf-8"),
    'English-only mod source': not any(any('\u0400' <= c <= '\u04ff' for c in p.read_text(encoding='utf-8')) for p in root.glob('*.cs')),
}
for name, ok in checks.items():
    print(('PASS' if ok else 'FAIL') + ' ' + name)
if not all(checks.values()):
    raise SystemExit(1)
