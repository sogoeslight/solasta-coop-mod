using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace SolastaCoop
{
    internal static class LobbyOrderPolicy
    {
        internal static bool ValidRoster(string[] names)
        {
            return names != null && names.Length >= 2 && names.Length <= 8 &&
                names.All(n => !String.IsNullOrEmpty(n) && n.Length <= 256) &&
                names.Distinct(StringComparer.Ordinal).Count() == names.Length;
        }

        internal static bool SameMembers(string[] first, string[] second)
        {
            return ValidRoster(first) && ValidRoster(second) && first.Length == second.Length &&
                new HashSet<string>(first, StringComparer.Ordinal).SetEquals(second);
        }

        internal static bool TryRead(object value, int host, string[] roster, out string[] order)
        {
            order = null;
            object[] packet = value as object[];
            if (packet == null || packet.Length != 4 || !object.Equals(packet[0], 1) || host <= 0 ||
                !object.Equals(packet[1], host)) return false;
            string[] source = packet[2] as string[], desired = packet[3] as string[];
            if (!SameMembers(source, roster) || !SameMembers(source, desired)) return false;
            order = (string[])desired.Clone();
            return true;
        }

        internal static List<int[]> Plan(string[] current, string[] desired)
        {
            if (!SameMembers(current, desired)) throw new InvalidOperationException("Party order requires the same distinct heroes.");
            var swaps = new List<int[]>();
            string[] working = (string[])current.Clone();
            for (int i = 0; i < working.Length; i++)
            {
                if (working[i] == desired[i]) continue;
                int other = Array.IndexOf(working, desired[i], i + 1);
                swaps.Add(new[] { i, other });
                string previous = working[i]; working[i] = working[other]; working[other] = previous;
            }
            return swaps;
        }
    }

    // Native character bindings and Photon ownership properties remain intact. Only the card
    // positions and the game's existing party SortIndex operation use the shared display order.
    internal sealed class LobbyPartyOrder : IDisposable
    {
        private const string PropertyKey = "SolastaCoopMod.party-order/1";
        private object room, panel, appliedSession;
        private string appliedSignature;
        private float nextPoll, pendingSince, nextErrorLog;
        private string[] roster, order, pending;
        private string selected;
        private bool visible;
        private RectTransform table;
        private RectTransform[] plates;
        private Vector3[] positions;
        private LayoutGroup[] layouts;
        private bool[] layoutEnabled;
        private int screenWidth, screenHeight;
        private readonly Vector3[] corners = new Vector3[4];
        internal string Status { get; private set; } = "Lobby party order is available to the host.";

        private static object Photon { get { return Read.Type("Photon.Pun.PhotonNetwork"); } }
        private static int Host
        {
            get { object id = Read.Get(Read.Get(Photon, "MasterClient"), "ActorNumber"); return id is int ? (int)id : 0; }
        }
        private static bool CanEdit
        {
            get { return object.Equals(Read.Get(Photon, "InRoom"), true) &&
                object.Equals(Read.Get(Photon, "OfflineMode"), false) && object.Equals(Read.Get(Photon, "IsMasterClient"), true); }
        }
        private bool CanInteract { get { return !object.Equals(Read.Get(panel, "ActionTaken"), true) &&
            !object.Equals(Read.Get(Read.Get(panel, "CanvasGroup"), "interactable"), false); } }
        private object Payload
        {
            get
            {
                IDictionary properties = Read.Get(room, "CustomProperties") as IDictionary;
                return properties != null && properties.Contains(PropertyKey) ? properties[PropertyKey] : null;
            }
        }
        private static string CharacterName(object character)
        {
            return Read.Get(character, "Name") as string;
        }

        internal void Tick()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.1f;
            try
            {
                object nextRoom = object.Equals(Read.Get(Photon, "InRoom"), true) ? Read.Get(Photon, "CurrentRoom") : null;
                if (!object.ReferenceEquals(nextRoom, room))
                {
                    RestoreLayout();
                    room = nextRoom; roster = order = pending = null; selected = null;
                    appliedSession = null; appliedSignature = null;
                }
                if (room == null) { visible = false; return; }
                Type panelType = Read.Type("NewAdventurePanel");
                panel = panelType == null ? null : Read.Call(Read.Get(Read.Type("Gui"), "GuiService"), "GetScreen", panelType);
                bool nextVisible = panel != null && object.Equals(Read.Get(panel, "Visible"), true) &&
                    !object.Equals(Read.Get(panel, "Hiding"), true) && !object.Equals(Read.Get(panel, "Showing"), true) &&
                    object.Equals(Read.Get(panel, "MultiplayerMode"), true);
                if (nextVisible && !visible) { appliedSession = null; appliedSignature = null; }
                visible = nextVisible;
                if (visible) RefreshLobby();
                else
                {
                    RestoreLayout();
                    selected = null; pending = null;
                    ApplyInGame();
                }
            }
            catch (Exception ex)
            {
                ReportError(ex);
            }
        }

        private void ReportError(Exception ex)
        {
            Status = "Party order waiting: " + ex.GetBaseException().Message;
            if (Time.unscaledTime < nextErrorLog) return;
            nextErrorLog = Time.unscaledTime + 15f;
            Log.Error("core", "Lobby party order", ex);
        }

        private void RefreshLobby()
        {
            RectTransform nextTable = Read.Get(panel, "characterSessionPlatesTable") as RectTransform;
            if (nextTable == null) { RestoreLayout(); return; }
            Type plateType = Read.Type("CharacterPlateSession");
            var cards = new List<RectTransform>();
            var names = new List<string>();
            for (int i = 0; i < nextTable.childCount; i++)
            {
                RectTransform child = nextTable.GetChild(i) as RectTransform;
                if (child == null || !child.gameObject.activeInHierarchy) continue;
                object card = child.GetComponent(plateType);
                cards.Add(child);
                names.Add(CharacterName(Read.Get(card, "GuiCharacter")));
            }
            string[] current = names.ToArray();
            if (!LobbyOrderPolicy.ValidRoster(current))
            {
                RestoreLayout(); roster = order = null; selected = null;
                Status = "Select all heroes with distinct names to change party order.";
                return;
            }
            if (table != nextTable || roster == null || !roster.SequenceEqual(current) ||
                plates == null || !plates.SequenceEqual(cards) || screenWidth != Screen.width || screenHeight != Screen.height)
            {
                RestoreLayout();
                LayoutRebuilder.ForceRebuildLayoutImmediate(nextTable);
                table = nextTable; plates = cards.ToArray(); roster = current;
                positions = plates.Select(p => p.localPosition).ToArray();
                layouts = table.GetComponents<LayoutGroup>(); layoutEnabled = layouts.Select(l => l.enabled).ToArray();
                screenWidth = Screen.width; screenHeight = Screen.height;
                selected = null;
            }
            string[] shared;
            order = LobbyOrderPolicy.TryRead(Payload, Host, roster, out shared) ? shared : (string[])roster.Clone();
            if (pending != null && (pending.SequenceEqual(order) || Time.unscaledTime - pendingSince > 5f)) pending = null;
            Status = pending != null ? "Waiting for the shared order to update." :
                CanEdit ? "Choose a slot, then another slot to swap. Hero ownership stays unchanged." : "The host controls party order.";
        }

        private bool Publish(string[] desired)
        {
            if (!visible || pending != null || !CanEdit || !CanInteract ||
                !object.ReferenceEquals(room, Read.Get(Photon, "CurrentRoom")) || !LobbyOrderPolicy.SameMembers(roster, desired)) return false;
            MethodInfo method = room.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Single(m => m.Name == "SetCustomProperties" && m.GetParameters().Length == 3);
            IDictionary properties = Activator.CreateInstance(method.GetParameters()[0].ParameterType) as IDictionary;
            if (properties == null) return false;
            properties[PropertyKey] = new object[] { 1, Host, (string[])roster.Clone(), (string[])desired.Clone() };
            if (!object.Equals(method.Invoke(room, new object[] { properties, null, null }), true))
            { Status = "Could not publish party order. Try again."; return false; }
            pending = (string[])desired.Clone(); pendingSince = Time.unscaledTime;
            selected = null;
            Log.Event("core", "INFO", "Lobby party order published: " + string.Join(", ", desired));
            return true;
        }

        private void SelectSlot(int slot)
        {
            if (order == null || slot < 0 || slot >= order.Length || pending != null || !CanEdit || !CanInteract) return;
            string name = order[slot];
            if (selected == null) { selected = name; return; }
            if (selected == name) { selected = null; return; }
            int first = Array.IndexOf(order, selected);
            if (first < 0) { selected = null; return; }
            string[] desired = (string[])order.Clone();
            desired[first] = name; desired[slot] = selected;
            Publish(desired);
        }

        internal void LateTick()
        {
            if (!visible || table == null || roster == null || order == null || positions == null) return;
            try
            {
                for (int i = 0; i < layouts.Length; i++) if (layouts[i] != null) layouts[i].enabled = false;
                for (int slot = 0; slot < order.Length; slot++)
                {
                    int original = Array.IndexOf(roster, order[slot]);
                    if (original >= 0 && plates[original] != null) plates[original].localPosition = positions[slot];
                }
            }
            catch (Exception ex) { ReportError(ex); }
        }

        private void RestoreLayout()
        {
            if (plates != null && positions != null)
                for (int i = 0; i < plates.Length; i++) if (plates[i] != null) plates[i].localPosition = positions[i];
            if (layouts != null)
                for (int i = 0; i < layouts.Length; i++) if (layouts[i] != null) layouts[i].enabled = layoutEnabled[i];
            table = null; plates = null; positions = null; layouts = null; layoutEnabled = null;
        }

        internal void Draw()
        {
            if (!visible || plates == null || order == null) return;
            bool enabled = GUI.enabled;
            try
            {
                GUI.enabled = enabled && CanEdit && CanInteract && pending == null;
                float top = Screen.height, left = Screen.width, right = 0f;
                for (int slot = 0; slot < order.Length; slot++)
                {
                    int original = Array.IndexOf(roster, order[slot]);
                    RectTransform card = original < 0 ? null : plates[original];
                    if (card == null) continue;
                    card.GetWorldCorners(corners);
                    Canvas canvas = card.GetComponentInParent<Canvas>();
                    if (canvas != null) canvas = canvas.rootCanvas;
                    Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                    Vector2 lower = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                    Vector2 upper = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                    float width = Math.Min(150f, Math.Abs(upper.x - lower.x) - 4f);
                    if (width < 40f) continue;
                    Rect button = new Rect((lower.x + upper.x - width) / 2f, Math.Max(30f, Screen.height - upper.y - 26f), width, 24f);
                    top = Math.Min(top, button.y); left = Math.Min(left, button.x); right = Math.Max(right, button.xMax);
                    string label = !CanEdit ? "Slot " + (slot + 1) : selected == order[slot] ? "Cancel selection" :
                        selected == null ? "Move slot " + (slot + 1) : "Swap with " + (slot + 1);
                    if (GUI.Button(button, label)) SelectSlot(slot);
                }
                GUI.enabled = enabled;
                if (right > left) GUI.Box(new Rect(left, Math.Max(2f, top - 28f), right - left, 26f), Status);
            }
            catch (Exception ex) { ReportError(ex); }
            finally { GUI.enabled = enabled; }
        }

        private void ApplyInGame()
        {
            if (Read.Get(Read.Type("Gui"), "GameLocation") == null || Read.Get(Read.Type("Gui"), "Battle") != null) return;
            object session = Read.Get(Read.Service("ISessionService"), "Session");
            object characters = Read.Service("IGameLocationCharacterService");
            object[] party = Read.Items(Read.Get(characters, "PartyCharacters")).ToArray();
            string[] names = party.Select(c => CharacterName(Read.Get(c, "RulesetCharacter"))).ToArray();
            string[] desired;
            if (session == null || !LobbyOrderPolicy.TryRead(Payload, Host, names, out desired)) return;
            string signature = Host + ":" + string.Join("\n", desired);
            if (object.ReferenceEquals(appliedSession, session) && appliedSignature == signature) return;
            MethodInfo swap = characters.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Single(m => m.Name == "SwapPartyMembers" && m.GetParameters().Length == 2 &&
                    m.GetParameters().All(p => p.ParameterType == typeof(int)));
            foreach (int[] pair in LobbyOrderPolicy.Plan(names, desired)) swap.Invoke(characters, new object[] { pair[0], pair[1] });
            string[] actual = Read.Items(Read.Get(characters, "PartyCharacters"))
                .Select(c => CharacterName(Read.Get(c, "RulesetCharacter"))).ToArray();
            if (!actual.SequenceEqual(desired)) throw new InvalidOperationException("Native party order has not settled yet.");
            appliedSession = session; appliedSignature = signature;
            Status = "Lobby party order applied through native party swaps.";
            Log.Event("core", "INFO", Status);
        }

        public void Dispose() { RestoreLayout(); visible = false; }
    }
}
