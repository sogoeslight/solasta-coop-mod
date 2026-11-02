using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SolastaCoop
{
    // Transmits the destination of actual host MoveCharacter commands.
    // It never sends or executes mouse clicks, game RPCs, or movement of remote-controlled heroes.
    internal sealed class HostMoveShare : IDisposable
    {
        private const byte EventCode = 192;
        private const string Magic = "SolastaCoopView.host-movement/1";
        private const float MinimumSendInterval = 0.2f;
        private sealed class Movement
        {
            internal int Sender, Sequence, X, Y, Z;
            internal ulong Character;
        }
        private static HostMoveShare instance;
        private readonly object gate = new object();
        private readonly Queue<Movement> incoming = new Queue<Movement>();
        private object client, room;
        private Type photon, optionsType, sendType;
        private EventInfo eventInfo;
        private Delegate eventHandler;
        private MethodInfo raiseEvent;
        private bool hookInstalled;
        private bool disabled;
        private float lastSent;
        private ulong lastCharacter;
        private int lastX, lastY, lastZ, sequence, receivedSequence;
        private int receivedFrom;
        internal bool Enabled { get; private set; } = true;
        internal string Status { get; private set; } = "Waiting for a Photon connection.";

        internal void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            if (!enabled)
            {
                lock (gate) incoming.Clear();
                Detach();
            }
            Status = enabled ? "Host waypoint sharing enabled." : "Host waypoint sharing disabled.";
        }

        internal HostMoveShare(PartyAutoFollow follower)
        {
            Follower = follower;
            instance = this;
        }

        internal PartyAutoFollow Follower { get; private set; }

        private static int Coordinate(object position, string property)
        {
            object value = Read.Get(position, property);
            return value is int ? (int)value : 0;
        }

        private static bool ValidCoord(int value) { return value >= -10000 && value <= 10000; }

        private static ulong GuidOf(object character)
        {
            object value = Read.Get(character, "Guid") ?? Read.Get(Read.Get(character, "RulesetCharacter"), "Guid");
            return value is ulong ? (ulong)value : 0UL;
        }

        private static bool? LocalCharacter(object character)
        {
            object ruleset = Read.Get(character, "RulesetCharacter");
            object controllers = Read.Service("IPlayerControllerService");
            object networking = Read.Service("INetworkingService");
            if (ruleset == null || controllers == null || networking == null) return null;
            object id = Read.Call(controllers, "GetPlayerControllingHero", ruleset);
            if (!(id is int)) return null;
            object controller = Read.Call(controllers, "GetPlayerController", id);
            object local = Read.Call(networking, "IsLocalPlayerController", controller);
            return local is bool ? (bool?)(bool)local : null;
        }

        private bool Online()
        {
            return object.Equals(Read.Get(photon, "InRoom"), true) &&
                object.Equals(Read.Get(photon, "OfflineMode"), false);
        }

        private static int Actor(object player)
        {
            object value = Read.Get(player, "ActorNumber");
            return value is int ? (int)value : 0;
        }

        private bool Member(int actor)
        {
            return actor > 0 && Read.Items(Read.Get(Read.Get(room, "Players"), "Values"))
                .Any(p => Actor(p) == actor);
        }

        private void InstallMoveHook()
        {
            if (hookInstalled || disabled) return;
            object service = Read.Service("ICommandService");
            if (service == null) return;
            try
            {
                MethodInfo target = service.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "MoveCharacter" && m.GetParameters().Length == 8 && !m.IsAbstract);
                if (target == null) throw new MissingMethodException("ICommandService.MoveCharacter(8) implementation");
                var parameters = target.GetParameters();
                MethodInfo callback = typeof(HostMoveShare).GetMethod("AfterMove", BindingFlags.NonPublic | BindingFlags.Static)
                    .MakeGenericMethod(parameters[0].ParameterType, parameters[1].ParameterType);
                HarmonySupport.Patch(target, null, callback);
                hookInstalled = true;
                Log.Event("core", "INFO", "Host move hook installed: " + target.DeclaringType.FullName);
            }
            catch (Exception ex)
            {
                disabled = true;
                Status = "Failed to hook movement commands: " + ex.GetBaseException().Message;
                Log.Error("core", "Install host movement hook", ex);
            }
        }

        private static void AfterMove<TActor, TPosition>(TActor __0, TPosition __1)
        {
            try
            {
                HostMoveShare current = instance;
                if (current != null) current.MoveIssued(__0, __1);
            }
            catch (Exception ex) { Log.Error("core", "Host movement event", ex); }
        }

        private void MoveIssued(object actor, object position)
        {
            if (!Enabled || !hookInstalled || !Online() || !object.Equals(Read.Get(photon, "IsMasterClient"), true) ||
                PartyAutoFollow.IsIssuingMove || actor == null || position == null || LocalCharacter(actor) != true ||
                Read.Get(Read.Type("Gui"), "Battle") != null) return;
            ulong guid = GuidOf(actor);
            int x = Coordinate(position, "x"), y = Coordinate(position, "y"), z = Coordinate(position, "z");
            if (guid == 0 || !ValidCoord(x) || !ValidCoord(y) || !ValidCoord(z)) return;
            if (guid == lastCharacter && x == lastX && y == lastY && z == lastZ &&
                Time.unscaledTime - lastSent < MinimumSendInterval) return;
            lastSent = Time.unscaledTime;
            lastCharacter = guid;
            lastX = x; lastY = y; lastZ = z;
            if (raiseEvent == null) return;
            object options = Activator.CreateInstance(optionsType);
            object send = Activator.CreateInstance(sendType);
            ObserverHud.SetMember(send, "Reliability", true);
            raiseEvent.Invoke(null, new object[] { EventCode,
                new object[] { Magic, ++sequence, guid.ToString(), x, y, z }, options, send });
            Status = "Host waypoint sent: " + x + ", " + y + ", " + z + ".";
        }

        private void Attach(object next)
        {
            if (ReferenceEquals(next, client)) return;
            Detach();
            client = next;
            if (next == null) return;
            eventInfo = next.GetType().GetEvent("EventReceived");
            if (eventInfo == null) throw new MissingMemberException("Photon EventReceived");
            Type eventArgument = eventInfo.EventHandlerType.GetMethod("Invoke").GetParameters()[0].ParameterType;
            MethodInfo callback = GetType().GetMethod("OnEvent", BindingFlags.NonPublic | BindingFlags.Instance)
                .MakeGenericMethod(eventArgument);
            eventHandler = Delegate.CreateDelegate(eventInfo.EventHandlerType, this, callback);
            eventInfo.AddEventHandler(next, eventHandler);
            raiseEvent = photon.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "RaiseEvent" && m.GetParameters().Length == 4);
            optionsType = raiseEvent.GetParameters()[2].ParameterType;
            sendType = raiseEvent.GetParameters()[3].ParameterType;
        }

        private void OnEvent<T>(T evt)
        {
            try
            {
                if (!Enabled || !object.Equals(Read.Get(evt, "Code"), EventCode)) return;
                object[] data = Read.Get(evt, "CustomData") as object[];
                object sender = Read.Get(evt, "Sender");
                if (!(sender is int) || data == null || data.Length != 6 ||
                    !object.Equals(data[0], Magic) || !(data[1] is int) || !(data[2] is string) ||
                    !(data[3] is int) || !(data[4] is int) || !(data[5] is int)) return;
                ulong guid;
                if (!ulong.TryParse((string)data[2], out guid) || guid == 0 ||
                    !ValidCoord((int)data[3]) || !ValidCoord((int)data[4]) || !ValidCoord((int)data[5])) return;
                lock (gate)
                {
                    if (incoming.Count >= 32) incoming.Dequeue();
                    incoming.Enqueue(new Movement { Sender = (int)sender, Sequence = (int)data[1],
                        Character = guid, X = (int)data[3], Y = (int)data[4], Z = (int)data[5] });
                }
            }
            catch (Exception ex) { Log.Error("network", "Receive host movement event", ex); }
        }

        internal void Tick()
        {
            if (disabled) return;
            if (!Enabled)
            {
                Detach();
                return;
            }
            try
            {
                photon = Read.Type("Photon.Pun.PhotonNetwork");
                bool online = Online();
                object nextRoom = online ? Read.Get(photon, "CurrentRoom") : null;
                if (!ReferenceEquals(room, nextRoom))
                {
                    room = nextRoom;
                    receivedSequence = 0;
                    receivedFrom = 0;
                    lock (gate) incoming.Clear();
                }
                if (!online)
                {
                    Detach();
                    Status = "Waiting for multiplayer.";
                    return;
                }
                Attach(Read.Get(photon, "NetworkingClient"));
                InstallMoveHook();
                int master = Actor(Read.Get(photon, "MasterClient"));
                bool localHost = object.Equals(Read.Get(photon, "IsMasterClient"), true);
                while (true)
                {
                    Movement move;
                    lock (gate)
                    {
                        if (incoming.Count == 0) break;
                        move = incoming.Dequeue();
                    }
                    if (localHost || !Member(move.Sender) || move.Sender != master) continue;
                    if (receivedFrom == move.Sender && move.Sequence <= receivedSequence) continue;
                    receivedFrom = move.Sender;
                    receivedSequence = move.Sequence;
                    Follower.OnHostMove(move.Character, move.X, move.Y, move.Z);
                    Status = "Host movement command received: " + move.X + ", " + move.Y + ", " + move.Z + ".";
                }
            }
            catch (Exception ex)
            {
                Status = "Movement command sharing error: " + ex.GetBaseException().Message;
                Log.Error("network", "Host movement channel", ex);
            }
        }

        private void Detach()
        {
            if (client != null && eventInfo != null && eventHandler != null)
                try { eventInfo.RemoveEventHandler(client, eventHandler); }
                catch (Exception e) { Log.Error("network", "Detach movement channel", e); }
            client = null;
            eventInfo = null;
            eventHandler = null;
            raiseEvent = null;
        }

        public void Dispose()
        {
            Detach();
            if (ReferenceEquals(instance, this)) instance = null;
        }
    }
}
