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
