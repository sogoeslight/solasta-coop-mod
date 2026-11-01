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
    'Loot mirror uses dedicated event channel': 'EventCode = 191' in loot and 'EventCode = 190' in wire,
    'Loot only mirrors pixels, never executes game actions': 'ScreenCapture.CaptureScreenshotAsTexture()' in loot and 'GameLocationActionService' not in loot,
    'Loot validates JPEG bytes': 'TravelWire.Jpeg((byte[])data[3])' in loot,
    'Loot mirror works both host and guest': 'IsMasterClient' not in loot and 'IsMember(packet.Actor)' in loot,
    'Loot manual fallback hotkey': 'KeyCode.F11' in hud,
    'Loot received window dismissible': 'KeyCode.F12' in hud,
    'Loot mirror compiled': 'source / "LootShare.cs"' in (root / 'build.py').read_text(),
    'Safety disables HUD after game actor mapping errors': 'gameCharacterMappingError' in hud,
    'Battle snapshot records active turn identity': 'activeGuid=' in (root / 'RuntimeDiagnostics.cs').read_text(),
}
for name, ok in checks.items():
    print(('PASS' if ok else 'FAIL') + ' ' + name)
if not all(checks.values()):
    raise SystemExit(1)
