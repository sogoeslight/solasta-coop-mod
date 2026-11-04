using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TA
{
    public struct int3
    {
        public int x, y, z;
        public int3(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
    }
}
public static class ActionDefinitions { public enum Id { ExplorationMove, Manipulate } }
public enum FollowOrientation { North }
public enum FollowStance { Walk }
public interface IPlayerControllerService { }
public interface INetworkingService { }
public interface ICursorService { }
public interface IGameLocationCharacterService { }
public interface ICommandService { }
public interface IGameLocationActionService { }
public sealed class FollowRuleset { public ulong Guid; public int Owner; public string Name = "Fixture hero"; }
public sealed class FollowController { public int Id; }
public sealed class FollowCharacter
{
    public ulong Guid;
    public FollowRuleset RulesetCharacter;
    public TA.int3 LocationPosition, DestinationPosition;
    public FollowOrientation Orientation;
    public bool Busy, MovingToDestination, MoveInProgress;
    public ActionDefinitions.Id CurrentAction;
    public FollowCharacter(ulong id, int owner, int x, int y, int z)
    {
        Guid = id; RulesetCharacter = new FollowRuleset { Guid = id, Owner = owner };
        LocationPosition = new TA.int3(x, y, z);
    }
    public FollowStance GetMoveStance() { return FollowStance.Walk; }
}
public sealed class FollowPlayers : IPlayerControllerService
{
    public int GetPlayerControllingHero(FollowRuleset ruleset) { return ruleset.Owner; }
    public FollowController GetPlayerController(int id) { return new FollowController { Id = id }; }
}
public sealed class FollowNetwork : INetworkingService
{
    public int LocalPlayer = 2;
    public bool IsLocalPlayerController(FollowController controller) { return controller.Id == LocalPlayer; }
}
public sealed class CursorLocationExplorationCharacter { }
public sealed class CursorLocationNarrativeSequence { }
public sealed class FollowCursor : ICursorService { public object CurrentCursor = new CursorLocationExplorationCharacter(); }
public sealed class FollowParty : IGameLocationCharacterService { public readonly List<FollowCharacter> PartyCharacters = new List<FollowCharacter>(); }
public sealed class FollowActions : IGameLocationActionService
{
    public bool IsCharacterActing(FollowCharacter actor) { return actor.Busy; }
    public bool IsCharacterExecutingAction(FollowCharacter actor, ActionDefinitions.Id id) { return actor.Busy && actor.CurrentAction == id; }
}
public sealed class FollowCommand
{
    public FollowCharacter Actor;
    public TA.int3 Target;
    public bool Enqueue;
}
public sealed class FollowCommands : ICommandService
{
    public readonly List<FollowCommand> Sent = new List<FollowCommand>();
    public bool FailNext, DelayEcho;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void MoveCharacter(FollowCharacter actor, TA.int3 target, FollowOrientation orientation, float delay,
        FollowStance stance, Action done = null, bool enqueue = false, bool autoOrientation = false)
    {
        if (FailNext) { FailNext = false; throw new InvalidOperationException("Fixture transient command failure"); }
        Sent.Add(new FollowCommand { Actor = actor, Target = target, Enqueue = enqueue });
        if (!DelayEcho)
        {
            actor.DestinationPosition = target;
            actor.CurrentAction = ActionDefinitions.Id.ExplorationMove;
            actor.Busy = actor.MovingToDestination = true;
        }
    }
}
public static class ServiceRepository
{
    public static readonly FollowNetwork Network = new FollowNetwork();
    public static readonly FollowPlayers Players = new FollowPlayers();
    public static readonly FollowCursor Cursor = new FollowCursor();
    public static readonly FollowParty Party = new FollowParty();
    public static readonly FollowCommands Commands = new FollowCommands();
    public static readonly FollowActions Actions = new FollowActions();
    public static object GetService(Type type)
    {
        if (type == typeof(INetworkingService)) return Network;
        if (type == typeof(IPlayerControllerService)) return Players;
        if (type == typeof(ICursorService)) return Cursor;
        if (type == typeof(IGameLocationCharacterService)) return Party;
        if (type == typeof(ICommandService)) return Commands;
        if (type == typeof(IGameLocationActionService)) return Actions;
        return null;
    }
}
public static class Gui { public static object GameLocation = new object(), Battle; }
namespace Photon.Pun
{
    public sealed class FollowPlayer { public int ActorNumber; }
    public sealed class FollowRoom
    {
        public readonly Dictionary<int, FollowPlayer> Players = new Dictionary<int, FollowPlayer> {
            { 1, new FollowPlayer { ActorNumber = 1 } }, { 2, new FollowPlayer { ActorNumber = 2 } } };
    }
    public sealed class FollowEvent { public byte Code = 192; public int Sender; public object CustomData; }
    public sealed class FollowClient
    {
        public event Action<FollowEvent> EventReceived;
        public void Receive(int sender, object[] data)
        { if (EventReceived != null) EventReceived(new FollowEvent { Sender = sender, CustomData = data }); }
    }
    public sealed class FollowOptions { }
    public struct FollowSend { public bool Reliability; }
    public static class PhotonNetwork
    {
        public static bool InRoom = true, OfflineMode, IsMasterClient;
        public static FollowPlayer MasterClient = new FollowPlayer { ActorNumber = 1 };
        public static FollowRoom CurrentRoom = new FollowRoom();
        public static FollowClient NetworkingClient = new FollowClient();
        public static bool RaiseEvent(byte code, object data, FollowOptions options, FollowSend send) { return true; }
    }
}
public static class VerifyAutoFollow
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static Type followerType;
    private static Assembly plugin;
    private static object follower;
    private static FollowCharacter remote, local, second;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    private static object Call(object target, string name, params object[] args)
    { return target.GetType().GetMethod(name, Flags).Invoke(target, args); }
    private static void TimeAt(float now) { typeof(Time).GetField("CoopTestElapsed", Flags).SetValue(null, now - 10f); }
    private static void Tick(float now) { TimeAt(now); Call(follower, "Tick"); }
    private static void Waypoint(int x, int y = 0, int z = 0) { Call(follower, "OnHostMove", remote.Guid, x, y, z); }
    private static int LocalCommands { get { return ServiceRepository.Commands.Sent.Count(c => c.Actor == local); } }
    private static bool Enabled { get { return (bool)followerType.GetProperty("Enabled", Flags).GetValue(follower, null); } }
    private static void Fresh()
    {
        TimeAt(10f); Gui.GameLocation = new object(); Gui.Battle = null;
        ServiceRepository.Cursor.CurrentCursor = new CursorLocationExplorationCharacter();
        ServiceRepository.Network.LocalPlayer = 2;
        ServiceRepository.Commands.Sent.Clear(); ServiceRepository.Commands.FailNext = ServiceRepository.Commands.DelayEcho = false;
        remote = new FollowCharacter(11, 1, 10, 0, 0) { MovingToDestination = true };
        local = new FollowCharacter(21, 2, 0, 0, 0); second = new FollowCharacter(22, 2, 0, 0, 4);
        ServiceRepository.Party.PartyCharacters.Clear();
        ServiceRepository.Party.PartyCharacters.AddRange(new[] { remote, new FollowCharacter(12, 1, 50, 0, 50), local, second });
        follower = Activator.CreateInstance(followerType, true); Call(follower, "SetEnabled", true, null);
    }
    public static int Main(string[] args)
    {
        plugin = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        followerType = plugin.GetType("SolastaCoop.PartyAutoFollow", true);
        Fresh(); Waypoint(20); Tick(10f);
        Check(ServiceRepository.Commands.Sent.Count == 2 && ServiceRepository.Commands.Sent.All(c => c.Actor.RulesetCharacter.Owner == 2 && !c.Enqueue),
            "Guest issues native replacement moves only for its two locally owned heroes");
        Waypoint(30); Tick(13f);
        Check(LocalCommands == 2 && ServiceRepository.Commands.Sent.Last(c => c.Actor == local).Target.x > 20,
            "A new host waypoint retargets the follower's active exploration walk instead of waiting forever for idle");
        Tick(14f); Tick(21f);
        Check(LocalCommands == 2, "Unchanged destination never restarts the same active walk");
        Check(ServiceRepository.Commands.Sent.All(c => c.Actor != remote), "Follower never moves host-owned characters");

        Fresh(); Waypoint(20); Tick(10f);
        Call(follower, "OnManualMove", local);
        local.DestinationPosition = new TA.int3(99, 0, 0);
        Waypoint(30); Tick(13f); Tick(20f);
        Check(LocalCommands == 1, "Manual movement relinquishes automatic ownership and remains untouched while busy");
        local.Busy = local.MovingToDestination = false; Waypoint(40); Tick(23f);
        Check(LocalCommands == 2, "Following resumes after the player's manual walk finishes");

        Fresh(); Waypoint(20); Tick(10f); local.CurrentAction = ActionDefinitions.Id.Manipulate;
        Waypoint(30); Tick(13f);
        Check(LocalCommands == 1, "An interaction action is never interrupted by follow retargeting");
        Fresh(); Waypoint(20); Tick(10f); local.DestinationPosition = new TA.int3(99, 0, 0);
        Waypoint(30); Tick(13f);
        Check(LocalCommands == 1, "A native destination mismatch also protects a walk issued outside auto-follow");

        Fresh(); Waypoint(40); Tick(10f);
        remote.MovingToDestination = false; remote.LocationPosition = new TA.int3(12, 0, 0);
        Tick(11f); Tick(14f);
        Check(!(bool)followerType.GetField("hostWaypointActive", Flags).GetValue(follower) &&
            ServiceRepository.Commands.Sent.Last(c => c.Actor == local).Target.x < 20,
            "A stopped or cancelled host route expires and tracking returns to the real leader position");

        Fresh(); Waypoint(40); Tick(10f);
        ServiceRepository.Cursor.CurrentCursor = new CursorLocationNarrativeSequence(); Tick(11f);
        Check(!(bool)followerType.GetField("hostWaypointActive", Flags).GetValue(follower), "Dialogue clears obsolete host destinations");
        local.Busy = local.MovingToDestination = false;
        ServiceRepository.Cursor.CurrentCursor = new CursorLocationExplorationCharacter();
        remote.LocationPosition = new TA.int3(20, 0, 0); Tick(12f);
        Check(LocalCommands == 2 && Enabled, "Following resumes after dialogue without toggling the button");
        Waypoint(50); Gui.Battle = new object(); Tick(13f);
        Check(!(bool)followerType.GetField("hostWaypointActive", Flags).GetValue(follower), "Combat discards exploration waypoints");
        Gui.Battle = null; Gui.GameLocation = new object(); local.Busy = false; Tick(14f);
        Check(Enabled && LocalCommands == 3, "A location change starts fresh tracking without disabling follow");

        Fresh(); remote.LocationPosition = new TA.int3(0, 3, 0); Waypoint(0, 3); Tick(10f);
        Check(LocalCommands == 1 && ServiceRepository.Commands.Sent.First(c => c.Actor == local).Target.y == 3,
            "A leader above the same grid cell is not mistaken for a nearby follower on the same floor");

        Fresh(); ServiceRepository.Commands.DelayEcho = true; Waypoint(20); Tick(10f); Waypoint(30); Tick(11f);
        Check(LocalCommands == 1, "A new waypoint cannot flood commands before the previous network echo arrives");
        Tick(13f); Check(LocalCommands == 2, "The bounded pending command is retried after its cooldown");
        Fresh(); ServiceRepository.Commands.FailNext = true; Waypoint(20); Tick(10f);
        Check(Enabled && LocalCommands == 0, "Transient command errors pause rather than permanently disable follow");
        Tick(11f); Check(LocalCommands == 0, "Transient failures respect retry backoff");
        Tick(13f); Check(LocalCommands == 1 && Enabled, "Follow recovers automatically after a transient command failure");

        Fresh(); ServiceRepository.Network.LocalPlayer = 1; Tick(10f);
        Check(ServiceRepository.Commands.Sent.Count == 2 && ServiceRepository.Commands.Sent.All(c => c.Actor.RulesetCharacter.Owner == 1),
            "Host-side following retains the same ownership restriction");

        Fresh(); object share = Activator.CreateInstance(plugin.GetType("SolastaCoop.HostMoveShare", true), Flags, null, new[] { follower }, null);
        Call(share, "Tick");
        Photon.Pun.PhotonNetwork.NetworkingClient.Receive(1, new object[] { "SolastaCoopView.host-movement/1", 1, remote.Guid.ToString(), 20, 0, 0 });
        Call(share, "Tick"); Tick(10f);
        Check(LocalCommands == 1, "Authenticated host waypoint reaches the guest through the actual Photon adapter");
        ServiceRepository.Commands.MoveCharacter(local, new TA.int3(99, 0, 0), FollowOrientation.North, 0f, FollowStance.Walk);
        int afterManual = LocalCommands;
        TimeAt(13f);
        Photon.Pun.PhotonNetwork.NetworkingClient.Receive(1, new object[] { "SolastaCoopView.host-movement/1", 2, remote.Guid.ToString(), 30, 0, 0 });
        Call(share, "Tick"); Tick(13f);
        Check(LocalCommands == afterManual, "Real movement postfix recognizes a guest's manual click and protects it");
        local.Busy = local.MovingToDestination = false;
        TimeAt(16f);
        Photon.Pun.PhotonNetwork.NetworkingClient.Receive(1, new object[] { "SolastaCoopView.host-movement/1", 3, remote.Guid.ToString(), 40, 0, 0 });
        Call(share, "Tick"); Tick(16f);
        Check(LocalCommands == afterManual + 1, "Guest follows subsequent host clicks after its manual walk ends");
        int acceptedVersion = (int)followerType.GetField("hostMoveVersion", Flags).GetValue(follower);
        Photon.Pun.PhotonNetwork.NetworkingClient.Receive(2, new object[] { "SolastaCoopView.host-movement/1", 100, remote.Guid.ToString(), 999, 0, 0 });
        Photon.Pun.PhotonNetwork.NetworkingClient.Receive(1, new object[] { "SolastaCoopView.host-movement/1", 1, remote.Guid.ToString(), 999, 0, 0 });
        Call(share, "Tick");
        Check((int)followerType.GetField("hostMoveVersion", Flags).GetValue(follower) == acceptedVersion,
            "Non-host and out-of-order movement packets cannot replace the accepted target");
        Call(share, "Dispose");
        Console.WriteLine("Auto-follow checks: PASS (timed guest/host, action state, recovery and real Harmony adapter fixtures; live pathfinding not tested)");
        return 0;
    }
}
