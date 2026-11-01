using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace SolastaCoop
{
    internal sealed partial class ObserverHud
    {
        private object previewPanel, previewActionType;
        private Delegate previewCancelled;
        private string previewKind;
        private int previewsOpened, previewClicksBlocked;
        internal string DiagnosticSummary
        {
            get { return Status + "\npreview=" + (previewKind ?? "none") + "; actionType=" +
                Read.Text(previewActionType) + "; opened=" + previewsOpened + "; inertItemClicks=" + previewClicksBlocked +
                "\nLast observer exception: " + LastFailure; }
        }

        private static string PreviewKind(object action)
        {
            // Some Solasta actions (notably Warlock pact magic and invocations) use a
            // different selector from the ordinary prepared-spell action.
            // Accept only spell/power *selection* categories, never actual casting.
            object actual = Read.Get(action, "GuiCharacterAction") ?? action;
            object definition = Read.Get(actual, "ActionDefinition");
            string parameter = Read.Text(Read.Get(definition, "Parameter"));
            if (parameter == "SelectPower" || parameter == "SelectInvocationPower") return "power";
            if (parameter == "SelectSpell" || parameter == "SelectInvocation" ||
                parameter == "SelectSpellRepertoire" || parameter == "SelectPactSpell" ||
                parameter == "SelectWarlockSpell" || parameter == "SelectSpellSlot") return "spell";
            // A fallback for action definitions where Parameter is empty, but the
            // game's actual action ID is a recognized *selector*, not CastSpell.
            string id = Read.Text(Read.Get(definition, "Id") ?? Read.Get(actual, "ActionId"));
            if (id == "SelectPower" || id == "SelectInvocationPower") return "power";
            if (id == "SelectSpell" || id == "SelectInvocation" || id == "SelectSpellRepertoire" ||
                id == "SelectPactSpell" || id == "SelectWarlockSpell") return "spell";
            return null;
        }

        private bool CanBrowse()
        {
            object gui = Read.Type("Gui"), battle = Read.Get(gui, "Battle");
            object state = Read.Get(Read.Get(battle, "FiniteStateMachine"), "CurrentState");
            Type ready = Read.Type("BattleState_TurnReady");
            return Enabled && borrowed && ready != null && ready.IsInstanceOfType(state) &&
                Read.Same(Read.Get(battle, "ActiveContender"), actor) &&
                Read.Same(Read.Get(Read.Get(panel, "GuiCharacter"), "GameLocationCharacter"), actor) &&
                Object.Equals(Read.Call(Read.Get(gui, "ActivePlayerController"), "IsCharacterControlled", actor), false) &&
                Object.Equals(Read.Get(Read.Type("Photon.Pun.PhotonNetwork"), "InRoom"), true) &&
                !Object.Equals(Read.Get(Read.Get(gui, "GuiService"), "GrabbedCameraControls"), true);
        }

        private static bool BrowseForm(object __instance, object __0)
        {
            if (!Observed(__instance)) return true;
            current.Browse(__instance, Read.Get(__0, "GuiCharacterAction"));
            return false;
        }

        private static bool BrowseAction(object __instance, object __1)
        {
            if (!Observed(__instance)) return true;
            current.Browse(__instance, __1);
            return false;
        }

        private Delegate Callback(Type type, string method)
        {
            MethodInfo invoke = type.GetMethod("Invoke");
            if (invoke == null || invoke.ReturnType != typeof(void)) throw new InvalidOperationException("Unsupported preview callback.");
            ParameterExpression[] parameters = invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
            return Expression.Lambda(type, Expression.Call(Expression.Constant(this),
                typeof(ObserverHud).GetMethod(method, Flags)), parameters).Compile();
        }

        private void PreviewItemClicked()
        {
            previewClicksBlocked++;
            if (previewClicksBlocked <= 3) Log.Write("Observer preview item selected: no action, effect or target cursor created.");
        }

        private bool OwnsPreview()
        {
            return previewPanel != null && previewCancelled != null &&
                Object.Equals(Read.Get(previewPanel, previewKind == "spell" ? "SpellcastCancelled" : "PowerCancelled"), previewCancelled);
        }

        private void Browse(object actionPanel, object action)
        {
            if (!CanBrowse()) return;
            string kind = PreviewKind(action);
            if (kind == null) return;
            try
            {
                object type = Read.Get(actionPanel, "ActionType");
                Log.Event("core", "INFO", "Observer selector opened; kind=" + kind +
                    "; parameter=" + Read.Text(Read.Get(Read.Get(Read.Get(action, "GuiCharacterAction") ?? action,
                        "ActionDefinition"), "Parameter")) + "; actionType=" + Read.Text(type));
                if (OwnsPreview() && kind == previewKind && Object.Equals(type, previewActionType) &&
                    Object.Equals(Read.Get(previewPanel, "Visible"), true)) { ClosePreview(); return; }
                ClosePreview();
                object menu = Read.Get(actionPanel, kind == "spell" ? "SpellSelectionPanel" : "PowerSelectionPanel");
                if (menu == null || Object.Equals(Read.Get(menu, "Visible"), true))
                { SetStatus("The game is using this list. Preview is waiting."); return; }
                MethodInfo bind = menu.GetType().GetMethods(Flags).SingleOrDefault(m => m.Name == "Bind" &&
                    m.GetParameters().Length == (kind == "spell" ? 5 : 3));
                if (bind == null) throw new MissingMethodException(menu.GetType().Name, "Bind");
                ParameterInfo[] p = bind.GetParameters();
                previewPanel = menu; previewKind = kind; previewActionType = type;
                previewCancelled = Callback(p[1].ParameterType, "ClosePreview");
                Delegate inert = Callback(p[2].ParameterType, "PreviewItemClicked");
                SetMember(menu, "ActionType", type);
                object guiCharacter = Read.Get(panel, "GuiCharacter");
                object[] args = kind == "spell" ?
                    new object[] { guiCharacter, previewCancelled, inert, type, Object.Equals(Read.Get(actor, "CanOnlyUseCantrips"), true) } :
                    new object[] { Read.Get(guiCharacter, "RulesetCharacter"), previewCancelled, inert };
                object gameObject = Read.Get(menu, "gameObject");
                if (gameObject != null) Invoke(gameObject, "SetActive", true);
                bind.Invoke(menu, args);
                if (gameObject != null) Invoke(gameObject, "SetActive", false);
                Invoke(menu, "Show", true);
                previewsOpened++;
                SetStatus("Teammate list open: " + (kind == "spell" ? "spells" : "abilities") +
                    ", " + Read.Text(type) + ". Hover to view descriptions. Casting or activating is disabled.");
            }
            catch (Exception e)
            {
                ClosePreview();
                LastFailure = e.GetBaseException().ToString();
                Log.Error("core", "Open observer preview", e);
                SetStatus("Failed to open list: " + e.GetBaseException().Message);
            }
        }

        private void ClosePreview()
        {
            try { if (OwnsPreview()) Invoke(previewPanel, "Hide", true); }
            catch (Exception e) { Log.Error("core", "Close observer preview", e); }
            finally { previewPanel = null; previewCancelled = null; previewKind = null; previewActionType = null; }
        }

        private static void RefreshPreviewButton(object __instance)
        {
            if (!Observed(__instance)) return;
            try
            {
                bool browse = PreviewKind(Read.Get(__instance, "GuiCharacterAction")) != null;
                object group = Read.Get(__instance, "canvasGroup"), button = Read.Get(__instance, "Button");
                if (group != null) SetMember(group, "interactable", browse);
                if (button != null) SetMember(button, "interactable", browse);
            }
            catch (Exception e)
            {
                Log.Error("core", "Refresh observer preview button", e);
                ObserverHud owner = current;
                if (owner != null) { owner.Enabled = false; owner.Dispose(); }
            }
        }

        private static void PreviewBinding(object __instance, object __1)
        {
            ObserverHud owner = current;
            if (owner == null || !owner.borrowed || Object.Equals(__1, owner.previewCancelled)) return;
            // Release before the game's new Bind starts, so parent Hide cannot close the new local menu.
            if (ReferenceEquals(__instance, Read.Get(owner.panel, "SpellSelectionPanel")) ||
                ReferenceEquals(__instance, Read.Get(owner.panel, "PowerSelectionPanel"))) owner.Dispose();
        }

        private static void EnsurePreviewHooks()
        {
            Type form = Read.Type("CharacterActionItemForm");
            MethodInfo refresh = form == null ? null : form.GetMethod("Refresh", Flags);
            HarmonySupport.Patch(refresh, null, typeof(ObserverHud).GetMethod("RefreshPreviewButton", Flags));
            foreach (string name in new[] { "SpellSelectionPanel", "PowerSelectionPanel" })
            {
                Type type = Read.Type(name);
                if (type == null || type.GetProperty("ActionType", Flags) == null ||
                    !type.GetMethods(Flags).Any(m => m.Name == "Bind" && m.GetParameters().Length == (name == "SpellSelectionPanel" ? 5 : 3)))
                    throw new MissingMemberException(name + " preview API");
                MethodInfo bind = type.GetMethods(Flags).Single(m => m.Name == "Bind" &&
                    m.GetParameters().Length == (name == "SpellSelectionPanel" ? 5 : 3));
                HarmonySupport.Patch(bind, typeof(ObserverHud).GetMethod("PreviewBinding", Flags));
            }
        }
    }
}
