using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

public interface INetworkingService { }
public static class ServiceRepository
{
    public static readonly FakeNetwork Network = new FakeNetwork();
    public static object GetService(Type type) { return type == typeof(INetworkingService) ? Network : null; }
}
public sealed class FakeNetwork : INetworkingService
{
    public bool IsMultiplayerGame = true, IsMasterClient = true;
    public int MasterClientNumber = 7;
    public FakeTick Tick = new FakeTick();
    public List<FakePlayer> GetPlayersInRoom() { return new List<FakePlayer> { new FakePlayer { actorNumber = 7 }, new FakePlayer { actorNumber = 19 } }; }
}
public sealed class FakeTick { public long LocalTick = 100; }
public sealed class FakePlayer { public int actorNumber; }
public sealed class FakeTree { public string Name = "test-dialogue"; }
public sealed class FakeDescription { public int Index = 3; }
public sealed class FakeSequence { public FakeTree NarrativeTreeDefinition = new FakeTree(); }
public sealed class FakeProperties : Hashtable { }
public sealed class FakeRoom
{
    public IDictionary CustomProperties = new FakeProperties();
    public int Publishes;
    public bool SetCustomProperties(FakeProperties values, FakeProperties expected, object flags)
    {
        foreach (DictionaryEntry entry in values) CustomProperties[entry.Key] = entry.Value;
        Publishes++;
        return true;
    }
}
namespace Photon.Pun
{
    public static class PhotonNetwork { public static FakeRoom CurrentRoom = new FakeRoom(); }
}
public sealed class NarrativeStateAnswerChoice
{
    public Dictionary<int, List<int>> playerVotesPerChoice = new Dictionary<int, List<int>>();
    public FakeSequence NarrativeSequence = new FakeSequence();
    public FakeDescription NarrativeStateDescription = new FakeDescription();
    public int Winner = -1, Completions, Begins;
    [MethodImpl(MethodImplOptions.NoInlining)] public void Begin(object[] args) { Begins++; if (args == null) throw new ArgumentNullException("args"); }
    [MethodImpl(MethodImplOptions.NoInlining)] private void GetVoteWinner(out int selectedIndex, out bool everyoneVoted)
    {
        selectedIndex = 4;
        everyoneVoted = playerVotesPerChoice.Values.Sum(v => v.Count) >= 2;
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private void CompleteVotingSession(int answer)
    {
        Winner = answer;
        Completions++;
    }
    public void Attempt(bool timeout = false)
    {
        int answer; bool everyone;
        GetVoteWinner(out answer, out everyone);
        if (everyone || timeout) CompleteVotingSession(answer);
    }
}
public static class VerifyDialogue
{
    private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
    public static int Main(string[] args)
    {
        Assembly plugin = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Type dialogue = plugin.GetType("SolastaCoop.DialogueChoices", true);
        Type mode = plugin.GetType("SolastaCoop.DialogueMode", true);
        Action<int> setMode = value => dialogue.GetMethod("SetMode", Flags).Invoke(null, new[] { Enum.ToObject(mode, value) });
        dialogue.GetMethod("Install", Flags).Invoke(null, null);
        Console.WriteLine(dialogue.GetProperty("Status", Flags).GetValue(null, null));
        Check((bool)dialogue.GetField("installed", Flags).GetValue(null), "Real Harmony installs all three native-shape dialogue hooks");
        setMode(1);
        var follow = new NarrativeStateAnswerChoice(); follow.Begin(new object[0]);
        follow.playerVotesPerChoice[9] = new List<int> { 19 };
        follow.Attempt(true);
        Check(follow.Completions == 0, "Follow-host waits beyond vanilla timeout when only guest voted");
        follow.playerVotesPerChoice[4] = new List<int> { 7 };
        follow.Attempt();
        Check(follow.Winner == 4 && follow.Completions == 1, "Host answer completes the native session");
        var hostOnly = new NarrativeStateAnswerChoice(); ServiceRepository.Network.Tick.LocalTick++;
        hostOnly.Begin(new object[0]); hostOnly.playerVotesPerChoice[9] = new List<int> { 7 }; hostOnly.Attempt();
        Check(hostOnly.Winner == 9, "Follow-host needs no guest click");
        setMode(2);
        int[] counts = new int[2];
        for (int i = 0; i < 1000; i++)
        {
            ServiceRepository.Network.IsMasterClient = false;
            ServiceRepository.Network.Tick.LocalTick++;
            var guest = new NarrativeStateAnswerChoice(); guest.Begin(new object[0]);
            ServiceRepository.Network.IsMasterClient = true;
            var host = new NarrativeStateAnswerChoice(); host.Begin(new object[0]);
            host.playerVotesPerChoice[4] = new List<int> { 7 }; host.playerVotesPerChoice[9] = new List<int> { 19 };
            host.Attempt();
            CheckSilent(host.Winner == 4 || host.Winner == 9, "Winner belongs to real votes");
            counts[host.Winner == 4 ? 0 : 1]++;
            ServiceRepository.Network.IsMasterClient = false;
            guest.playerVotesPerChoice[9] = new List<int> { 19 }; guest.playerVotesPerChoice[4] = new List<int> { 7 };
            guest.Attempt();
            CheckSilent(guest.Winner == host.Winner, "Guest uses the host-authored outcome, never an independent draw");
        }
        Check(counts[0] > 400 && counts[0] < 600, "1000 real host resolutions include both choices with approximately equal frequency");
        var early = new NarrativeStateAnswerChoice(); early.Begin(new object[0]);
        early.playerVotesPerChoice[4] = new List<int> { 7 }; early.playerVotesPerChoice[9] = new List<int> { 19 }; early.Attempt();
        Check(early.Completions == 0, "A repeated choice at the same native tick cannot reuse a previous published outcome");
        ServiceRepository.Network.IsMasterClient = true;
        var newer = new NarrativeStateAnswerChoice(); newer.Begin(new object[0]);
        newer.playerVotesPerChoice[4] = new List<int> { 7 }; newer.playerVotesPerChoice[9] = new List<int> { 19 }; newer.Attempt();
        ServiceRepository.Network.IsMasterClient = false; early.Attempt();
        Check(early.Winner == newer.Winner, "Guest completion resumes after delayed host outcome arrives");
        setMode(0);
        var vanilla = new NarrativeStateAnswerChoice(); vanilla.Begin(new object[0]);
        vanilla.playerVotesPerChoice[9] = new List<int> { 7, 19 }; vanilla.Attempt();
        Check(vanilla.Winner == 4, "Vanilla mode preserves the original resolver");
        Console.WriteLine("Dialogue Harmony checks: PASS; random distribution " + counts[0] + "/" + counts[1]);
        return 0;
    }
    private static void CheckSilent(bool condition, string name) { if (!condition) throw new Exception(name); }
}
