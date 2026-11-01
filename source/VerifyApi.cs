using System;
using System.IO;
using System.Linq;
using System.Reflection;

public static class VerifyApi
{
    private static int failures;
    private static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static;
    private static void Check(bool value, string message)
    {
        Console.WriteLine((value ? "PASS " : "FAIL ") + message);
        if (!value) failures++;
    }
    public static int Main(string[] args)
    {
        string managed = Path.GetFullPath(args[0]);
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
        {
            string path = Path.Combine(managed, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Assembly game = Assembly.LoadFrom(Path.Combine(managed, "Assembly-CSharp.dll"));
        Func<string, Type> type = name => game.GetType(name, true);
        Check(type("Gui").GetProperty("Battle", Flags) != null, "Gui.Battle");
        Check(type("GameLocationBattle").GetProperty("ActiveContender", Flags) != null, "Battle.ActiveContender");
        Check(type("IGameLocationCharacterService").GetProperty("PartyCharacters", Flags) != null, "PartyCharacters");
        Check(type("IGameLocationActionService").GetProperty("AllActionDefinitions", Flags) != null,
            "Action service.AllActionDefinitions");
        Check(type("GameLocationCharacter").GetProperty("RulesetCharacter", Flags) != null,
            "GameLocationCharacter.RulesetCharacter");
        foreach (string name in new[] { "SpellRepertoires", "UsablePowers", "AttackModes", "CurrentHitPoints", "TemporaryHitPoints" })
            Check(type("RulesetCharacter").GetProperty(name, Flags) != null, "RulesetCharacter." + name);
        Check(type("ServiceRepository").GetMethods(Flags).Any(m => m.Name == "GetService" &&
            !m.IsGenericMethodDefinition && m.GetParameters().Length == 1 &&
            m.GetParameters()[0].ParameterType == typeof(Type)), "ServiceRepository.GetService(Type)");
        Check(type("GameLocationCharacter").GetMethods(Flags).Any(m => m.Name == "GetActionStatus" &&
            m.GetParameters().Length == 6), "GetActionStatus signature");
        Check(Enum.IsDefined(type("ActionDefinitions+ActionStatus"), "Irrelevant"), "ActionStatus.Irrelevant");
        Check(type("RulesetSpellRepertoire").GetMethod("GetSlotsNumber", Flags).GetParameters().Length == 3,
            "Spell slots signature");
        foreach (string name in new[] { "KnownCantrips", "KnownSpells", "PreparedSpells", "AutoPreparedSpells" })
            Check(type("RulesetSpellRepertoire").GetProperty(name, Flags) != null, "Spell repertoire." + name);
        foreach (string name in new[] { "PowerDefinition", "RemainingUses", "MaxUses" })
            Check(type("RulesetUsablePower").GetProperty(name, Flags) != null, "Power." + name);
        Assembly pun = Assembly.LoadFrom(Path.Combine(managed, "PhotonUnityNetworking.dll"));
        Type photon = pun.GetType("Photon.Pun.PhotonNetwork", true);
        foreach (string name in new[] { "NetworkingClient", "PhotonServerSettings", "IsConnected", "NetworkClientState", "CloudRegion" })
            Check(photon.GetProperty(name, Flags) != null || photon.GetField(name, Flags) != null, "Photon." + name);
        Assembly realtime = Assembly.LoadFrom(Path.Combine(managed, "PhotonRealtime.dll"));
        Type appSettings = realtime.GetType("Photon.Realtime.AppSettings", true);
        FieldInfo protocol = appSettings.GetField("Protocol", Flags);
        Check(protocol != null && Enum.IsDefined(protocol.FieldType, "Tcp"), "Photon TCP setting");
        Check(appSettings.GetField("EnableProtocolFallback", Flags).FieldType == typeof(bool), "Protocol fallback setting");
        Type clientType = realtime.GetType("Photon.Realtime.LoadBalancingClient", true);
        Check(clientType.GetEvent("StateChanged", Flags) != null, "Photon client.StateChanged event");
        Check(clientType.GetProperty("DisconnectedCause", Flags) != null, "Photon client.DisconnectedCause");
        Check(clientType.GetEvent("OpResponseReceived", Flags) != null, "Photon client.OpResponseReceived event");
        Check(type("NetworkingManager").GetMethod("OnConnected", Flags) != null, "Native per-server connection callback");
        foreach (string name in new[] { "CreateRoom", "JoinRoom", "OnDisconnected", "OnJoinedRoom", "OnCreateRoomFailed", "OnJoinRoomFailed" })
            Check(type("NetworkingManager").GetMethods(Flags).Count(m => m.Name == name) == 1, "Native retry hook " + name);
        Check(type("NetworkingManager").GetMethod("CreateRoom", Flags).GetParameters().Length == 1 &&
            type("NetworkingManager").GetMethod("JoinRoom", Flags).GetParameters().All(p => p.ParameterType == typeof(string)), "Native retry argument signatures");
        Check(type("NetworkingManager").GetMethod("OnDisconnected", Flags).GetParameters()[0].ParameterType.IsEnum, "Disconnect cause is an enum (boxed by Harmony)");
        Check(type("MenuSubpanel").GetMethod("OnCancelCb", Flags) != null &&
            type("MultiplayerSetupPanel").GetMethod("OnCancelCb", Flags).DeclaringType == type("MenuSubpanel"), "Multiplayer Cancel uses the patched native menu method");
        Check(clientType.GetEvent("EventReceived", Flags) != null, "Photon travel event subscription");
        var raiseEvent = photon.GetMethods(Flags).SingleOrDefault(m => m.Name == "RaiseEvent" && m.GetParameters().Length == 4);
        Check(raiseEvent != null && raiseEvent.GetParameters()[0].ParameterType == typeof(byte), "Photon RaiseEvent travel transport");
        Check(raiseEvent.GetParameters()[2].ParameterType.GetField("TargetActors", Flags) != null &&
            raiseEvent.GetParameters()[3].ParameterType.GetProperty("Reliability", Flags) != null, "Photon targeted sends and reliability flag");
        foreach (string name in new[] { "CurrentRoom", "MasterClient", "IsMasterClient", "InRoom", "OfflineMode" })
            Check(photon.GetProperty(name, Flags) != null, "Photon travel room property " + name);
        Check(realtime.GetType("Photon.Realtime.Room", true).GetProperty("Players", Flags) != null &&
            realtime.GetType("Photon.Realtime.Player", true).GetProperty("ActorNumber", Flags) != null, "Photon room member validation");
        foreach (string name in new[] { "GameCampaignScreen", "TravelPreparationModal", "TravelSettingsModal" })
            Check(type(name).GetProperty("Visible", Flags) != null && type(name).GetProperty("Hiding", Flags) != null, "Travel UI visibility " + name);
        Assembly peerAssembly = Assembly.LoadFrom(Path.Combine(managed, "Photon3Unity3D.dll"));
        Type peerType = peerAssembly.GetType("ExitGames.Client.Photon.PhotonPeer", true);
        foreach (string name in new[] { "RoundTripTime", "RoundTripTimeVariance", "ResentReliableCommands", "QueuedIncomingCommands", "QueuedOutgoingCommands" })
            Check(peerType.GetProperty(name, Flags) != null, "Photon peer." + name);
        foreach (string name in new[] { "DisconnectTimeout", "BytesIn", "BytesOut", "ServerAddress", "PeerState", "SocketImplementation" })
            Check(peerType.GetProperty(name, Flags) != null, "Photon peer." + name);
        Check(type("ICursorService").GetProperty("CurrentCursor", Flags) != null, "Cursor service.CurrentCursor");
        Check(type("CursorLocation").GetProperty("ActionParams", Flags) != null, "Targeting cursor.ActionParams");
        Check(type("CursorLocation").GetProperty("SelectionService", Flags) != null, "Targeting cursor.SelectionService");
        foreach (string name in new[] { "ActingCharacter", "RulesetEffect", "AttackMode" })
            Check(type("CharacterActionParams").GetProperty(name, Flags) != null, "Action params." + name);
        Check(type("IGameLocationSelectionService").GetProperty("HoveredCharacters", Flags) != null,
            "Selection service.HoveredCharacters");
        Check(type("GameLocationCharacter").GetProperty("LocationPosition", Flags) != null, "Character.LocationPosition");
        Check(type("RulesetEffectSpell").GetProperty("SpellDefinition", Flags) != null, "Spell effect.SpellDefinition");
        Type evaluation = type("BattleDefinitions+AttackEvaluationParams");
        Check(evaluation.GetMethods(Flags).Any(m => m.Name == "FillForMagicDistance" && m.GetParameters().Length == 8),
            "Native magic distance evaluation");
        foreach (string method in new[] { "FillForPhysicalRangeAttack", "FillForPhysicalReachAttack" })
            Check(evaluation.GetMethods(Flags).Any(m => m.Name == method && m.GetParameters().Length == 6), method);
        Check(type("RulesetAttackMode").GetProperty("Ranged", Flags) != null, "Attack mode.Ranged");
        Check(type("IGameLocationBattleService").GetMethods(Flags).Any(m => m.Name == "IsWithinXCells" &&
            m.GetParameters().Length == 5), "Native melee reach cell check");
        Check(type("IGameLocationBattleService").GetMethods(Flags).Any(m => m.Name == "CanAttackerSeeCharacterFromPosition" &&
            m.GetParameters().Length == 4), "Native caster-to-target line-of-sight check");
        Type scope = type("TooltipDefinitions+Scope");
        Check(scope.IsEnum && Enum.IsDefined(scope, "Simplified") && Enum.IsDefined(scope, "Detailed") &&
            Enum.IsDefined(scope, "All"), "Native tooltip scope names");
        Check(type("TooltipPanel").GetMethods(Flags).Count(m => m.Name == "SetupFeatures" &&
            m.ReturnType == typeof(void) && m.GetParameters().Length == 3 &&
            m.GetParameters()[0].ParameterType == scope) == 1, "Native tooltip feature scope selection");
        foreach (string spec in new[] { "CharacterControlPanel|HandleInput", "CharacterControlPanelBattle|HandleInput",
            "CharacterActionPanel|HandleInput", "CharacterControlPanelBattle|OnEndTurnCb", "CharacterControlPanelBattle|OnUpdateHook",
            "CharacterActionPanel|OnActivateAction", "CharacterActionPanel|RestoreDefaultCursor", "WieldedConfigurationSelector|OnToggleCb",
            "CharacterActionPanel|OnPointerEnter", "CharacterActionPanel|OnPointerExit", "CharacterActionPanel|SpellcastEngaged",
            "CharacterActionPanel|SpellCastConfirmed", "CharacterActionPanel|PowerEngaged", "CharacterActionPanel|ExecuteEffectOfAction",
            "CharacterControlPanel|OnInspectCb", "CharacterControlPanel|RestoreDefaultCursor",
            "ActiveCharacterPanel|OnStopConcentratingCb", "ActiveCharacterPanel|OnPreviousCharacterCb",
            "ActiveCharacterPanel|OnNextCharacterCb", "ActiveCharacterPanel|OnGamepadShortcutsCb", "InventorySlotBox|OnSwitchAmmunitionCb" })
        {
            string[] pieces = spec.Split('|');
            Check(type(pieces[0]).GetMethods(Flags).Any(m => m.DeclaringType == type(pieces[0]) && m.Name == pieces[1]),
                "Observer input guard target " + spec);
        }
        Check(type("GuiPanel").GetProperty("CanvasGroup", Flags) != null, "Native HUD mouse-interaction guard");
        Check(type("CharacterControlPanelBattle").GetMethods(Flags).Any(m => m.Name == "Bind" && m.GetParameters().Length == 2),
            "Native HUD character binding");
        Check(type("CharacterActionItemForm").GetMethod("Refresh", Flags) != null &&
            type("CharacterActionItemForm").GetProperty("Button", Flags) != null, "Native action category preview button");
        Check(type("SpellSelectionPanel").GetMethods(Flags).Any(m => m.Name == "Bind" && m.GetParameters().Length == 5) &&
            type("SpellSelectionPanel").GetProperty("SpellcastCancelled", Flags) != null, "Native spell preview binding and callback");
        Check(type("PowerSelectionPanel").GetMethods(Flags).Any(m => m.Name == "Bind" && m.GetParameters().Length == 3) &&
            type("PowerSelectionPanel").GetProperty("PowerCancelled", Flags) != null, "Native power preview binding and callback");
        Check(type("IGuiService").GetMethods(Flags).Any(m => m.Name == "GetScreen" && !m.IsGenericMethodDefinition &&
            m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Type)), "Native battle screen lookup");
        Check(type("IGuiService").GetProperty("GrabbedCameraControls", Flags) != null, "Observer modal-interface guard");
        Check(type("CameraModeManual").GetMethod("AdjustOrbit", Flags) != null, "Native camera orbit update");
        Check(type("CameraModeManual").GetProperty("FreeLookCamera", Flags) != null, "Native FreeLook camera reference");
        Check(type("ICameraService").GetProperty("CurrentCameraController", Flags) != null, "Current camera controller lookup");
        Assembly cinemachine = Assembly.LoadFrom(Path.Combine(managed, "Cinemachine.dll"));
        Type freeLook = cinemachine.GetType("Cinemachine.CinemachineFreeLook", true);
        FieldInfo orbits = freeLook.GetField("m_Orbits", Flags);
        Check(orbits != null && orbits.FieldType.IsArray && orbits.FieldType.GetElementType().GetField("m_Radius", Flags) != null &&
            orbits.FieldType.GetElementType().GetField("m_Height", Flags) != null, "Cinemachine orbit radius and height fields");
        foreach (string name in new[] { "distance", "maxRange" })
            Check(evaluation.GetField(name, Flags) != null, "Native range evaluation." + name);
        foreach (string name in new[] { "RangeHit", "Distance" })
            Check(Enum.IsDefined(type("RuleDefinitions+RangeType"), name), "Spell range type." + name);
        Assembly core = Assembly.LoadFrom(Path.Combine(managed, "UnityEngine.CoreModule.dll"));
        Check(core.GetType("UnityEngine.SceneManagement.SceneManager", true).GetEvent("sceneLoaded", Flags) != null,
            "Scene bootstrap event");
        Check(core.GetType("UnityEngine.Application", true).GetEvent("logMessageReceivedThreaded", Flags) != null,
            "Unity threaded console event");
        Assembly capture = Assembly.LoadFrom(Path.Combine(managed, "UnityEngine.ScreenCaptureModule.dll"));
        Assembly conversion = Assembly.LoadFrom(Path.Combine(managed, "UnityEngine.ImageConversionModule.dll"));
        Check(capture.GetType("UnityEngine.ScreenCapture", true).GetMethods(Flags).Any(m => m.Name == "CaptureScreenshotAsTexture" && m.GetParameters().Length == 0), "Unity captures the game frame as a texture");
        foreach (string name in new[] { "EncodeToJPG", "LoadImage" })
            Check(conversion.GetType("UnityEngine.ImageConversion", true).GetMethods(Flags).Any(m => m.Name == name), "Unity image conversion " + name);
        Assembly ui = Assembly.LoadFrom(Path.Combine(managed, "UnityEngine.UI.dll"));
        Check(ui.GetType("UnityEngine.UI.Image", true).GetProperty("raycastTarget", Flags) != null &&
            ui.GetType("UnityEngine.UI.GraphicRaycaster", true) != null, "Transparent input shield blocks native UI raycasts beneath the travel preview");
        Console.WriteLine("API failures: " + failures);
        return failures == 0 ? 0 : 1;
    }
}
