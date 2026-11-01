using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SolastaCoop
{
    // Share native loot *source identifiers*, not images or serialized inventory.
    // The receiver opens a real local modal, selects one of their own characters,
    // and performs all looting using Solasta's original gameplay command methods.
    internal sealed class LootShare : IDisposable
    {
        private const byte EventCode = 191;
        private const string Magic = "SolastaCoopView.native-loot/2";
        private const int OpenContainer = 1;
        private const int OpenProximity = 2;
        private const int Close = 3;

        private sealed class Message
        {
            internal int Sender;
            internal object[] Data;
        }

        private readonly object sync = new object();
        private readonly Queue<Message> inbox = new Queue<Message>();
        private static LootShare instance;
        private object client, room;
        private Type photon, optionType, sendType;
        private EventInfo receivedEvent;
        private Delegate receivedHandler;
        private MethodInfo sendEvent;
        private bool online, hooksInstalled, ignoreNative;
        private int localKind, remoteSender, sequence, incomingSequence;
        private string localHero, localContainer;
        private DateTime openedAt;
        private object remoteModal;
        private object remoteSourceCharacter;
        private bool previousSlotsDisabled;
        private int openingRemoteKind;
        private object openingRemoteSource;
        private Message pendingOpen;
        private DateTime pendingSince;
        private DateTime nextOpenAttempt;
        private int lastSender, lastSequence;
        private DateTime remoteOpenedAt;
        private DateTime nextBlockedActionLog;
        private int distanceBypasses, proximitySourceOverrides, blockedActions;

        internal bool Enabled { get; private set; }
        internal string Status { get; private set; } = "Интерактивный общий лут выключен.";
        internal string Diagnostics
        {
            get
            {
                return "Native interactive loot: enabled=" + Enabled + "; hooks=" + hooksInstalled +
                    "; connected=" + online + "; localKind=" + localKind + "; remote=" + remoteSender +
                    "; pending=" + (pendingOpen != null) + "; remoteSeq=" + incomingSequence +
                    "; rangeBypasses=" + distanceBypasses + "; sourceOverrides=" + proximitySourceOverrides +
                    "; blockedNonLocal=" + blockedActions + "; status=" + Status + "; F11=disabled";
            }
        }

        internal LootShare()
        {
            instance = this;
            try { InstallHooks(); }
            catch (Exception ex) { Log.Error("loot", "Install interactive loot hooks", ex); }
        }

        internal void SetEnabled(bool enabled)
        {
            if (!enabled)
            {
                if (online && localKind != 0) Send(new object[] { Magic, Close, ++sequence }, true);
                CloseRemote();
                localKind = 0;
                pendingOpen = null;
                Detach();
                Enabled = false;
                Status = "Интерактивный общий лут выключен.";
                return;
            }
            if (!hooksInstalled)
            {
                Enabled = false;
                Status = "Не удалось подключить штатный лут. См. loot-лог.";
                return;
            }
            Enabled = true;
            Status = "Ожидание открытия сундука или лута с земли.";
        }

        private static MethodInfo FindMethod(string typeName, string methodName, int arguments)
        {
            Type type = Read.Type(typeName);
            return type == null ? null : type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly).SingleOrDefault(m => m.Name == methodName && m.GetParameters().Length == arguments);
        }

        private static void PatchOpened(string typeName, string methodName, int arguments, string callbackName)
        {
            MethodInfo target = FindMethod(typeName, methodName, arguments);
            if (target == null) throw new MissingMethodException(typeName + "." + methodName);
            MethodInfo callback = typeof(LootShare).GetMethod(callbackName, BindingFlags.NonPublic | BindingFlags.Static);
            Type[] types = target.GetParameters().Select(p => p.ParameterType).ToArray();
            HarmonySupport.Patch(target, postfix: callback.MakeGenericMethod(types));
        }

        private static void PatchInteraction(string typeName, string methodName, int arguments)
        {
            MethodInfo target = FindMethod(typeName, methodName, arguments);
            if (target == null) throw new MissingMethodException(typeName + "." + methodName);
            MethodInfo guard = typeof(LootShare).GetMethod("GuardLootAction", BindingFlags.NonPublic | BindingFlags.Static);
            HarmonySupport.Patch(target, prefix: guard);
        }

        private void InstallHooks()
        {
            PatchOpened("ContainerLootModal", "ShowContainerLoot", 2, "ContainerOpened");
            PatchOpened("ProximityLootModal", "ShowProximityLoot", 1, "ProximityOpened");

            // Vanilla handlers remain responsible for inventory mutation and multiplayer RPCs.
            // Guard only against accidentally performing actions on somebody else's hero.
            PatchInteraction("LootEnumerationModal", "OnLootAllCb", 0);
            PatchInteraction("LootEnumerationModal", "TryToLootSlotBox", 1);
            PatchInteraction("ContainerLootModal", "OnLootAllCb", 0);
            PatchInteraction("ContainerLootModal", "HandleItemLoot", 4);
            PatchInteraction("LootEnumerationModal", "OnPointerUp", 1);
            PatchInteraction("LootEnumerationModal", "OnSubmitPerformed", 1);

            // The game's BuildHeroesList checks this virtual property before it calls
            // IsWithinXCells. Bypass it only for a remote container modal, not globally.
            MethodInfo restriction = FindMethod("ContainerLootModal", "get_ComputeHeroDistanceRestriction", 0);
            if (restriction == null || restriction.ReturnType != typeof(bool))
                throw new MissingMethodException("ContainerLootModal.get_ComputeHeroDistanceRestriction");
            HarmonySupport.Patch(restriction, prefix: typeof(LootShare).GetMethod(
                "IgnoreRemoteDistance", BindingFlags.NonPublic | BindingFlags.Static));

            // ProximityLootModal computes its item source from the selected character.
            // Replace that source with the opener's position while keeping LootingHero
            // as the receiver's own (locally controlled) character.
            MethodInfo proximityChanged = FindMethod("ProximityLootModal", "CharacterSelectionChanged", 0);
            if (proximityChanged == null) throw new MissingMethodException("ProximityLootModal.CharacterSelectionChanged");
            HarmonySupport.Patch(proximityChanged, transpiler: typeof(LootShare).GetMethod(
                "ProximitySourcePatch", BindingFlags.NonPublic | BindingFlags.Static));

            hooksInstalled = true;
            Log.Write("Interactive native loot hooks installed; range limited only for local vanilla UI; no screenshots or custom inventory RPCs.");
        }

        private static bool GuardLootAction(object __instance)
        {
            LootShare owner = instance;
            if (owner == null || !owner.Enabled || owner.remoteSender == 0 ||
                !Object.ReferenceEquals(owner.remoteModal, __instance)) return true;

            object guiHero = Read.Get(__instance, "LootingHero");
            object chosen = Read.Get(guiHero, "GameLocationCharacter");
            if (IsLocallyControlled(chosen)) return true;
            owner.blockedActions++;
            owner.Status = "Выберите собственного персонажа для лута. Чужим персонажем брать вещи нельзя.";
            if (DateTime.UtcNow >= owner.nextBlockedActionLog)
            {
                owner.nextBlockedActionLog = DateTime.UtcNow.AddSeconds(3);
                Log.Event("loot", "WARN", "Rejected remote loot click: selected hero is not locally controlled.");
            }
            return false;
        }

        private static bool IgnoreRemoteDistance(object __instance, ref bool __result)
        {
            LootShare owner = instance;
            if (owner == null || !owner.Enabled ||
                !(owner.openingRemoteKind == OpenContainer ||
                    owner.remoteSender != 0 && Object.ReferenceEquals(owner.remoteModal, __instance))) return true;
            owner.distanceBypasses++;
            __result = false;
            return false;
        }

        private static IEnumerable<CodeInstruction> ProximitySourcePatch(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> body = instructions.ToList();
            MethodInfo substitute = typeof(LootShare).GetMethod("SubstituteProximitySource", BindingFlags.Public | BindingFlags.Static);
            int replacements = 0;
            foreach (CodeInstruction instruction in body)
            {
                yield return instruction;
                MethodInfo method = instruction.operand as MethodInfo;
                if (method == null || method.Name != "get_GameLocationCharacter" ||
                    method.GetParameters().Length != 0 || method.ReturnType != Read.Type("GameLocationCharacter") ||
                    (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt)) continue;
                // Original GameLocationCharacter remains on the stack.
                // The helper returns either the original or the opener's position source.
                yield return new CodeInstruction(OpCodes.Call, substitute);
                yield return new CodeInstruction(OpCodes.Castclass, method.ReturnType);
                replacements++;
            }
            Log.Write("Proximity loot source redirections installed: " + replacements);
            if (replacements != 1)
                Log.Event("loot", "WARN", "Expected one source lookup in ProximityLootModal.CharacterSelectionChanged, found " + replacements);
        }

        // Public because Harmony-injected call is emitted into the game's assembly.
        public static object SubstituteProximitySource(object original)
        {
            LootShare owner = instance;
            if (owner == null || !owner.Enabled) return original;
            object source = owner.openingRemoteKind == OpenProximity ? owner.openingRemoteSource :
                owner.remoteSender != 0 ? owner.remoteSourceCharacter : null;
            if (source == null) return original;
            owner.proximitySourceOverrides++;
            return source;
        }

        private static void ContainerOpened<TCharacter, TContainer>(TCharacter __0, TContainer __1)
        {
            if (instance != null) instance.NativeOpened(OpenContainer, __0, __1);
        }

        private static void ProximityOpened<TCharacter>(TCharacter __0)
        {
            if (instance != null) instance.NativeOpened(OpenProximity, __0, null);
        }

        private static string GuidOf(object obj)
        {
            object guid = Read.Get(obj, "Guid") ?? Read.Get(Read.Get(obj, "RulesetCharacter"), "Guid");
            return guid is ulong && (ulong)guid != 0 ? ((ulong)guid).ToString() : null;
        }

        private void NativeOpened(int kind, object character, object container)
        {
            if (!Enabled || ignoreNative || !online || remoteSender != 0) return;
            string hero = GuidOf(character);
            string target = kind == OpenContainer ? GuidOf(container) : "";
            if (String.IsNullOrEmpty(hero) || (kind == OpenContainer && String.IsNullOrEmpty(target))) return;
            localKind = kind;
            localHero = hero;
            localContainer = target;
            openedAt = DateTime.UtcNow;
            Send(new object[] { Magic, kind, ++sequence, hero, target }, true);
            Log.Write("Interactive loot opened; kind=" + kind + "; hero=" + hero + "; source=" + target);
        }

        private static int Actor(object player)
        {
            object result = Read.Get(player, "ActorNumber");
            return result is int ? (int)result : 0;
        }

        private bool InRoom()
        {
            return Object.Equals(Read.Get(photon, "InRoom"), true) &&
                !Object.Equals(Read.Get(photon, "OfflineMode"), true);
        }

        private bool Member(int actor)
        {
            return actor > 0 && Read.Items(Read.Get(Read.Get(room, "Players"), "Values"))
                .Any(p => Actor(p) == actor);
        }

        private void Attach(object next)
        {
            if (Object.ReferenceEquals(client, next)) return;
            Detach();
            client = next;
            if (client == null) return;
            receivedEvent = client.GetType().GetEvent("EventReceived");
            if (receivedEvent == null) throw new MissingMemberException("Photon EventReceived");
            Type arg = receivedEvent.EventHandlerType.GetMethod("Invoke").GetParameters()[0].ParameterType;
            MethodInfo method = GetType().GetMethod("Receive", BindingFlags.NonPublic | BindingFlags.Instance)
                .MakeGenericMethod(arg);
            receivedHandler = Delegate.CreateDelegate(receivedEvent.EventHandlerType, this, method);
            receivedEvent.AddEventHandler(client, receivedHandler);
            sendEvent = photon.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "RaiseEvent" && m.GetParameters().Length == 4);
            optionType = sendEvent.GetParameters()[2].ParameterType;
            sendType = sendEvent.GetParameters()[3].ParameterType;
        }

        private void Detach()
        {
            if (receivedEvent != null && client != null && receivedHandler != null)
                try { receivedEvent.RemoveEventHandler(client, receivedHandler); }
                catch (Exception ex) { Log.Error("loot", "Detach interactive loot receiver", ex); }
            client = null; receivedEvent = null; receivedHandler = null; sendEvent = null;
        }

        private void Receive<T>(T evt)
        {
            try
            {
                if (!Enabled || !Object.Equals(Read.Get(evt, "Code"), EventCode)) return;
                int sender = (int)(Read.Get(evt, "Sender") ?? 0);
                object[] data = Read.Get(evt, "CustomData") as object[];
                if (data == null || data.Length < 3 || !Object.Equals(data[0], Magic) ||
                    !(data[1] is int) || !(data[2] is int)) return;
                int kind = (int)data[1];
                if (kind == Close && data.Length != 3) return;
                if (kind != Close && (kind != OpenContainer && kind != OpenProximity ||
                    data.Length != 5 || !(data[3] is string) || !(data[4] is string) ||
                    ((string)data[3]).Length > 32 || ((string)data[4]).Length > 32)) return;
                lock (sync)
                {
                    if (inbox.Count >= 16) inbox.Dequeue();
                    inbox.Enqueue(new Message { Sender = sender, Data = data });
                }
            }
            catch (Exception ex) { Log.Error("loot", "Receive interactive loot", ex); }
        }

        private void Send(object[] data, bool reliable)
        {
            if (!Enabled || !online || sendEvent == null) return;
            try
            {
                object opt = Activator.CreateInstance(optionType), send = Activator.CreateInstance(sendType);
                ObserverHud.SetMember(send, "Reliability", reliable);
                sendEvent.Invoke(null, new[] { (object)EventCode, data, opt, send });
            }
            catch (Exception ex) { Log.Error("loot", "Interactive loot event", ex); }
        }

        private static object FindHero(ulong guid)
        {
            object service = Read.Service("IGameLocationCharacterService");
            foreach (string property in new[] { "PartyCharacters", "AllValidEntities", "AllDeadEntities" })
                foreach (object character in Read.Items(Read.Get(service, property)))
                    if (Object.Equals(Read.Get(character, "Guid"), guid)) return character;
            return null;
        }

        private static bool IsLocallyControlled(object character)
        {
            if (character == null) return false;
            object ruleset = Read.Get(character, "RulesetCharacter");
            object players = Read.Service("IPlayerControllerService");
            object network = Read.Service("INetworkingService");
            if (ruleset == null || players == null || network == null) return false;
            object playerId = Read.Call(players, "GetPlayerControllingHero", ruleset);
            if (!(playerId is int)) return false;
            object controller = Read.Call(players, "GetPlayerController", playerId);
            return controller != null && Object.Equals(Read.Call(network, "IsLocalPlayerController", controller), true);
        }

        private static object FindLocalHero()
        {
            object service = Read.Service("IGameLocationCharacterService");
            return Read.Items(Read.Get(service, "PartyCharacters")).FirstOrDefault(IsLocallyControlled);
        }

        private static object FindContainer(ulong guid)
        {
            object service = Read.Service("IRulesetEntityService");
            IDictionary dictionary = Read.Get(service, "RulesetEntities") as IDictionary;
            if (dictionary == null || !dictionary.Contains(guid)) return null;
            object result = dictionary[guid];
            Type wanted = Read.Type("RulesetContainer");
            return wanted != null && wanted.IsInstanceOfType(result) ? result : null;
        }

        private static object Modal(int kind)
        {
            Type type = Read.Type(kind == OpenContainer ? "ContainerLootModal" : "ProximityLootModal");
            object gui = Read.Get(Read.Type("Gui"), "GuiService");
            return type == null ? null : Read.Call(gui, "GetScreen", type);
        }

        private static bool IsVisible(object screen)
        {
            return Object.Equals(Read.Get(screen, "Visible"), true) ||
                Object.Equals(Read.Get(screen, "Showing"), true) ||
                Object.Equals(Read.Get(screen, "Shown"), true);
        }

        private void OpenRemote(int kind, string heroId, string containerId, int sender)
        {
            ulong heroGuid, containerGuid;
            if (!UInt64.TryParse(heroId, out heroGuid)) return;
            object sourceHero = FindHero(heroGuid);
            object localHero = FindLocalHero();
            object container = null;
            if (kind == OpenContainer)
            {
                if (!UInt64.TryParse(containerId, out containerGuid)) return;
                container = FindContainer(containerGuid);
            }
            if (sourceHero == null || localHero == null || kind == OpenContainer && container == null)
            {
                Status = "Жду исходный объект и своего персонажа; повторю открытие...";
                return;
            }
            object modal = Modal(kind);
            if (modal == null) { Status = "Экран лута не найден через GuiService.GetScreen."; return; }
            if (IsVisible(modal) || localKind != 0) { Status = "Жду закрытия другого окна лута."; return; }

            bool originalDisabled = Object.Equals(Read.Get(modal, "ShouldDisableSlots"), true);
            bool success = false;
            try
            {
                ignoreNative = true;
                openingRemoteKind = kind;
                openingRemoteSource = sourceHero;
                // Must be set BEFORE Show... calls BuildModal/BuildSlots.
                ObserverHud.SetMember(modal, "ShouldDisableSlots", false);
                if (kind == OpenContainer) Read.Call(modal, "ShowContainerLoot", localHero, container);
                else Read.Call(modal, "ShowProximityLoot", localHero);
                if (!IsVisible(modal))
                {
                    Status = "Штатное окно не стало видимым, повторяю...";
                    return;
                }
                remoteModal = modal;
                remoteSourceCharacter = sourceHero;
                previousSlotsDisabled = originalDisabled;
                remoteOpenedAt = DateTime.UtcNow;
                remoteSender = sender;
                success = true;
                Status = "Открыт общий лут: выбирайте своих героев и забирайте предметы (без проверки дальности UI).";
                Log.Write("Interactive native loot open; sender=" + sender + "; kind=" + kind +
                    "; receiverHero=" + GuidOf(localHero) + "; sourceHero=" + heroId + "; container=" + containerId);
            }
            catch (Exception ex)
            {
                Status = "Не удалось открыть общий лут: " + ex.GetBaseException().Message;
                Log.Error("loot", "Open interactive native loot", ex);
            }
            finally
            {
                openingRemoteKind = 0;
                openingRemoteSource = null;
                ignoreNative = false;
                if (!success)
                {
                    try { ObserverHud.SetMember(modal, "ShouldDisableSlots", originalDisabled); }
                    catch (Exception ex) { Log.Error("loot", "Restore failed open slot state", ex); }
                }
            }
        }

        private void CloseRemote()
        {
            object modal = remoteModal;
            remoteModal = null;
            remoteSourceCharacter = null;
            remoteSender = 0;
            incomingSequence = 0;
            if (modal == null) return;
            try { ObserverHud.SetMember(modal, "ShouldDisableSlots", previousSlotsDisabled); }
            catch (Exception ex) { Log.Error("loot", "Restore loot slot state", ex); }
            try
            {
                ignoreNative = true;
                Read.Call(modal, "Hide", false);
            }
            catch (Exception ex) { Log.Error("loot", "Close interactive loot modal", ex); }
            finally { ignoreNative = false; }
        }

        private void Process(Message message)
        {
            if (!Member(message.Sender)) return;
            object local = Read.Get(photon, "LocalPlayer");
            if (message.Sender == Actor(local)) return;
            int kind = (int)message.Data[1], seq = (int)message.Data[2];
            if (message.Sender == lastSender && seq <= lastSequence) return;
            lastSender = message.Sender;
            lastSequence = seq;
            if (kind == Close)
            {
                if (pendingOpen != null && pendingOpen.Sender == message.Sender) pendingOpen = null;
                // Once opened, the second player owns their local modal. Do not close it
                // just because the original opener stopped looting first.
                if (remoteSender == message.Sender)
                    Status = "Другой игрок закрыл лут. Ваше окно остаётся открытым до закрытия вами.";
                return;
            }
            if (remoteSender != 0) return;
            pendingOpen = message;
            pendingSince = DateTime.UtcNow;
            nextOpenAttempt = DateTime.MinValue;
            Status = "Получено открытие, ищу объект и локального персонажа.";
            Log.Write("Interactive loot received; sender=" + message.Sender + "; kind=" + kind + "; seq=" + seq);
        }

        internal void Tick()
        {
            try
            {
                photon = Read.Type("Photon.Pun.PhotonNetwork");
                online = InRoom();
                object nextRoom = Read.Get(photon, "CurrentRoom");
                if (!Enabled || !online || !Object.ReferenceEquals(room, nextRoom))
                {
                    if (remoteSender != 0) CloseRemote();
                    pendingOpen = null;
                    localKind = 0;
                    if (!online || !Enabled) Detach();
                    room = online ? nextRoom : null;
                    lastSender = 0; lastSequence = 0;
                    if (!Enabled || !online) return;
                }
                Attach(Read.Get(photon, "NetworkingClient"));
                Message[] messages;
                lock (sync) { messages = inbox.ToArray(); inbox.Clear(); }
                foreach (Message message in messages) Process(message);
                if (pendingOpen != null)
                {
                    if ((DateTime.UtcNow - pendingSince).TotalSeconds > 12)
                    {
                        Status = "Не удалось открыть общий лут за 12 секунд. Проверьте loot-лог.";
                        Log.Event("loot", "WARN", "Interactive native loot open timed out: " + Diagnostics);
                        pendingOpen = null;
                    }
                    else if (DateTime.UtcNow >= nextOpenAttempt && remoteSender == 0 && localKind == 0)
                    {
                        nextOpenAttempt = DateTime.UtcNow.AddMilliseconds(450);
                        int kind = (int)pendingOpen.Data[1];
                        OpenRemote(kind, (string)pendingOpen.Data[3], (string)pendingOpen.Data[4], pendingOpen.Sender);
                        if (remoteSender == pendingOpen.Sender)
                        {
                            incomingSequence = (int)pendingOpen.Data[2];
                            pendingOpen = null;
                        }
                    }
                }
                if (remoteSender != 0 && (!Member(remoteSender) ||
                    (DateTime.UtcNow - remoteOpenedAt).TotalSeconds > 1.5 && !IsVisible(remoteModal)))
                    CloseRemote();
                if (localKind != 0 && (DateTime.UtcNow - openedAt).TotalSeconds > 1.5)
                {
                    if (!IsVisible(Modal(localKind)))
                    {
                        localKind = 0;
                        Send(new object[] { Magic, Close, ++sequence }, true);
                    }
                }
                if (remoteSender == 0 && localKind == 0 && pendingOpen == null)
                    Status = "Жду сундук или труп. Штатный UI, предметы можно забирать своим героем.";
            }
            catch (Exception ex)
            {
                Log.Error("loot", "Interactive native loot disabled", ex);
                SetEnabled(false);
            }
        }

        internal void ClosePreview() { pendingOpen = null; CloseRemote(); }

        public void Dispose()
        {
            SetEnabled(false);
            instance = null;
            Detach();
        }
    }
}
