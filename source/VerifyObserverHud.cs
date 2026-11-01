using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

public sealed class IGameLocationCharacterService { public readonly List<object> PartyCharacters = new List<object>(); }
public sealed class BattleState_TurnReady { }
public sealed class FakeMachine { public object CurrentState = new BattleState_TurnReady(); }
public sealed class FakeBattle { public object ActiveContender; public readonly FakeMachine FiniteStateMachine = new FakeMachine(); }
public sealed class FakeController
{
    public object LocalCharacter;
    public bool IsCharacterControlled(object character) { return ReferenceEquals(LocalCharacter, character); }
}
public sealed class FakeGuiService
{
    public bool GrabbedCameraControls;
    public readonly GameLocationScreenBattle Screen = new GameLocationScreenBattle();
    public object GetScreen(Type type) { return type == typeof(GameLocationScreenBattle) ? Screen : null; }
}
public static class Gui
{
    public static readonly FakeBattle Battle = new FakeBattle();
    private static readonly FakeController controller = new FakeController();
    public static bool ThrowWithoutBattle;
    public static int ControllerReads;
    public static FakeController ActivePlayerController
    {
        get
        {
            ControllerReads++;
            if (ThrowWithoutBattle && Battle.ActiveContender == null) throw new NullReferenceException("IPlayerControllerService is absent in menu");
            return controller;
        }
    }
    public static readonly FakeGuiService GuiService = new FakeGuiService();
}
public sealed class GuiManager
{
    public bool HeldAlt;
    public bool IsShowingDetailedInformation { [MethodImpl(MethodImplOptions.NoInlining)] get { return HeldAlt; } }
}
public sealed class FakeCanvas { public bool interactable = true, blocksRaycasts = false; }
public sealed class FakeTransform
{
    public object Parent;
    public bool IsChildOf(object parent) { return ReferenceEquals(Parent, parent); }
}
public sealed class FakeGuiCharacter
{
    public object GameLocationCharacter;
    public object RulesetCharacter { get { return ((FakeCharacter)GameLocationCharacter).RulesetCharacter; } }
}
public enum FakeActionType { Main, Bonus }
public sealed class GuiCharacterAction { public FakeDefinition ActionDefinition; }
public sealed class FakeDefinition { public string Parameter; }
public sealed class CharacterActionItemForm
{
    public GuiCharacterAction GuiCharacterAction;
    public FakeCanvas canvasGroup = new FakeCanvas();
    public FakeCanvas Button = new FakeCanvas();
    public FakeTransform transform;
    [MethodImpl(MethodImplOptions.NoInlining)] public void Refresh() { canvasGroup.interactable = false; Button.interactable = false; }
}
public sealed class SpellSelectionPanel
{
    public FakeActionType ActionType { get; set; }
    public FakeGuiCharacter Caster;
    public Action SpellcastCancelled;
    public Action<object, object, int> Cast;
    public bool Visible, CantripOnly;
    public int Binds, Hides;
    [MethodImpl(MethodImplOptions.NoInlining)] public void Bind(FakeGuiCharacter caster, Action cancel, Action<object, object, int> cast, FakeActionType type, bool cantripOnly)
    { Binds++; Caster = caster; SpellcastCancelled = cancel; Cast = cast; ActionType = type; CantripOnly = cantripOnly; }
    public void Show(bool instant) { Visible = true; }
    public void Hide(bool instant) { Hides++; Visible = false; Caster = null; SpellcastCancelled = null; Cast = null; }
}
public sealed class PowerSelectionPanel
{
    public FakeActionType ActionType { get; set; }
    public Action PowerCancelled;
    public Action<object> Engage;
    public object Character;
    public bool Visible;
    [MethodImpl(MethodImplOptions.NoInlining)] public void Bind(object character, Action cancel, Action<object> engage)
    { Character = character; PowerCancelled = cancel; Engage = engage; }
    public void Show(bool instant) { Visible = true; }
    public void Hide(bool instant) { Visible = false; PowerCancelled = null; Engage = null; }
}
public class CharacterControlPanel
{
    public int Inputs;
    [MethodImpl(MethodImplOptions.NoInlining)] public virtual bool HandleInput(int command) { Inputs++; return true; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnInspectCb() { Inputs++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void RestoreDefaultCursor() { Inputs++; }
}
public sealed class CharacterControlPanelBattle : CharacterControlPanel
{
    public bool Visible, Loaded = true, Hiding;
    public FakeCanvas CanvasGroup = new FakeCanvas();
    public FakeGuiCharacter GuiCharacter;
    public readonly List<object> ActionPanels = new List<object> { new CharacterActionPanel(), new CharacterActionPanel { ActionType = FakeActionType.Bonus } };
    public readonly SpellSelectionPanel SpellSelectionPanel = new SpellSelectionPanel();
    public readonly PowerSelectionPanel PowerSelectionPanel = new PowerSelectionPanel();
    public readonly object TooltipBlocker = new object();
    public readonly FakeTransform transform = new FakeTransform();
    public int Binds, Hides, Commands, Refreshes;
    [MethodImpl(MethodImplOptions.NoInlining)] public override bool HandleInput(int command) { Inputs++; return true; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnEndTurnCb() { Commands++; }
    [MethodImpl(MethodImplOptions.NoInlining)] private void OnUpdateHook() { CanvasGroup.interactable = true; Commands++; }
    public void RunFrame() { OnUpdateHook(); }
    public void Bind(object character, object anchor)
    {
        Binds++; GuiCharacter = new FakeGuiCharacter { GameLocationCharacter = character };
        foreach (CharacterActionPanel action in ActionPanels)
        { action.SpellSelectionPanel = SpellSelectionPanel; action.PowerSelectionPanel = PowerSelectionPanel; }
    }
    public void Show(bool instant) { Visible = true; CanvasGroup.interactable = true; }
    public void Hide(bool instant)
    { Hides++; SpellSelectionPanel.Hide(true); PowerSelectionPanel.Hide(true); Visible = false; GuiCharacter = null; }
    public void RefreshNow() { Refreshes++; }
}
public sealed class CharacterActionPanel
{
    public int Commands;
    public FakeActionType ActionType;
    public SpellSelectionPanel SpellSelectionPanel;
    public PowerSelectionPanel PowerSelectionPanel;
    [MethodImpl(MethodImplOptions.NoInlining)] public bool HandleInput(int command) { Commands++; return true; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnActivateAction(object item) { Commands++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnActivateAction(int action, object item) { Commands++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void RestoreDefaultCursor() { Commands++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnPointerEnter(object item) { Commands++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnPointerExit(object item) { Commands++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void SpellcastEngaged(object repertoire, object spell, int level) { Commands++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void SpellCastConfirmed() { Commands++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void PowerEngaged(object power) { Commands++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void ExecuteEffectOfAction() { Commands++; }
}
public sealed class WieldedConfigurationSelector
{
    public FakeTransform transform;
    public int Commands;
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnToggleCb(bool selected) { Commands++; }
}
public sealed class GameLocationScreenBattle
{
    public bool Visible = true;
    public readonly CharacterControlPanelBattle CharacterControlPanel = new CharacterControlPanelBattle();
}
public sealed class ActiveCharacterPanel
{
    public FakeTransform transform;
    public int Commands;
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnStopConcentratingCb() { Commands++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnPreviousCharacterCb() { Commands++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnNextCharacterCb() { Commands++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnGamepadShortcutsCb() { Commands++; }
}
public sealed class InventorySlotBox
{
    public FakeTransform transform;
    public int Commands;
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnSwitchAmmunitionCb(bool selected) { Commands++; }
}

public static class VerifyObserverHud
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static void Check(bool value, string description)
    {
        if (!value) throw new Exception(description);
        Console.WriteLine("PASS " + description);
    }
    public static int Main(string[] args)
    {
        Assembly plugin = Assembly.LoadFrom(args[0]);
        string directory = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "solasta-hud-" + Guid.NewGuid().ToString("N"));
        Type writerType = plugin.GetType("SolastaCoop.DiagnosticWriter", true);
        object writer = Activator.CreateInstance(writerType, Flags, null, new object[] { directory, 1048576L, 3 }, null);
        plugin.GetType("SolastaCoop.Log", true).GetField("Writer", Flags).SetValue(null, writer);
        Type type = plugin.GetType("SolastaCoop.ObserverHud", true);
        object hud = Activator.CreateInstance(type, true);
        Action<string, object[]> call = (name, values) => type.GetMethod(name, Flags).Invoke(hud, values);
        Func<string, bool> flag = name => (bool)type.GetProperty(name, Flags).GetValue(hud, null);
        Func<string> status = () => (string)type.GetProperty("Status", Flags).GetValue(hud, null);
        var party = new IGameLocationCharacterService();
        var local = new FakeCharacter { RulesetCharacter = new FakeRuleset { Guid = 100 } };
        var remote = new FakeCharacter { RulesetCharacter = new FakeRuleset { Guid = 200 } };
        party.PartyCharacters.Add(local); party.PartyCharacters.Add(remote);
        ServiceRepository.Services[typeof(IGameLocationCharacterService)] = party;
        Gui.ActivePlayerController.LocalCharacter = local;
        Gui.Battle.ActiveContender = remote;
        Photon.Pun.PhotonNetwork.InRoom = true;
        call("SetEnabled", new object[] { true });
        Check(flag("Enabled"), "Real embedded Harmony installs observer input guards: " + status());
        Gui.Battle.ActiveContender = null;
        Gui.ThrowWithoutBattle = true;
        int controllerReads = Gui.ControllerReads;
        call("Tick", null);
        Check(flag("Enabled") && !flag("Active") && Gui.ControllerReads == controllerReads,
            "Main menu without player-controller service does not disable observer HUD or access the service");
        Gui.Battle.ActiveContender = remote;
        Gui.ThrowWithoutBattle = false;
        Gui.GuiService.GrabbedCameraControls = true;
        call("Tick", null);
        Check(!flag("Active"), "A modal interface that owns camera controls prevents spectator binding");
        Gui.GuiService.GrabbedCameraControls = false;
        call("Tick", null);
        var panel = Gui.GuiService.Screen.CharacterControlPanel;
        Check(flag("Active") && panel.Visible && ReferenceEquals(panel.GuiCharacter.GameLocationCharacter, remote),
            "Hidden native panel binds the remote party member");
        Check(panel.CanvasGroup.interactable && panel.CanvasGroup.blocksRaycasts, "Mouse interaction is available for preview categories");
        Check(!panel.HandleInput(1) && panel.Inputs == 0, "Patched native control panel rejects keyboard actions");
        var actions = (CharacterActionPanel)panel.ActionPanels[0];
        actions.HandleInput(1); actions.OnActivateAction(new object()); actions.OnActivateAction(1, new object()); actions.RestoreDefaultCursor();
        panel.OnEndTurnCb(); panel.RunFrame();
        Check(actions.Commands == 0 && panel.Commands == 0 && panel.CanvasGroup.interactable,
            "Action callbacks, end turn and native input re-enabling are blocked");
        var cast = new GuiCharacterAction { ActionDefinition = new FakeDefinition { Parameter = "SelectSpell" } };
        var power = new GuiCharacterAction { ActionDefinition = new FakeDefinition { Parameter = "SelectPower" } };
        var castForm = new CharacterActionItemForm { GuiCharacterAction = cast, transform = new FakeTransform { Parent = panel.transform } };
        castForm.Refresh();
        Check(castForm.Button.interactable && castForm.canvasGroup.interactable,
            "Spent native spell category remains clickable for browsing without changing action status");
        actions.OnActivateAction(castForm);
        Check(panel.SpellSelectionPanel.Visible && ReferenceEquals(panel.SpellSelectionPanel.Caster.GameLocationCharacter, remote) &&
            panel.SpellSelectionPanel.ActionType == FakeActionType.Main && !panel.SpellSelectionPanel.CantripOnly && actions.Commands == 0,
            "Main-action category opens the native spell list for the remote character without running action activation");
        panel.SpellSelectionPanel.Cast(null, null, 3);
        actions.SpellcastEngaged(null, null, 3); actions.SpellCastConfirmed(); actions.PowerEngaged(null); actions.ExecuteEffectOfAction();
        actions.OnPointerEnter(castForm); actions.OnPointerExit(castForm); panel.OnInspectCb(); panel.RestoreDefaultCursor();
        Check(actions.Commands == 0 && panel.Inputs == 0 && panel.SpellSelectionPanel.Visible,
            "Spell clicks and direct effect/hover callbacks do not cast, create effects, move cursors or consume actions");
        var bonus = (CharacterActionPanel)panel.ActionPanels[1];
        bonus.OnActivateAction(1, cast);
        Check(panel.SpellSelectionPanel.Visible && panel.SpellSelectionPanel.ActionType == FakeActionType.Bonus,
            "Bonus-action category switches the native spell list filter");
        bonus.OnActivateAction(1, power);
        Check(!panel.SpellSelectionPanel.Visible && panel.PowerSelectionPanel.Visible &&
            panel.PowerSelectionPanel.ActionType == FakeActionType.Bonus && ReferenceEquals(panel.PowerSelectionPanel.Character, remote.RulesetCharacter),
            "Bonus powers open in the native power panel and close the spell list");
        panel.PowerSelectionPanel.Engage(null);
        Check(bonus.Commands == 0 && panel.PowerSelectionPanel.Visible, "Power selection uses an inert callback");
        panel.PowerSelectionPanel.PowerCancelled();
        Check(!panel.PowerSelectionPanel.Visible, "Native cancel closes the observer preview without a cursor callback");
        actions.OnActivateAction(castForm); actions.OnActivateAction(castForm);
        Check(!panel.SpellSelectionPanel.Visible, "Clicking the same spell category toggles the preview closed");
        // Warlock spell categories may be supplied as an invocation/pact selector.
        var warlock = new GuiCharacterAction { ActionDefinition = new FakeDefinition { Parameter = "SelectInvocation" } };
        var warlockForm = new CharacterActionItemForm { GuiCharacterAction = warlock,
            transform = new FakeTransform { Parent = panel.transform } };
        warlockForm.Refresh();
        Check(warlockForm.Button.interactable && warlockForm.canvasGroup.interactable,
            "Warlock invocation spell selector stays clickable for remote preview");
        actions.OnActivateAction(warlockForm);
        Check(panel.SpellSelectionPanel.Visible && panel.SpellSelectionPanel.Caster.GameLocationCharacter == remote &&
            actions.Commands == 0, "Warlock spell selector opens inert native spell list");
        panel.SpellSelectionPanel.Cast(null, null, 3);
        Check(actions.Commands == 0, "Clicking a viewed Warlock spell cannot cast it");
        var pact = new GuiCharacterAction { ActionDefinition = new FakeDefinition { Parameter = "SelectPactSpell" } };
        actions.OnActivateAction(1, pact);
        Check(panel.SpellSelectionPanel.Visible && actions.Commands == 0,
            "Warlock pact spell selector is available in read-only mode");
        actions.OnActivateAction(1, pact);
        Check(!panel.SpellSelectionPanel.Visible, "Toggling Warlock spell selector safely closes native preview");
        var attackForm = new CharacterActionItemForm { GuiCharacterAction = new GuiCharacterAction {
            ActionDefinition = new FakeDefinition { Parameter = "SelectTarget" } }, transform = new FakeTransform { Parent = panel.transform } };
        attackForm.Refresh(); actions.OnActivateAction(attackForm);
        Check(!attackForm.Button.interactable && !panel.SpellSelectionPanel.Visible && actions.Commands == 0,
            "Unsupported combat categories remain disabled and inert");
        actions.OnActivateAction(castForm);
        int opened = panel.SpellSelectionPanel.Binds;
        Gui.Battle.ActiveContender = local;
        actions.OnActivateAction(castForm);
        Check(panel.SpellSelectionPanel.Binds == opened, "A turn transition before the next tick cannot open a stale remote preview");
        call("Tick", null);
        Check(!panel.SpellSelectionPanel.Visible && !flag("Active"), "Leaving the remote turn closes the native preview");
        panel.Hide(true); Gui.Battle.ActiveContender = remote; call("Tick", null);
        var weapon = new WieldedConfigurationSelector { transform = new FakeTransform { Parent = panel.transform } };
        weapon.OnToggleCb(true);
        Check(weapon.Commands == 0, "Weapon-set callback cannot change equipment in spectator mode");
        var portrait = new ActiveCharacterPanel { transform = new FakeTransform { Parent = panel.transform } };
        portrait.OnStopConcentratingCb(); portrait.OnPreviousCharacterCb(); portrait.OnNextCharacterCb(); portrait.OnGamepadShortcutsCb();
        var slot = new InventorySlotBox { transform = new FakeTransform { Parent = panel.transform } };
        slot.OnSwitchAmmunitionCb(true);
        Check(portrait.Commands == 0 && slot.Commands == 0, "Portrait and ammunition callbacks cannot send spectator commands");
        var unrelated = new CharacterControlPanelBattle();
        Check(unrelated.HandleInput(1) && unrelated.Inputs == 1, "Unrelated native panels keep their original input behavior");
        panel.Bind(local, new object()); panel.Show(true); Gui.Battle.ActiveContender = local;
        actions.OnActivateAction(new object());
        Check(actions.Commands == 1, "A native panel rebound to the local character immediately regains callbacks before polling");
        call("Tick", null);
        Check(!flag("Active") && panel.Visible && panel.CanvasGroup.interactable && panel.HandleInput(1),
            "Handoff never hides or disables a panel already rebound to the local character");
        panel.Hide(true); Gui.Battle.ActiveContender = remote;
        call("Tick", null);
        Check(flag("Active"), "Spectator HUD can resume on the next remote turn");
        actions.OnActivateAction(castForm);
        panel.SpellSelectionPanel.Bind(new FakeGuiCharacter { GameLocationCharacter = local }, () => { }, (a,b,c) => { }, FakeActionType.Main, false);
        panel.SpellSelectionPanel.Show(true);
        int hides = panel.SpellSelectionPanel.Hides;
        call("SetEnabled", new object[] { false });
        Check(panel.SpellSelectionPanel.Visible && panel.SpellSelectionPanel.Hides == hides,
            "Native menu binding releases the observer before Bind so parent Hide cannot close the new local menu");
        panel.SpellSelectionPanel.Hide(true); panel.Hide(true); call("SetEnabled", new object[] { true }); call("Tick", null);
        Gui.Battle.ActiveContender = new FakeCharacter();
        call("Tick", null);
        Check(!flag("Active") && !panel.Visible, "Enemy turn releases the borrowed panel");
        Gui.Battle.ActiveContender = remote; panel.Bind(local, new object()); panel.Show(true);
        int binds = panel.Binds;
        call("Tick", null);
        Check(!flag("Active") && panel.Binds == binds && panel.Visible,
            "An already visible reaction or game-owned HUD is left alone");
        panel.Hide(true); panel.CanvasGroup = null;
        call("Tick", null);
        Check(!flag("Enabled") && !flag("Active") && !panel.Visible, "Missing mouse guard disables the feature before binding");
        Console.WriteLine("Observer HUD tests: PASS (real Harmony, fake game UI)");
        return 0;
    }
}
