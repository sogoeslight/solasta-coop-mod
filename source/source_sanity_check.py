#!/usr/bin/env python3
"""Source-level invariants only, does not compile or load Unity."""
from pathlib import Path

root = Path(__file__).resolve().parent
retry = (root / 'ConnectionRetry.cs').read_text()
preview = (root / 'ObserverPreview.cs').read_text()
hud = (root / 'CoopView.cs').read_text()
verify_retry = (root / 'VerifyConnectionRetry.cs').read_text()
verify_hud = (root / 'VerifyObserverHud.cs').read_text()

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
}
for name, ok in checks.items():
    print(('PASS' if ok else 'FAIL') + ' ' + name)
if not all(checks.values()):
    raise SystemExit(1)
