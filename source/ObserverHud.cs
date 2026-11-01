using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SolastaCoop
{
    // Borrows the game's hidden battle HUD. Ownership and selected characters are never changed.
    internal sealed partial class ObserverHud : IDisposable
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static ObserverHud current;
        private static bool hooksReady;
        private object panel, actor, canvas;
        private bool oldInteractable, oldBlocksRaycasts, borrowed;
        private DateTime nextRefresh;
        internal bool Enabled { get; private set; }
        internal string Status { get; private set; }
        internal bool Active { get { return borrowed; } }
        internal string LastFailure = "none";

        internal ObserverHud() { Status = "Штатная панель союзника выключена."; }

        private void SetStatus(string text)
        {
            if (Status != text) Log.Write("Observer HUD: " + text);
            Status = text;
        }

        internal void SetEnabled(bool enabled)
        {
            if (!enabled)
            {
                Enabled = false;
                Dispose();
                SetStatus("Штатная панель союзника выключена.");
                return;
            }
            try
            {
                EnsureHooks();
                Enabled = true;
                SetStatus("Ожидание хода союзника. Штатная панель работает только для просмотра.");
            }
            catch (Exception e)
            {
                Enabled = false;
                Dispose();
                Log.Error("core", "Enable observer HUD", e);
                SetStatus("Панель не включена: " + e.GetBaseException().Message);
            }
        }

        internal static void SetMember(object target, string name, object value)
        {
            if (target == null) throw new InvalidOperationException("HUD object missing: " + name);
            Type type = target.GetType();
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(name, Flags | BindingFlags.DeclaredOnly);
                if (property != null && property.CanWrite) { property.SetValue(target, value, null); return; }
                FieldInfo field = type.GetField(name, Flags | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }
            throw new MissingMemberException(target.GetType().Name, name);
        }

        private static object Invoke(object target, string name, params object[] args)
        {
            if (target == null) throw new InvalidOperationException("HUD target missing: " + name);
            MethodInfo method = target.GetType().GetMethods(Flags).FirstOrDefault(m => m.Name == name &&
                !m.IsGenericMethodDefinition && m.GetParameters().Length == args.Length &&
                m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(v => v));
            if (method == null) throw new MissingMethodException(target.GetType().Name, name);
            return method.Invoke(target, args);
        }

        internal void Tick()
        {
            if (!Enabled) return;
            try
            {
                Type gui = Read.Type("Gui");
                object battle = Read.Get(gui, "Battle");
                object nextActor = Read.Get(battle, "ActiveContender");
                // Gui.ActivePlayerController dereferences a service which is absent in menus.
                // Never evaluate it before a loaded battle and an active actor exist.
                if (nextActor == null)
                {
                    Dispose(); SetStatus("Ожидание боя и действующего союзника."); return;
                }
                object controller = Read.Get(gui, "ActivePlayerController");
                if (controller == null)
                {
                    Dispose(); SetStatus("Ожидание боя и действующего союзника."); return;
                }
                object owned = Read.Call(controller, "IsCharacterControlled", nextActor);
                object state = Read.Get(Read.Get(battle, "FiniteStateMachine"), "CurrentState");
                Type ready = Read.Type("BattleState_TurnReady");
                bool partyMember = nextActor != null && Read.Items(Read.Get(Read.Service("IGameLocationCharacterService"), "PartyCharacters"))
                    .Any(p => Read.Same(p, nextActor));
                Type photon = Read.Type("Photon.Pun.PhotonNetwork");
                bool multiplayer = Object.Equals(Read.Get(photon, "InRoom"), true);
                bool eligible = multiplayer && partyMember && Object.Equals(owned, false) &&
                    ready != null && ready.IsInstanceOfType(state);
                if (!eligible)
                {
                    Dispose();
                    SetStatus("Ожидание хода союзника. Свой ход, ход врага и переходы не изменяются.");
                    return;
                }
                object service = Read.Get(gui, "GuiService");
                if (Object.Equals(Read.Get(service, "GrabbedCameraControls"), true))
                {
                    Dispose(); SetStatus("Открыт модальный интерфейс игры. Просмотр ожидает."); return;
                }
                Type screenType = Read.Type("GameLocationScreenBattle");
                object screen = screenType == null ? null : Read.Call(service, "GetScreen", screenType);
                object nextPanel = Read.Get(screen, "CharacterControlPanel");
                bool available = Object.Equals(Read.Get(screen, "Visible"), true) &&
                    Object.Equals(Read.Get(nextPanel, "Loaded"), true) && !Object.Equals(Read.Get(nextPanel, "Hiding"), true);
                if (!available)
                {
                    Dispose(); SetStatus("Штатный боевой интерфейс пока недоступен."); return;
                }
                if (borrowed && (!ReferenceEquals(nextPanel, panel) || !Read.Same(actor, nextActor) ||
                    !Read.Same(Read.Get(Read.Get(panel, "GuiCharacter"), "GameLocationCharacter"), actor))) Dispose();
                if (!borrowed)
                {
                    // A visible HUD can belong to a reaction or another UI flow. Leave it alone.
                    if (Object.Equals(Read.Get(nextPanel, "Visible"), true))
                    {
                        SetStatus("Штатная панель уже используется игрой. Просмотр ожидает."); return;
                    }
                    object group = Read.Get(nextPanel, "CanvasGroup");
                    object interactable = Read.Get(group, "interactable"), raycasts = Read.Get(group, "blocksRaycasts");
                    if (!(interactable is bool) || !(raycasts is bool))
                        throw new InvalidOperationException("Нет CanvasGroup для блокировки управления.");
                    panel = nextPanel; actor = nextActor; canvas = group;
                    oldInteractable = (bool)interactable; oldBlocksRaycasts = (bool)raycasts;
                    borrowed = true; current = this;
                    SetMember(canvas, "interactable", false);
                    SetMember(canvas, "blocksRaycasts", true);
                    object anchor = Read.Get(panel, "TooltipBlocker");
                    Invoke(panel, "Bind", actor, anchor);
                    Invoke(panel, "Show", true);
                    EnforceReadOnly();
                    nextRefresh = DateTime.MinValue;
                    SetStatus("Просмотр союзника: нажмите Заклинания/Способности основного или бонусного действия. Каст заблокирован.");
                }
                EnforceReadOnly();
                if (DateTime.UtcNow >= nextRefresh)
                {
                    Invoke(panel, "RefreshNow");
                    EnforceReadOnly();
                    nextRefresh = DateTime.UtcNow.AddSeconds(0.5);
                }
            }
            catch (Exception e)
            {
                Enabled = false;
                Dispose();
                LastFailure = e.GetBaseException().ToString();
                Log.Error("core", "Observer HUD disabled after failure", e);
                SetStatus("Просмотр отключён после ошибки: " + e.GetBaseException().Message);
            }
        }

        private void EnforceReadOnly()
        {
            SetMember(canvas, "interactable", true);
            SetMember(canvas, "blocksRaycasts", true);
            object blocker = Read.Get(Read.Get(panel, "TooltipBlocker"), "gameObject");
            if (blocker != null) Invoke(blocker, "SetActive", false);
        }

        private static bool Observed(object instance)
        {
            ObserverHud owner = current;
            if (owner == null || !owner.borrowed || instance == null) return false;
            if (!Read.Same(Read.Get(Read.Get(owner.panel, "GuiCharacter"), "GameLocationCharacter"), owner.actor)) return false;
            if (ReferenceEquals(instance, owner.panel)) return true;
            if (Read.Items(Read.Get(owner.panel, "ActionPanels")).Any(p => ReferenceEquals(p, instance))) return true;
            if (ReferenceEquals(owner.previewPanel, instance) && owner.OwnsPreview()) return true;
            object root = Read.Get(owner.panel, "transform"), child = Read.Get(instance, "transform");
            return root != null && child != null && Object.Equals(Read.Call(child, "IsChildOf", root), true);
        }

        private static bool GuardInput(object __instance, ref bool __result)
        {
            if (!Observed(__instance)) return true;
            __result = false; return false;
        }
        private static bool GuardAction(object __instance) { return !Observed(__instance); }

        private static void EnsureHooks()
        {
            if (hooksReady) return;
            foreach (string spec in new[] {
                "CharacterControlPanel|HandleInput|input", "CharacterControlPanelBattle|HandleInput|input",
                "CharacterActionPanel|HandleInput|input", "CharacterControlPanelBattle|OnEndTurnCb|action",
                "CharacterControlPanelBattle|OnUpdateHook|action", "CharacterActionPanel|OnActivateAction|action",
                "CharacterActionPanel|RestoreDefaultCursor|action", "WieldedConfigurationSelector|OnToggleCb|action",
                "CharacterActionPanel|OnPointerEnter|action", "CharacterActionPanel|OnPointerExit|action",
                "CharacterActionPanel|SpellcastEngaged|action", "CharacterActionPanel|SpellCastConfirmed|action",
                "CharacterActionPanel|PowerEngaged|action", "CharacterActionPanel|ExecuteEffectOfAction|action",
                "CharacterControlPanel|OnInspectCb|action", "CharacterControlPanel|RestoreDefaultCursor|action",
                "ActiveCharacterPanel|OnStopConcentratingCb|action", "ActiveCharacterPanel|OnPreviousCharacterCb|action",
                "ActiveCharacterPanel|OnNextCharacterCb|action", "ActiveCharacterPanel|OnGamepadShortcutsCb|action",
                "InventorySlotBox|OnSwitchAmmunitionCb|action" })
            {
                string[] fields = spec.Split('|');
                Type type = Read.Type(fields[0]);
                MethodInfo[] targets = type == null ? new MethodInfo[0] : type.GetMethods(Flags)
                    .Where(m => m.DeclaringType == type && m.Name == fields[1]).ToArray();
                if (targets.Length == 0) throw new MissingMethodException(spec);
                foreach (MethodInfo target in targets)
                {
                    string handler = fields[2] == "input" ? "GuardInput" : "GuardAction";
                    if (fields[1] == "OnActivateAction")
                        handler = target.GetParameters().Length == 1 ? "BrowseForm" : "BrowseAction";
                    MethodInfo prefix = typeof(ObserverHud).GetMethod(handler, Flags);
                    HarmonySupport.Patch(target, prefix);
                }
            }
            EnsurePreviewHooks();
            hooksReady = true;
            Log.Write("Observer HUD input guards installed using Harmony " + HarmonySupport.Version);
        }

        public void Dispose()
        {
            if (!borrowed) return;
            ClosePreview();
            bool ours = false;
            try
            {
                // Never hide a panel that the game has already rebound to another character.
                ours = Read.Same(Read.Get(Read.Get(panel, "GuiCharacter"), "GameLocationCharacter"), actor);
                if (ours) Invoke(panel, "Hide", true);
            }
            catch (Exception e) { Log.Error("core", "Release observer HUD", e); }
            finally
            {
                try
                {
                    if (ours)
                    {
                        SetMember(canvas, "interactable", oldInteractable);
                        SetMember(canvas, "blocksRaycasts", oldBlocksRaycasts);
                    }
                }
                catch (Exception e) { Log.Error("core", "Restore HUD interactivity", e); }
                if (ReferenceEquals(current, this)) current = null;
                borrowed = false; panel = null; actor = null; canvas = null;
            }
        }
    }
}
