# Source checks, 0.1.10

These checks inspect updated source only. They do not compile a C# assembly or load Unity/Solasta. The 0.1.9 binary was deliberately excluded from the update package.

Test intentions in `VerifyConnectionRetry.cs`:
- Host `CreateRoom` times out, and no second creation request occurs.
- Photon code `32766` is handled by the native rejected callback.
- Join retry never happens before explicit user acknowledgement.
- After acknowledgement and a safe peer state, one native JoinRoom call replays and the original menu callback is restored.
- Each further timeout again requires acknowledgement.
- Cancelling, connecting successfully or disabling retry stops pending work.

Test intentions in `VerifyObserverHud.cs`:
- Warlock `SelectInvocation` and `SelectPactSpell` categories are browsable.
- Native spell cast callbacks are inert in borrowed teammate HUD.
- Unrelated combat categories remain disabled.

The host's TCP trace demonstrates successful connection/authentication to the GameServer followed by a joining timeout and then Photon `32766` on duplicate CreateRoom. It does not establish the upstream packet filtering mechanism.


## Version 0.1.21 checks

`verify.py` includes native API checks and real Harmony dialogue fixtures, plus pure vote-policy cases. Pass `--cecil /path/to/Mono.Cecil.dll` (a Mono-compatible build) to include movement fixtures. The harness creates an isolated copy of Unity CoreModule with deterministic time getters under the ignored build directory; game references and distributed binaries are never modified. Movement checks measure completion frames at x1/x2/x3, destination preservation, baked waits, walking blend compensation, independent exploration speed and cinematic exclusion.

No fixture replaces a live host/client gameplay test.


## Version 0.1.22 startup regression

With `--cecil`, the harness also runs `VerifyStartup`: no new gameplay detours in the Bootstrap/menu phase, all four gameplay installers after location load, a genuinely empty virtual acknowledgement left unpatched and callable, save-before-acknowledgement ordering, native save restrictions, and the x1.5/x1.75 defaults. The Unity fixture changes time/preference access only in an ignored test copy. The original 0.1.21 acknowledgement installer reproduces a native Mono crash in the isolated fixture; the call-site implementation passes.
