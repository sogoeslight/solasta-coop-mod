using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: AssemblyVersion("0.1.20.0")]

namespace Doorstop
{
    public static class Entrypoint
    {
        private static bool started;
        public static void Start()
        {
            if (started) return;
            started = true;
            SolastaCoop.Log.Write("Bootstrap started, waiting for sceneLoaded.");
            SceneManager.sceneLoaded += SceneLoaded;
        }

        private static void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SolastaCoop.Log.Write("Scene loaded; name=" + scene.name + "; mode=" + mode);
            if (SolastaCoop.CoopView.Instance != null) return;
            try
            {
                GameObject go = new GameObject("SolastaCoopMod");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<SolastaCoop.CoopView>();
                SolastaCoop.Log.Write("Overlay created on sceneLoaded.");
            }
            catch (Exception e) { SolastaCoop.Log.Error("core", "Create overlay", e); }
        }
    }
}

namespace SolastaCoop
{
    internal static class Read
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;
        private static readonly Dictionary<string, Type> Types = new Dictionary<string, Type>();
        internal static Type Type(string name)
        {
            Type value;
            if (Types.TryGetValue(name, out value)) return value;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                value = assembly.GetType(name, false);
                if (value != null) { Types[name] = value; return value; }
            }
            return null;
        }
        internal static object Get(object target, string name)
        {
            if (target == null) return null;
            Type type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(name, Flags | BindingFlags.DeclaredOnly);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(instance, null);
                FieldInfo field = type.GetField(name, Flags | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(instance);
                type = type.BaseType;
            }
            return null;
        }
        internal static object Call(object target, string name, params object[] args)
        {
            if (target == null) return null;
            Type type = target as Type ?? target.GetType();
            MethodInfo method = type.GetMethods(Flags).FirstOrDefault(m => m.Name == name &&
                !m.IsGenericMethodDefinition && m.GetParameters().Length == args.Length &&
                m.GetParameters().Select((p, i) => args[i] == null ||
                    p.ParameterType.IsInstanceOfType(args[i])).All(v => v));
            return method == null ? null : method.Invoke(target is Type ? null : target, args);
        }
        internal static IEnumerable<object> Items(object value)
        {
            IEnumerable list = value as IEnumerable;
            if (list == null || value is string) yield break;
            foreach (object item in list) if (item != null) yield return item;
        }
        internal static object Service(string name)
        {
            Type type = Type(name), repository = Type("ServiceRepository");
            return type == null || repository == null ? null : Call(repository, "GetService", type);
        }
        internal static string Text(object value) { return value == null ? "?" : value.ToString(); }
        internal static string Plain(string value)
        {
            return Regex.Replace(value ?? "", "<[^>]*>", "").Replace("\\n", "\n");
        }
        internal static string Localize(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            Type gui = Type("Gui");
            if (gui == null) return Plain(value);
            MethodInfo method = gui.GetMethods(Flags).FirstOrDefault(m => m.Name == "Localize" &&
                m.ReturnType == typeof(string) && m.GetParameters().Length == 4 &&
                m.GetParameters()[0].ParameterType == typeof(string) &&
                m.GetParameters()[1].ParameterType == typeof(bool));
            try { return Plain(method == null ? value : (string)method.Invoke(null, new object[] { value, true, null, null })); }
            catch { return Plain(value); }
        }
        internal static string Title(object definition)
        {
            object presentation = Get(definition, "GuiPresentation");
            string key = Get(presentation, "Title") as string;
            return String.IsNullOrEmpty(key) ? Text(Get(definition, "Name")) : Localize(key);
        }
        internal static string Description(object definition)
        {
            return Localize(Get(Get(definition, "GuiPresentation"), "Description") as string);
        }
        internal static string Name(object character)
        {
            object ruleset = Get(character, "RulesetCharacter") ?? character;
            string first = Text(Get(ruleset, "DisplayName") ?? Get(ruleset, "Name"));
            string surname = Get(ruleset, "DisplaySurName") as string;
            return Plain(first + (String.IsNullOrEmpty(surname) ? "" : " " + surname));
        }
        internal static bool Same(object a, object b)
        {
            if (ReferenceEquals(a, b)) return a != null;
            object ga = Get(Get(a, "RulesetCharacter"), "Guid");
            object gb = Get(Get(b, "RulesetCharacter"), "Guid");
            return ga != null && gb != null && ga.Equals(gb);
        }
    }

    internal sealed class Row
    {
        internal string Title, Details;
        internal Row(string title, string details) { Title = title; Details = details; }
    }

    public sealed class CoopView : MonoBehaviour
    {
        internal static CoopView Instance;
        private Rect window = new Rect(25, 60, 590, 600);
        private bool visible = true, follow = true;
        private int tab, selected;
        private float preferredCameraZoom = 1.4f;
        private float preferredCombatSpeed = 2f;
        private float nextRefresh;
        private Vector2 scroll;
        private string heading = "Load a save or start a new game.", error = "", details = "";
        private object selectedCharacter;
        private List<object> party = new List<object>();
        private readonly List<Row>[] rows = { new List<Row>(), new List<Row>(), new List<Row>() };
        private GUIStyle textStyle, titleStyle;
        private GUIStyle rangeStyle;
        private bool showHoverRange = true;
        private float nextRangeRefresh;
        private float nextFollowGuiError;
        private RangeSnapshot hoverRange;
        private string lastRangeError = "";
        private readonly PhotonTransport transport = new PhotonTransport();
        private readonly RuntimeDiagnostics diagnostics = new RuntimeDiagnostics();
        private readonly ObserverHud observerHud = new ObserverHud();
        private readonly TravelShare travelShare = new TravelShare();
        private readonly LootShare lootShare = new LootShare();
        private readonly PartyAutoFollow autoFollow = new PartyAutoFollow();
        private HostMoveShare hostMoveShare;
        private readonly AutoFastTravelVote autoFastTravelVote = new AutoFastTravelVote();
        private readonly AutoLoadingProceed autoLoadingProceed = new AutoLoadingProceed();
        private static volatile bool gameCharacterMappingError;
        private string networkMessage = "Choose the transport for the next connection.";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Application.logMessageReceivedThreaded += UnityLog;
            DetailedTooltips.SetEnabled(true);
            CameraZoom.SetFactor(1.4f);
            ItemTransferRange.Install();
            try { ConnectionRetry.Install(); }
            catch (Exception e) { Log.Error("network", "Install connection retries", e); }
            observerHud.SetEnabled(true);
            lootShare.SetEnabled(true);
            autoFastTravelVote.Install();
            autoLoadingProceed.Install();
            hostMoveShare = new HostMoveShare(autoFollow);
            try { diagnostics.Start(); }
            catch (Exception e) { Log.Error("core", "Start diagnostics", e); }
            Log.Write("CoopMod " + BuildInfo.Version + " ready; F7=range, F8=panel, F9=verbose, F10=report.");
        }
        private void OnDestroy()
        {
            if (Instance != this) return;
            Application.logMessageReceivedThreaded -= UnityLog;
            diagnostics.Dispose();
            observerHud.Dispose();
            travelShare.Dispose();
            lootShare.Dispose();
            autoFollow.SetEnabled(false);
            if (hostMoveShare != null) hostMoveShare.Dispose();
            autoFastTravelVote.Dispose();
            autoLoadingProceed.Dispose();
            ConnectionRetry.Cancel("Game closed.");
            CameraZoom.RestoreAll();
            CombatSpeed.RestoreAll();
            Log.Write("Overlay destroyed.");
            Instance = null;
        }
        private void OnApplicationQuit() { Log.Write("Application quitting."); }
        private void OnApplicationFocus(bool focused) { Log.Write("Application focus=" + focused); }
        private void OnApplicationPause(bool paused) { Log.Write("Application paused=" + paused); }
        private static void UnityLog(string condition, string stack, LogType type)
        {
            string lower = (condition ?? "").ToLowerInvariant();
            bool related = lower.Contains("photon") || lower.Contains("socket") || lower.Contains("network") ||
                lower.Contains("disconnect") || lower.Contains("timeout") || lower.Contains("coopmod") || lower.Contains("doorstop");
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                if (lower.Contains("couldn't recover character used in action")) gameCharacterMappingError = true;
                Log.UnityMessage("ERROR", condition, stack);
            }
            else if (type == LogType.Warning && related) Log.UnityMessage("WARN", condition, stack);
            else if (Log.Verbose && related) Log.UnityMessage("DEBUG", condition, "");
        }
        private void LateUpdate()
        {
            if (gameCharacterMappingError)
            {
                gameCharacterMappingError = false;
                if (observerHud.Enabled)
                {
                    observerHud.SetEnabled(false);
                    Log.Event("core", "WARN", "Game character action mapping failed: observer HUD disabled as a precaution. Reload a host save to recover battle state.");
                }
            }
            observerHud.Tick();
            diagnostics.HudStatus = observerHud.DiagnosticSummary;
            if (Input.GetKeyDown(KeyCode.F7)) SetRangeEnabled(!showHoverRange);
            if (!showHoverRange) { hoverRange = null; diagnostics.ObserveRange(null, "disabled-by-user"); return; }
            if (Time.unscaledTime < nextRangeRefresh) return;
            nextRangeRefresh = Time.unscaledTime + 0.1f;
            hoverRange = null;
            try
            {
                hoverRange = HoverRange.Capture();
                diagnostics.ObserveRange(hoverRange, HoverRange.LastStatus);
                if (lastRangeError.Length > 0) Log.Event("range", "INFO", "Range sampling recovered.");
                lastRangeError = "";
            }
            catch (Exception e)
            {
                string message = e.GetBaseException().Message;
                if (message != lastRangeError) Log.Error("range", "Range tooltip", e);
                lastRangeError = message;
                diagnostics.ObserveRange(null, "capture-error:" + message);
            }
        }
        private void Update()
        {
            CameraZoom.Tick();
            CombatSpeed.Tick();
            autoFastTravelVote.Tick();
            if (hostMoveShare != null) hostMoveShare.Tick();
            if (!autoFastTravelVote.VotePending) autoFollow.Tick();
            ConnectionRetry.Tick();
            travelShare.Tick();
            lootShare.Tick();
            if (travelShare.ShouldCapture) StartCoroutine(travelShare.Capture());
            if (Input.GetKeyDown(KeyCode.F6)) travelShare.TogglePreview();
            diagnostics.TravelStatus = travelShare.DiagnosticSummary;
            diagnostics.LootStatus = lootShare.Diagnostics;
            if (Input.GetKeyDown(KeyCode.F8)) SetVisible(!visible);
            if (Input.GetKeyDown(KeyCode.F9)) diagnostics.SetVerbose(!Log.Verbose);
            if (Input.GetKeyDown(KeyCode.F10)) diagnostics.SaveReport();
            diagnostics.Tick();
            if (!visible || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.5f;
            try { Refresh(); error = ""; }
            catch (Exception e)
            {
                string next = e.GetBaseException().Message;
                if (error != next) Log.Error("core", "Refresh panel", e);
                error = next;
            }
        }
        private void SetVisible(bool enabled)
        {
            if (visible != enabled) Log.Write("Panel visible=" + enabled);
            visible = enabled;
        }
        private void SetRangeEnabled(bool enabled)
        {
            if (showHoverRange != enabled) Log.Event("range", "INFO", "Tooltip enabled=" + enabled);
            showHoverRange = enabled;
        }
        private void Refresh()
        {
            object service = Read.Service("IGameLocationCharacterService");
            party = Read.Items(Read.Get(service, "PartyCharacters")).ToList();
            if (party.Count == 0)
            {
                heading = "Load a save or start a new game.";
                selectedCharacter = null;
                foreach (List<Row> list in rows) list.Clear();
                return;
            }
            object active = Read.Get(Read.Get(Read.Type("Gui"), "Battle"), "ActiveContender");
            if (follow && active != null)
            {
                int index = party.FindIndex(p => Read.Same(p, active));
                if (index >= 0) selected = index;
            }
            selected = Math.Max(0, Math.Min(selected, party.Count - 1));
            selectedCharacter = party[selected];
            object ruleset = Read.Get(selectedCharacter, "RulesetCharacter");
            bool isActive = Read.Same(active, selectedCharacter);
            heading = Read.Name(selectedCharacter) + (isActive ? "  [active turn]" : "");
            heading += "\nHP: " + Read.Text(Read.Get(ruleset, "CurrentHitPoints")) +
                "    temporary HP: " + Read.Text(Read.Get(ruleset, "TemporaryHitPoints"));
            foreach (List<Row> list in rows) list.Clear();
            BuildActions(selectedCharacter, ruleset);
            BuildSpells(ruleset);
            BuildPowers(ruleset);
        }
        private void BuildActions(object character, object ruleset)
        {
            IDictionary definitions = Read.Get(Read.Service("IGameLocationActionService"), "AllActionDefinitions") as IDictionary;
            bool inBattle = Read.Get(Read.Type("Gui"), "Battle") != null;
            MethodInfo method = character.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetActionStatus" && m.GetParameters().Length == 6);
            if (method != null && definitions != null && inBattle)
            {
                string[] names = { "AttackMain", "AttackOff", "CastMain", "CastBonus", "PowerMain",
                    "PowerBonus", "DashMain", "DashBonus", "DisengageMain", "DisengageBonus", "Dodge",
                    "Ready", "Shove", "HideMain", "HideBonus", "UseItemMain", "UseItemBonus", "StandUp" };
                ParameterInfo[] parameters = method.GetParameters();
                foreach (string name in names)
                {
                    if (!Enum.IsDefined(parameters[0].ParameterType, name)) continue;
                    object[] args = { Enum.Parse(parameters[0].ParameterType, name),
                        Enum.Parse(parameters[1].ParameterType, "Battle"),
                        Enum.Parse(parameters[2].ParameterType, "Irrelevant"), null, false, false };
                    if (!definitions.Contains(args[0])) continue;
                    object status = method.Invoke(character, args);
                    object definition = definitions[args[0]];
                    string label = Read.Title(definition);
                    rows[0].Add(new Row(label + "  [" + Read.Text(status) + "]",
                        Read.Description(definition) + "\n\nStatus from your local game state. " +
                        "This panel does not validate targeting or all usage restrictions."));
                }
            }
            foreach (object attack in Read.Items(Read.Get(ruleset, "AttackModes")))
            {
                object source = Read.Get(attack, "SourceDefinition");
                string title = source == null ? "Attack" : Read.Title(source);
                rows[0].Add(new Row(title + "  [" + Read.Text(Read.Get(attack, "ActionType")) + "]",
                    "Attack bonus: " + Read.Text(Read.Get(attack, "ToHitBonus")) +
                    "\nAttacks: " + Read.Text(Read.Get(attack, "AttacksNumber")) +
                    "\nClose / maximum range: " + Read.Text(Read.Get(attack, "CloseRange")) +
                    " / " + Read.Text(Read.Get(attack, "MaxRange"))));
            }
        }
        private void BuildSpells(object ruleset)
        {
            foreach (object repertoire in Read.Items(Read.Get(ruleset, "SpellRepertoires")))
            {
                foreach (object spell in Read.Items(Read.Get(repertoire, "KnownCantrips"))
                    .Concat(Read.Items(Read.Get(repertoire, "KnownSpells")))
                    .Concat(Read.Items(Read.Get(repertoire, "PreparedSpells")))
                    .Concat(Read.Items(Read.Get(repertoire, "AutoPreparedSpells"))).Distinct())
                {
                    object ready = Convert.ToInt32(Read.Get(spell, "SpellLevel") ?? 0) == 0 ||
                        Read.Items(Read.Get(repertoire, "AutoPreparedSpells")).Contains(spell)
                        ? (object)true : Read.Call(repertoire, "IsSpellReady", spell);
                    string state = ready is bool && !(bool)ready ? " [not prepared]" : "";
                    string title = Read.Title(spell) + " [" + Read.Text(Read.Get(spell, "SpellLevel")) + "]" + state;
                    string body = Read.Description(spell) + "\n\nCasting time: " + Read.Text(Read.Get(spell, "CastingTime")) +
                        "\nSpell attack bonus: " + Read.Text(Read.Get(repertoire, "SpellAttackBonus")) +
                        "; save DC: " + Read.Text(Read.Get(repertoire, "SaveDC"));
                    rows[1].Add(new Row(title, body));
                }
                MethodInfo slots = repertoire.GetType().GetMethod("GetSlotsNumber");
                if (slots != null)
                {
                    List<string> summary = new List<string>();
                    for (int level = 1; level <= 9; level++)
                    {
                        object[] args = { level, 0, 0 };
                        slots.Invoke(repertoire, args);
                        if (Convert.ToInt32(args[2]) > 0) summary.Add(level + ": " + args[1] + "/" + args[2]);
                    }
                    if (summary.Count > 0) rows[1].Insert(0, new Row("Slots: " + String.Join("; ", summary.ToArray()),
                        "Spell level: remaining / maximum. Data from the local game state."));
                }
            }
        }
        private void BuildPowers(object ruleset)
        {
            foreach (object power in Read.Items(Read.Get(ruleset, "UsablePowers")))
            {
                object definition = Read.Get(power, "PowerDefinition");
                if (definition == null) continue;
                object presentation = Read.Get(definition, "GuiPresentation");
                if (Read.Get(presentation, "Hidden") is bool && (bool)Read.Get(presentation, "Hidden")) continue;
                rows[2].Add(new Row(Read.Title(definition) + " [" + Read.Text(Read.Get(power, "RemainingUses")) +
                    "/" + Read.Text(Read.Get(power, "MaxUses")) + "]",
                    Read.Description(definition) + "\n\nActivation: " + Read.Text(Read.Get(definition, "ActivationTime"))));
            }
        }
        private void OnGUI()
        {
            if (TravelShare.Capturing) return;
            travelShare.Draw();
            if (textStyle == null)
            {
                textStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 14, richText = false };
                titleStyle = new GUIStyle(textStyle) { fontStyle = FontStyle.Bold, fontSize = 16 };
                rangeStyle = new GUIStyle(GUI.skin.box) { wordWrap = true, fontSize = 15,
                    alignment = TextAnchor.UpperLeft, richText = false, padding = new RectOffset(10, 10, 8, 8) };
            }
            DrawHoverRange();
            DrawAutoFollowButton();
            if (!visible) return;
            window.width = Math.Min(590, Screen.width - 10);
            window.height = Math.Min(650, Screen.height - 10);
            window.x = Mathf.Clamp(window.x, 0, Math.Max(0, Screen.width - window.width));
            window.y = Mathf.Clamp(window.y, 0, Math.Max(0, Screen.height - window.height));
            window = GUI.Window(487201, window, DrawWindow, "Solasta Coop Mod " + BuildInfo.Version + "  |  F8");
        }
        private void DrawAutoFollowButton()
        {
            bool previous = GUI.enabled;
            try
            {
                if (Read.Get(Read.Type("Gui"), "GameLocation") == null) return;
                Rect rect = new Rect(Math.Max(8, Screen.width - 238), 14, 228, 34);
                bool ready = Read.Get(Read.Type("Gui"), "Battle") == null;
                GUI.enabled = previous && ready;
                string label = autoFollow.Enabled ? "Auto-follow: ON" : "Auto-follow: OFF";
                if (GUI.Button(rect, label))
                    autoFollow.SetEnabled(!autoFollow.Enabled);
            }
            catch (Exception ex)
            {
                if (Time.unscaledTime > nextFollowGuiError)
                {
                    nextFollowGuiError = Time.unscaledTime + 15f;
                    Log.Error("core", "Party auto-follow button", ex);
                }
            }
            finally { GUI.enabled = previous; }
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            bool newFollow = GUILayout.Toggle(follow, "Follow active teammate turns");
            if (newFollow != follow) { follow = newFollow; Log.Write("Follow active ally=" + follow); }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Hide", GUILayout.Width(70))) SetVisible(false);
            GUILayout.EndHorizontal();
            bool nativeHud = GUILayout.Toggle(observerHud.Enabled, "Show teammate action panel (read-only)");
            if (nativeHud != observerHud.Enabled) observerHud.SetEnabled(nativeHud);
            bool expanded = GUILayout.Toggle(DetailedTooltips.Enabled, "Detailed tooltips without Alt");
            if (expanded != DetailedTooltips.Enabled) DetailedTooltips.SetEnabled(expanded);
            SetRangeEnabled(GUILayout.Toggle(showHoverRange, "Spell and attack range on hover [F7]"));
            GUILayout.Label(heading, titleStyle);
            if (party.Count > 0)
            {
                int next = GUILayout.SelectionGrid(selected, party.Select(Read.Name).ToArray(), Math.Min(3, party.Count));
                if (next != selected)
                {
                    selected = next; follow = false; details = ""; nextRefresh = 0;
                    autoFollow.SelectTarget(party[selected]);
                }
            }
            int newTab = GUILayout.SelectionGrid(tab, new[] { "Actions", "Spells", "Abilities", "Network", "Logs", "Camera", "Travel", "Loot", "Transfer", "Combat" }, 5);
            if (newTab != tab) { tab = newTab; details = ""; scroll = Vector2.zero; }
            if (!String.IsNullOrEmpty(error)) GUILayout.Label("Read error: " + error, textStyle);
            if (tab == 3) DrawNetwork();
            else if (tab == 4) DrawDiagnostics();
            else if (tab == 5) DrawCamera();
            else if (tab == 6) DrawTravel();
            else if (tab == 7) DrawLoot();
            else if (tab == 8) DrawTransfer();
            else if (tab == 9) DrawCombatSpeed();
            else
            {
                GUILayout.Label("Teammate actions and abilities (read-only).", textStyle);
                scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Math.Max(80, window.height - 420)));
                foreach (Row row in rows[tab])
                    if (GUILayout.Button(row.Title)) details = row.Details;
                if (rows[tab].Count == 0) GUILayout.Label("No data available yet.", textStyle);
                GUILayout.EndScrollView();
                GUILayout.Label(details, textStyle, GUILayout.MaxHeight(110));
            }
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, window.width - 20, 22));
            if (Event.current.type == EventType.MouseDown &&
                new Rect(0, 0, window.width, window.height).Contains(Event.current.mousePosition)) Event.current.Use();
        }

        private void DrawNetwork()
        {
            try
            {
                Type photon = Read.Type("Photon.Pun.PhotonNetwork");
                object client = Read.Get(photon, "NetworkingClient");
                object peer = Read.Get(client, "LoadBalancingPeer");
                object settings = Read.Get(Read.Get(photon, "PhotonServerSettings"), "AppSettings");
                string state = Read.Text(Read.Get(photon, "NetworkClientState"));
                bool connected = Read.Get(photon, "IsConnected") is bool &&
                    (bool)Read.Get(photon, "IsConnected");

                GUILayout.Label("Photon: " + state + "  |  Transport: " +
                    Read.Text(Read.Get(peer, "TransportProtocol")) + "  |  Region: " +
                    Read.Text(Read.Get(photon, "CloudRegion")), textStyle);
                bool previous = GUI.enabled;
                try
                {
                    GUI.enabled = previous && settings != null;
                    bool tcp = transport.TcpSelected(settings);
                    bool nextTcp = GUILayout.Toggle(tcp, "Use TCP for next connection");
                    if (nextTcp != tcp) ChangeProtocol(settings, state, connected, !nextTcp);
                }
                finally { GUI.enabled = previous; }
                GUILayout.Label("Transport changes after reconnecting to multiplayer.", textStyle);
                GUILayout.Label(networkMessage, textStyle);

                bool retry = GUILayout.Toggle(ConnectionRetry.Enabled, "Retry failed room joins (up to 3)");
                if (retry != ConnectionRetry.Enabled) ConnectionRetry.SetEnabled(retry);
                GUILayout.Label(ConnectionRetry.Status, textStyle);
                if (ConnectionRetry.NeedsAcknowledgement &&
                    GUILayout.Button("Photon error dialog closed, retry joining"))
                    ConnectionRetry.ConfirmErrorDismissed();
                if (GUILayout.Button("Cancel retries")) ConnectionRetry.Cancel("Cancelled by user.");
            }
            catch (Exception e)
            {
                GUILayout.Label("Network unavailable: " + e.GetBaseException().Message, textStyle);
            }
        }

        private void ChangeProtocol(object settings, string state, bool connected, bool restore)
        {
            Log.Event("network", "INFO", "Transport selection requested; restore=" + restore + "; state=" + state +
                "; connected=" + connected + "; previous=" + Read.Text(Read.Get(settings, "Protocol")));
            try
            {
                networkMessage = transport.Change(settings, state, connected, restore);
                Log.Event("network", "INFO", networkMessage);
            }
            catch (Exception e)
            {
                networkMessage = e.GetBaseException().Message;
                Log.Error("network", "Transport selection failed", e);
            }
            diagnostics.Tick(true);
        }

        private void DrawTravel()
        {
            bool enabled = GUILayout.Toggle(travelShare.Enabled, "Share host travel screen and cursors");
            if (enabled != travelShare.Enabled) travelShare.SetEnabled(enabled);
            GUILayout.Label(travelShare.Status, textStyle);
            GUILayout.Label("Travel preview: up to 1 FPS. No remote clicks.", textStyle);
            if (GUILayout.Button("Show / hide travel preview [F6]")) travelShare.TogglePreview();

            GUILayout.Space(8);
            bool acceptFastTravel = GUILayout.Toggle(autoFastTravelVote.Enabled,
                "Auto-accept fast-travel votes");
            if (acceptFastTravel != autoFastTravelVote.Enabled)
                autoFastTravelVote.SetEnabled(acceptFastTravel);
            GUILayout.Label(autoFastTravelVote.Status, textStyle);

            GUILayout.Space(8);
            bool acceptLoading = GUILayout.Toggle(autoLoadingProceed.Enabled,
                "Auto-continue after loading a save or location");
            if (acceptLoading != autoLoadingProceed.Enabled)
                autoLoadingProceed.SetEnabled(acceptLoading);
            GUILayout.Label(autoLoadingProceed.Status, textStyle);
            GUILayout.Label("Confirms only the ready loading screen, not dialogs or other votes.", textStyle);
        }

        private void DrawLoot()
        {
            bool enabled = GUILayout.Toggle(lootShare.Enabled, "Share interactive chest, corpse and ground loot");
            if (enabled != lootShare.Enabled) lootShare.SetEnabled(enabled);
            GUILayout.Label(lootShare.Status, textStyle);
            GUILayout.Label("Players loot with their own heroes; game-side range rules may still apply.", textStyle);
            if (GUILayout.Button("Close shared loot window")) lootShare.ClosePreview();
        }

        private void DrawDiagnostics()
        {
            bool verbose = GUILayout.Toggle(Log.Verbose, "Verbose logging [F9]");
            if (verbose != Log.Verbose) diagnostics.SetVerbose(verbose);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save diagnostic report [F10]")) diagnostics.SaveReport();
            if (GUILayout.Button("Run checks again"))
            {
                try { diagnostics.RunChecks(); }
                catch (Exception e) { Log.Error("core", "Manual API checks", e); }
            }
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Math.Max(100, window.height - 350)));
            GUILayout.Label("Logs: " + Log.DirectoryPath, textStyle);
            if (Log.Writer.FailedWrites > 0)
                GUILayout.Label("Log write errors: " + Log.Writer.FailedWrites + "\n" + Log.Writer.LastError, textStyle);
            GUILayout.Label(diagnostics.ReportMessage, textStyle);
            GUILayout.Label(observerHud.Status + "\n" + lootShare.Diagnostics, textStyle);
            GUILayout.Label(DetailedTooltips.Status, textStyle);
            GUILayout.Label("Network\n" + diagnostics.LatestNetwork + "\n\nRange\n" +
                diagnostics.LatestRange + "\n\n" + diagnostics.Checks, textStyle);
            GUILayout.EndScrollView();
        }

        private void DrawTransfer()
        {
            bool unlimited = GUILayout.Toggle(ItemTransferRange.Enabled,
                "Allow item transfers beyond 6 cells");
            if (unlimited != ItemTransferRange.Enabled) ItemTransferRange.SetEnabled(unlimited);
            GUILayout.Label(ItemTransferRange.Status, textStyle);
            GUILayout.Label("Extends recipient selection in inventory; server rules may still apply.", textStyle);

            GUILayout.Space(8);
            bool next = GUILayout.Toggle(autoFollow.Enabled, "Auto-follow teammate during exploration");
            if (next != autoFollow.Enabled) autoFollow.SetEnabled(next);
            GUILayout.Label(autoFollow.Status, textStyle);
            if (GUILayout.Button("Auto-select moving teammate"))
                autoFollow.SelectAutomaticLeader();
            GUILayout.Label("Select a specific leader in the character list above.", textStyle);

            if (hostMoveShare != null)
            {
                GUILayout.Space(8);
                bool waypoints = GUILayout.Toggle(hostMoveShare.Enabled, "Share host movement destinations");
                if (waypoints != hostMoveShare.Enabled) hostMoveShare.SetEnabled(waypoints);
                GUILayout.Label("Sends host waypoints to followers via Photon.", textStyle);
            }
        }

        private void DrawCombatSpeed()
        {
            bool speedEnabled = CombatSpeed.Factor > 1f;
            bool nextEnabled = GUILayout.Toggle(speedEnabled, "Speed up combat animations");
            if (nextEnabled != speedEnabled)
                CombatSpeed.SetFactor(nextEnabled ? Math.Max(1.25f, preferredCombatSpeed) : 1f);
            if (CombatSpeed.Factor > 1f)
            {
                GUILayout.Label("Animation speed: ×" + CombatSpeed.Factor.ToString("0.00"), titleStyle);
                float factor = GUILayout.HorizontalSlider(CombatSpeed.Factor, 1.25f, 3f);
                if (Math.Abs(factor - CombatSpeed.Factor) > 0.125f)
                    CombatSpeed.SetFactor(factor);
                preferredCombatSpeed = CombatSpeed.Factor;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("×1.25")) CombatSpeed.SetFactor(1.25f);
                if (GUILayout.Button("×2")) CombatSpeed.SetFactor(2f);
                if (GUILayout.Button("×3")) CombatSpeed.SetFactor(3f);
                GUILayout.EndHorizontal();
                preferredCombatSpeed = CombatSpeed.Factor;
            }
            GUILayout.Label("Speeds up animations and VFX during combat; turn timers stay unchanged.", textStyle);
        }

        private void DrawCamera()
        {
            bool zoomEnabled = CameraZoom.Factor > 1f;
            bool nextEnabled = GUILayout.Toggle(zoomEnabled, "Extended camera zoom");
            if (nextEnabled != zoomEnabled)
                CameraZoom.SetFactor(nextEnabled ? Math.Max(1.1f, preferredCameraZoom) : 1f);
            if (CameraZoom.Factor > 1f)
            {
                GUILayout.Label("Maximum zoom-out: ×" + CameraZoom.Factor.ToString("0.0"), titleStyle);
                float factor = (float)Math.Round(
                    GUILayout.HorizontalSlider(CameraZoom.Factor, 1.1f, 3f), 1);
                if (Math.Abs(factor - CameraZoom.Factor) > 0.05f) CameraZoom.SetFactor(factor);
                preferredCameraZoom = CameraZoom.Factor;
            }
            GUILayout.Label("Extends zoom in exploration and combat; not cinematics or world map.", textStyle);
        }

        private void DrawHoverRange()
        {
            if (!showHoverRange || hoverRange == null) return;
            Vector2 pointer = Event.current.mousePosition;
            if (visible && window.Contains(pointer)) return;
            string text = "Distance: " + hoverRange.Distance.ToString("0.0") + " cells\nRange: " +
                Math.Round(hoverRange.Maximum).ToString("0") + " cells";
            float width = Math.Min(230, Screen.width - 10);
            float height = rangeStyle.CalcHeight(new GUIContent(text), width);
            float x = Mathf.Clamp(pointer.x + 24, 5, Math.Max(5, Screen.width - width - 5));
            float y = pointer.y + 24;
            if (y + height > Screen.height - 5) y = pointer.y - height - 15;
            y = Mathf.Clamp(y, 5, Math.Max(5, Screen.height - height - 5));
            int oldDepth = GUI.depth;
            Color oldColor = GUI.color, oldBackground = GUI.backgroundColor, oldContent = GUI.contentColor;
            bool oldEnabled = GUI.enabled;
            try
            {
                GUI.depth = -1000;
                GUI.enabled = true;
                GUI.color = Color.white;
                GUI.contentColor = Color.white;
                GUI.backgroundColor = Color.white;
                rangeStyle.normal.textColor = hoverRange.HighlightGreen ?
                    new Color(0.6f, 1f, 0.65f) : new Color(1f, 0.65f, 0.6f);
                GUI.Box(new Rect(x, y, width, height), text, rangeStyle);
            }
            finally
            {
                GUI.depth = oldDepth;
                GUI.color = oldColor; GUI.backgroundColor = oldBackground; GUI.contentColor = oldContent;
                GUI.enabled = oldEnabled;
            }
        }
    }
}
