using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SolastaCoop
{
    // Native UI preview: small identifiers only, no screenshots and no loot gameplay RPCs.
    // Disabled by default until tested with two real clients.
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
        private DateTime openedAt, lastHello;
        private object remoteModal, remoteCanvasGroup;
        private bool previousInteractable, previousBlocksRaycasts;
        private int lastSender, lastSequence;
        private DateTime remoteOpenedAt;

        internal bool Enabled { get; private set; }
        internal string Status { get; private set; } = "Нативный просмотр лута выключен. F11 отключён.";
        internal string Diagnostics
        {
            get { return "Native loot: enabled=" + Enabled + "; hooks=" + hooksInstalled +
                "; connected=" + online + "; localKind=" + localKind + "; remote=" + remoteSender +
                "; frames=none; F11=disabled"; }
        }

        internal LootShare()
        {
            instance = this;
            try { InstallHooks(); }
            catch (Exception ex) { Log.Error("loot", "Install native loot hooks", ex); }
        }
        internal void SetEnabled(bool enabled)
        {
            if (!enabled)
            {
                if (online && localKind != 0) Send(new object[] { Magic, Close, ++sequence }, true);
                CloseRemote();
                localKind = 0;
                Detach();
                Enabled = false;
                Status = "Нативный просмотр выключен, F11 отключён.";
                return;
            }
            if (!hooksInstalled)
            {
                Status = "Не удалось подключить нативные окна лута. См. лог.";
                return;
            }
            Enabled = true;
            Status = "Ожидание открытия настоящего окна сундука или трупа.";
        }
        private static MethodInfo FindMethod(string typeName, string methodName, int arguments)
        {
            Type type = Read.Type(typeName);
            return type == null ? null : type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SingleOrDefault(m => m.Name == methodName && m.GetParameters().Length == arguments);
        }
        private static void PatchOpened(string typeName, string methodName, int args, string handlerName)
        {
            MethodInfo target = FindMethod(typeName, methodName, args);
            if (target == null) throw new MissingMethodException(typeName + "." + methodName);
            MethodInfo callback = typeof(LootShare).GetMethod(handlerName, BindingFlags.NonPublic | BindingFlags.Static);
            Type[] types = target.GetParameters().Select(p => p.ParameterType).ToArray();
            HarmonySupport.Patch(target, null, callback.MakeGenericMethod(types));
        }
        private static void PatchInteraction(string typeName, string methodName, int args)
        {
            MethodInfo target = FindMethod(typeName, methodName, args);
            if (target == null) throw new MissingMethodException(typeName + "." + methodName);
            MethodInfo callback = typeof(LootShare).GetMethod("AllowLoot", BindingFlags.NonPublic | BindingFlags.Static);
            HarmonySupport.Patch(target, callback);
        }
        private void InstallHooks()
        {
            PatchOpened("ContainerLootModal", "ShowContainerLoot", 2, "ContainerOpened");
            PatchOpened("ProximityLootModal", "ShowProximityLoot", 1, "ProximityOpened");
            PatchInteraction("LootEnumerationModal", "OnLootAllCb", 0);
            PatchInteraction("LootEnumerationModal", "TryToLootSlotBox", 1);
            PatchInteraction("ContainerLootModal", "OnLootAllCb", 0);
            PatchInteraction("ContainerLootModal", "HandleItemLoot", 4);
            PatchInteraction("InventoryPanel", "OnLootAllCb", 0);
            hooksInstalled = true;
            Log.Write("Native loot hooks installed. Feature disabled by default. No screen capture.");
        }
        private static bool AllowLoot()
        {
            // Read-only remote UI must not submit any loot action, even via controller shortcuts.
            return instance == null || !instance.Enabled || instance.remoteSender == 0;
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
            object guid = Read.Get(obj, "Guid");
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
            Log.Write("Native loot opened, kind=" + kind + "; hero=" + hero + "; target=" + target);
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
                catch (Exception ex) { Log.Error("loot", "Detach native receiver", ex); }
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
            catch (Exception ex) { Log.Error("loot", "Receive native loot", ex); }
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
            catch (Exception ex) { Log.Error("loot", "Native loot event", ex); }
        }
        private static object FindHero(ulong guid)
        {
            object service = Read.Service("IGameLocationCharacterService");
            foreach (string property in new[] { "PartyCharacters", "AllValidEntities", "AllDeadEntities" })
                foreach (object character in Read.Items(Read.Get(service, property)))
                    if (Object.Equals(Read.Get(character, "Guid"), guid)) return character;
            return null;
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
            object hero = FindHero(heroGuid);
            object container = null;
            if (kind == OpenContainer)
            {
                if (!UInt64.TryParse(containerId, out containerGuid)) return;
                container = FindContainer(containerGuid);
            }
            if (hero == null || kind == OpenContainer && container == null)
            {
                Log.Event("loot", "WARN", "Native loot object is not in local game state; skipped.");
                return;
            }
            object modal = Modal(kind);
            if (modal == null || IsVisible(modal) || localKind != 0) return;
            try
            {
                ignoreNative = true;
                if (kind == OpenContainer) Read.Call(modal, "ShowContainerLoot", hero, container);
                else Read.Call(modal, "ShowProximityLoot", hero);
                // Native UI and native item slots are used, but remote interactions are blocked.
                object group = Read.Get(modal, "CanvasGroup");
                if (group != null)
                {
                    // Preserve the native UI so its slots and tooltips remain real Unity UI.
                    // Keep raycasts blocked from falling through to gameplay underneath.
                    object interactable = Read.Get(group, "interactable");
                    object blocks = Read.Get(group, "blocksRaycasts");
                    if (interactable is bool && blocks is bool)
                    {
                        remoteCanvasGroup = group;
                        previousInteractable = (bool)interactable;
                        previousBlocksRaycasts = (bool)blocks;
                        ObserverHud.SetMember(group, "interactable", false);
                        ObserverHud.SetMember(group, "blocksRaycasts", true);
                    }
                }
                remoteModal = modal;
                remoteOpenedAt = DateTime.UtcNow;
                remoteSender = sender;
                Status = "Штатное окно лута союзника открыто (только просмотр).";
                Log.Write("Opened native loot preview, sender=" + sender + "; kind=" + kind);
            }
            catch (Exception ex) { Log.Error("loot", "Show native loot modal", ex); }
            finally { ignoreNative = false; }
        }
        private void CloseRemote()
        {
            if (remoteCanvasGroup != null)
            {
                try
                {
                    ObserverHud.SetMember(remoteCanvasGroup, "interactable", previousInteractable);
                    ObserverHud.SetMember(remoteCanvasGroup, "blocksRaycasts", previousBlocksRaycasts);
                }
                catch (Exception ex) { Log.Error("loot", "Restore native loot canvas", ex); }
                remoteCanvasGroup = null;
            }
            if (remoteModal != null)
            {
                try
                {
                    ignoreNative = true;
                    Read.Call(remoteModal, "Hide", false);
                }
                catch (Exception ex) { Log.Error("loot", "Close native loot modal", ex); }
                finally { ignoreNative = false; }
            }
            remoteModal = null; remoteSender = 0; incomingSequence = 0;
        }
        private void Process(Message message)
        {
            if (!Member(message.Sender)) return;
            object local = Read.Get(photon, "LocalPlayer");
            if (message.Sender == Actor(local)) return;
            int kind = (int)message.Data[1], seq = (int)message.Data[2];
            // Ignore delayed/out-of-order opens after a Close packet.
            if (message.Sender == lastSender && seq <= lastSequence) return;
            lastSender = message.Sender;
            lastSequence = seq;
            if (kind == Close)
            {
                if (remoteSender == message.Sender) CloseRemote();
                return;
            }
            if (remoteSender != 0 || localKind != 0) return;
            OpenRemote(kind, (string)message.Data[3], (string)message.Data[4], message.Sender);
            if (remoteSender == message.Sender) incomingSequence = seq;
        }
        internal void Tick()
        {
            try
            {
                photon = Read.Type("Photon.Pun.PhotonNetwork");
                bool wasOnline = online;
                online = InRoom();
                object nextRoom = Read.Get(photon, "CurrentRoom");
                if (!Enabled || !online || !Object.ReferenceEquals(room, nextRoom))
                {
                    if (remoteSender != 0) CloseRemote();
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
                if (remoteSender == 0 && localKind == 0)
                    Status = "Ожидание открытия сундука или трупа. Никакого стрима.";
            }
            catch (Exception ex)
            {
                Log.Error("loot", "Native loot disabled", ex);
                SetEnabled(false);
            }
        }
        internal void ClosePreview() { CloseRemote(); }
        public void Dispose()
        {
            SetEnabled(false);
            instance = null;
            Detach();
        }
    }
}
