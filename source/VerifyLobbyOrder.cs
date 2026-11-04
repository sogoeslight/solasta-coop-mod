using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

public interface ISessionService { }
public interface IGameLocationCharacterService { }
public static class Gui { public static object GameLocation = new object(), Battle; }
public sealed class LobbyFixtureCanvas { public bool interactable = true; }
public sealed class LobbyFixturePanel { public bool ActionTaken; public LobbyFixtureCanvas CanvasGroup = new LobbyFixtureCanvas(); }
public sealed class LobbyFixtureSession { public object Session = new object(); }
public sealed class LobbyFixtureHero { public string Name; public ulong Guid; public int Owner, SortIndex; }
public sealed class LobbyFixtureCharacter { public LobbyFixtureHero RulesetCharacter; }
public sealed class LobbyFixtureCharacters
{
    public readonly List<LobbyFixtureCharacter> PartyCharacters = new List<LobbyFixtureCharacter>();
    public int Swaps;
    public void SwapPartyMembers(int first, int second)
    {
        PartyCharacters[first].RulesetCharacter.SortIndex = second;
        PartyCharacters[second].RulesetCharacter.SortIndex = first;
        PartyCharacters.Sort((a, b) => a.RulesetCharacter.SortIndex.CompareTo(b.RulesetCharacter.SortIndex));
        Swaps++;
    }
}
public static class ServiceRepository
{
    public static readonly LobbyFixtureSession Session = new LobbyFixtureSession();
    public static readonly LobbyFixtureCharacters Characters = new LobbyFixtureCharacters();
    public static object GetService(Type type)
    {
        if (type == typeof(ISessionService)) return Session;
        if (type == typeof(IGameLocationCharacterService)) return Characters;
        return null;
    }
}
namespace Photon.Pun
{
    public sealed class LobbyFixturePlayer
    {
        public int ActorNumber;
        public Hashtable CustomProperties = new Hashtable { { "p", "unchanged slot ownership" }, { "q", "unchanged GUID ownership" } };
    }
    public sealed class LobbyFixtureRoom
    {
        public Hashtable CustomProperties = new Hashtable { { "ch0", new byte[] { 1, 2 } }, { "ch1", new byte[] { 3, 4 } },
            { "r", "unchanged native slot/GUID mapping" }, { "other-mod", 17 } };
        public readonly Dictionary<int, LobbyFixturePlayer> Players = new Dictionary<int, LobbyFixturePlayer> {
            { 1, new LobbyFixturePlayer { ActorNumber = 1 } }, { 2, new LobbyFixturePlayer { ActorNumber = 2 } } };
        public int Writes;
        public bool Reject;
        public Hashtable LastWrite;
        public bool SetCustomProperties(Hashtable properties, Hashtable expected, object flags)
        {
            if (Reject) return false;
            LastWrite = properties; Writes++;
            foreach (DictionaryEntry item in properties) CustomProperties[item.Key] = item.Value;
            return true;
        }
    }
    public static class PhotonNetwork
    {
        public static bool InRoom = true, OfflineMode, IsMasterClient = true;
        public static LobbyFixturePlayer MasterClient = new LobbyFixturePlayer { ActorNumber = 1 };
        public static LobbyFixtureRoom CurrentRoom = new LobbyFixtureRoom();
    }
}
public static class VerifyLobbyOrder
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const string Key = "SolastaCoopMod.party-order/1";
    private static Type policy, adapter;
    private static readonly string[] Names = { "Ranger", "Wizard", "Warlock", "Paladin" };
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    private static object Call(object target, string name, params object[] args)
    { return target.GetType().GetMethod(name, Flags).Invoke(target, args); }
    private static void Set(object target, string name, object value) { target.GetType().GetField(name, Flags).SetValue(target, value); }
    private static object Get(object target, string name) { return target.GetType().GetField(name, Flags).GetValue(target); }
    private static bool Read(object payload, int host, string[] roster, out string[] order)
    {
        object[] args = { payload, host, roster, null };
        bool result = (bool)policy.GetMethod("TryRead", Flags).Invoke(null, args); order = (string[])args[3]; return result;
    }
    private static object Packet(string[] source, string[] desired, int host = 1) { return new object[] { 1, host, source, desired }; }
    private static IEnumerable<string[]> Permutations(string[] names, int start = 0)
    {
        if (start == names.Length) { yield return (string[])names.Clone(); yield break; }
        for (int i = start; i < names.Length; i++)
        {
            string[] next = (string[])names.Clone(); string tmp = next[start]; next[start] = next[i]; next[i] = tmp;
            foreach (string[] permutation in Permutations(next, start + 1)) yield return permutation;
        }
    }
    private static object Fresh()
    {
        Photon.Pun.PhotonNetwork.CurrentRoom = new Photon.Pun.LobbyFixtureRoom();
        Photon.Pun.PhotonNetwork.IsMasterClient = true; Photon.Pun.PhotonNetwork.OfflineMode = false;
        Gui.GameLocation = new object(); Gui.Battle = null; ServiceRepository.Session.Session = new object();
        ServiceRepository.Characters.PartyCharacters.Clear(); ServiceRepository.Characters.Swaps = 0;
        for (int i = 0; i < Names.Length; i++) ServiceRepository.Characters.PartyCharacters.Add(new LobbyFixtureCharacter {
            RulesetCharacter = new LobbyFixtureHero { Name = Names[i], Guid = (ulong)i + 100, Owner = i < 2 ? 1 : 2, SortIndex = i } });
        object instance = Activator.CreateInstance(adapter, true);
        Set(instance, "room", Photon.Pun.PhotonNetwork.CurrentRoom); Set(instance, "visible", true);
        Set(instance, "panel", new LobbyFixturePanel()); Set(instance, "roster", (string[])Names.Clone()); Set(instance, "order", (string[])Names.Clone());
        return instance;
    }
    public static int Main(string[] args)
    {
        var plugin = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        policy = plugin.GetType("SolastaCoop.LobbyOrderPolicy", true); adapter = plugin.GetType("SolastaCoop.LobbyPartyOrder", true);
        string[] desired = { "Paladin", "Wizard", "Warlock", "Ranger" }, parsed;
        Check(Read(Packet(Names, desired), 1, Names, out parsed) && parsed.SequenceEqual(desired), "Shared order accepts the current host and exact hero membership");
        parsed[0] = "changed";
        Check(desired[0] == "Paladin", "Reading a shared order cannot mutate its network payload");
        Check(!Read(Packet(Names, desired, 2), 1, Names, out parsed) && !Read(Packet(Names, desired), 2, Names, out parsed),
            "Orders tagged with another or previous host are ignored");
        Check(!Read(Packet(Names, new[] { "Ranger", "Ranger", "Warlock", "Paladin" }), 1, Names, out parsed) &&
            !Read(Packet(Names, new[] { "Ranger", "Wizard" }), 1, Names, out parsed) &&
            !Read(Packet(Names, desired), 1, new[] { "A", "B", "C", "D" }, out parsed),
            "Duplicate, missing and stale-save heroes cannot reorder the current party");
        Check(!Read(new object[] { 9, 1, Names, desired }, 1, Names, out parsed) && !Read(null, 1, Names, out parsed) &&
            !Read(Packet(new[] { "", "A" }, new[] { "A", "" }), 1, new[] { "", "A" }, out parsed),
            "Malformed versions, empty slots and incomplete packets are rejected");
        int checkedOrders = 0;
        foreach (string[] permutation in Permutations(Names))
        {
            string[] working = (string[])Names.Clone();
            var plan = (List<int[]>)policy.GetMethod("Plan", Flags).Invoke(null, new object[] { working, permutation });
            foreach (int[] pair in plan) { string tmp = working[pair[0]]; working[pair[0]] = working[pair[1]]; working[pair[1]] = tmp; }
            if (!working.SequenceEqual(permutation) || plan.Count > 3) throw new Exception("Invalid swap plan");
            checkedOrders++;
        }
        Check(checkedOrders == 24, "All 24 four-hero permutations reduce to at most three native swaps");

        object host = Fresh(); var room = Photon.Pun.PhotonNetwork.CurrentRoom;
        object snapshot = room.CustomProperties["ch0"], mapping = room.CustomProperties["r"];
        Call(host, "SelectSlot", 0); Check((string)Get(host, "selected") == "Ranger" && room.Writes == 0, "First click selects a source without publishing");
        Call(host, "SelectSlot", 0); Check(Get(host, "selected") == null && room.Writes == 0, "Clicking the source again cancels the swap");
        Call(host, "SelectSlot", 0); Call(host, "SelectSlot", 3);
        Check(room.Writes == 1 && room.LastWrite.Count == 1 && room.LastWrite.Contains(Key) &&
            Read(room.CustomProperties[Key], 1, Names, out parsed) && parsed.SequenceEqual(desired),
            "Two slot clicks publish one complete host order");
        Check(ReferenceEquals(snapshot, room.CustomProperties["ch0"]) && ReferenceEquals(mapping, room.CustomProperties["r"]) &&
            room.Players.Values.All(p => (string)p.CustomProperties["p"] == "unchanged slot ownership" &&
                (string)p.CustomProperties["q"] == "unchanged GUID ownership"),
            "Native character snapshots, slot/GUID mappings and player ownership properties are untouched");
        Call(host, "SelectSlot", 1); Call(host, "SelectSlot", 2);
        Check(room.Writes == 1, "A pending publish prevents overlapping swaps");
        Photon.Pun.PhotonNetwork.IsMasterClient = false; object guest = Activator.CreateInstance(adapter, true);
        Set(guest, "room", room); Set(guest, "visible", true); Set(guest, "roster", Names); Set(guest, "order", Names);
        Check(!(bool)Call(guest, "Publish", (object)desired) && room.Writes == 1, "Guests can read the shared order but cannot edit it");

        Photon.Pun.PhotonNetwork.IsMasterClient = true;
        object blocked = Fresh(); ((LobbyFixturePanel)Get(blocked, "panel")).CanvasGroup.interactable = false;
        Check(!(bool)Call(blocked, "Publish", (object)desired), "A native modal blocking the lobby also blocks swap controls");
        ((LobbyFixturePanel)Get(blocked, "panel")).CanvasGroup.interactable = true;
        ((LobbyFixturePanel)Get(blocked, "panel")).ActionTaken = true;
        Check(!(bool)Call(blocked, "Publish", (object)desired), "Launching a game closes the edit window");
        object failed = Fresh(); Photon.Pun.PhotonNetwork.CurrentRoom.Reject = true;
        Check(!(bool)Call(failed, "Publish", (object)desired) && Get(failed, "pending") == null,
            "Rejected publishing creates no optimistic shared or pending order");
        object departed = Fresh(); var oldRoom = Photon.Pun.PhotonNetwork.CurrentRoom;
        Photon.Pun.PhotonNetwork.CurrentRoom = new Photon.Pun.LobbyFixtureRoom();
        Check(!(bool)Call(departed, "Publish", (object)desired) && oldRoom.Writes == 0 && Photon.Pun.PhotonNetwork.CurrentRoom.Writes == 0,
            "A room change between rendering and clicking cannot publish stale lobby edits");

        object loaded = Fresh(); room = Photon.Pun.PhotonNetwork.CurrentRoom; room.CustomProperties[Key] = Packet(Names, desired);
        var heroes = ServiceRepository.Characters.PartyCharacters.Select(c => c.RulesetCharacter).ToArray();
        var owners = heroes.ToDictionary(h => h.Guid, h => h.Owner);
        Call(loaded, "ApplyInGame");
        Check(ServiceRepository.Characters.PartyCharacters.Select(c => c.RulesetCharacter.Name).SequenceEqual(desired) &&
            ServiceRepository.Characters.Swaps == 1 && ServiceRepository.Characters.PartyCharacters.All(c =>
                heroes.Contains(c.RulesetCharacter) && owners[c.RulesetCharacter.Guid] == c.RulesetCharacter.Owner),
            "Loaded party order uses native swaps while preserving hero instances, GUIDs and owners");
        ServiceRepository.Characters.SwapPartyMembers(0, 1); int swaps = ServiceRepository.Characters.Swaps;
        Call(loaded, "ApplyInGame"); Gui.GameLocation = new object(); Call(loaded, "ApplyInGame");
        Check(ServiceRepository.Characters.Swaps == swaps, "Later manual in-game ordering and location changes are not overwritten");
        ServiceRepository.Session.Session = new object(); Call(loaded, "ApplyInGame");
        Check(ServiceRepository.Characters.PartyCharacters.Select(c => c.RulesetCharacter.Name).SequenceEqual(desired),
            "A newly loaded session can apply the lobby order again");
        object joiningGuest = Fresh(); room = Photon.Pun.PhotonNetwork.CurrentRoom;
        room.CustomProperties[Key] = Packet(Names, desired); Photon.Pun.PhotonNetwork.IsMasterClient = false;
        Gui.Battle = new object(); Call(joiningGuest, "ApplyInGame");
        Check(ServiceRepository.Characters.Swaps == 0, "An active battle defers pending initial party ordering");
        Gui.Battle = null; Call(joiningGuest, "ApplyInGame");
        Check(ServiceRepository.Characters.PartyCharacters.Select(c => c.RulesetCharacter.Name).SequenceEqual(desired),
            "A guest joining later applies the shared order without writing room or ownership properties");
        Console.WriteLine("Lobby order checks: PASS (policy, host/guest publishing, ownership and loading fixtures; Unity card layout not rendered)");
        return 0;
    }
}
