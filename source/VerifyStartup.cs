using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

public interface INetworkingService { }
public interface INarrativeDirectionService { }
public interface IGameSerializationService { }
public static class ServiceRepository
{
    public static readonly FakeNetwork Network = new FakeNetwork();
    public static readonly FakeNarrative Narrative = new FakeNarrative();
    public static readonly FakeSerialization Serialization = new FakeSerialization();
    public static object GetService(Type type)
    {
        if (type == typeof(INetworkingService)) return Network;
        if (type == typeof(INarrativeDirectionService)) return Narrative;
        if (type == typeof(IGameSerializationService)) return Serialization;
        return null;
    }
}
public static class StartupEvents { public static readonly List<string> Events = new List<string>(); }
public sealed class FakeNetwork : INetworkingService { public bool IsMultiplayerGame, IsMasterClient = true; }
public sealed class FakeNarrative : INarrativeDirectionService { public bool IsSequenceInProgress; }
public sealed class FakeSerialization : IGameSerializationService
{
    public bool CanSave = true;
    public void AutoSaveGame() { StartupEvents.Events.Add("save"); }
}
public static class Gui { public static object GameLocation, Battle; }
public static class NetworkingDefinitions
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static uint DurationInFrames(float seconds) { return (uint)Math.Ceiling(seconds / 0.02f); }
}
public sealed class WorldLocationCharacter
{
    public object GraphicsCharacter { get { return null; } }
    [MethodImpl(MethodImplOptions.NoInlining)] private float GetForwardedAdjustedTime(float current, float duration) { return current + Time.deltaTime; }
    private IEnumerator WaitForSeconds(float seconds) { yield return seconds; }
    private IEnumerator WaitForEvent(int kind, bool alternate = false, bool elapsed = false) { yield return NetworkingDefinitions.DurationInFrames(0.6f); }
    private IEnumerator AnimateMove(float distance) { yield return distance / Time.deltaTime; }
}
public sealed class NarrativeStateAnswerChoice
{
    public int Calls, Winner;
    [MethodImpl(MethodImplOptions.NoInlining)] public void Begin(object[] args) { Calls++; if (args == null) throw new ArgumentNullException("args"); }
    [MethodImpl(MethodImplOptions.NoInlining)] private void GetVoteWinner(out int answer, out bool everyone) { Calls++; answer = 0; everyone = false; }
    [MethodImpl(MethodImplOptions.NoInlining)] private void CompleteVotingSession(int answer) { Calls++; Winner = answer; }
}
public sealed class FakeFunctor { public string Type = "StartDialogSequence"; }
public class GameGadgetListener
{
    public readonly List<FakeFunctor> FunctorParams = new List<FakeFunctor> { new FakeFunctor() };
    // Deliberately preserve the real game's empty base implementation.
    public virtual void AcknowledgeTrigger() { }
}
public sealed class TestListener : GameGadgetListener
{
    public override void AcknowledgeTrigger() { StartupEvents.Events.Add("acknowledge"); }
}
public sealed class GameGadget
{
    public readonly List<GameGadgetListener> ActiveListeners = new List<GameGadgetListener> { new TestListener() };
    [MethodImpl(MethodImplOptions.NoInlining)] public void Interact(int index, object actor)
    {
        if (index < 0 || index >= ActiveListeners.Count) throw new ArgumentOutOfRangeException("index");
        StartupEvents.Events.Add("interact");
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public bool Update(object location, object battle, bool fastTravel, ref bool interrupted)
    {
        var listener = ActiveListeners[0];
        listener.AcknowledgeTrigger();
        StartupEvents.Events.Add("execute");
        return !interrupted;
    }
    public object ItemContainer;
}
public sealed class WorldGadget { public GameGadget GameGadget = new GameGadget(); }
public sealed class FakeContainer
{
    public bool HasBeenOpenedOnce = true;
    public List<object> InventorySlots = new List<object>();
}
public sealed class WorldGadgetHighlightSwapper
{
    public bool wantedVisible = true, visible;
    public WorldGadget worldGadget = new WorldGadget();
    [MethodImpl(MethodImplOptions.NoInlining)] public void RefreshVisibility()
    {
        if (visible == wantedVisible) return;
        visible = wantedVisible;
        StartupEvents.Events.Add("highlight");
    }
}
public sealed class ContainerLootModal
{
    public object Hero, Container;
    [MethodImpl(MethodImplOptions.NoInlining)] public void ShowContainerLoot(object hero, object container) { Hero = hero; Container = container; }
}
public sealed class ProximityLootModal
{
    public object Hero; public int Calls;
    [MethodImpl(MethodImplOptions.NoInlining)] public void ShowProximityLoot(object hero) { Hero = hero; Calls++; }
}
public static class VerifyStartup
{
    private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    private static object PatchInfo(MethodInfo method)
    {
        Type harmony = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("HarmonyLib.Harmony", false)).FirstOrDefault(t => t != null);
        return harmony == null ? null : harmony.GetMethod("GetPatchInfo", Flags).Invoke(null, new object[] { method });
    }
    public static int Main(string[] args)
    {
        Assembly plugin = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        if (args.Length > 1 && args[1] == "baseline")
        {
            plugin.GetType("SolastaCoop.DialogueAutosave", true).GetMethod("Install", Flags).Invoke(null, null);
            Console.WriteLine("BASELINE: old installer returned; invoking empty native-shape acknowledgement.");
            typeof(GameGadgetListener).GetMethod("AcknowledgeTrigger").Invoke(new GameGadgetListener(), null);
            return 0;
        }
        Type initialization = plugin.GetType("SolastaCoop.GameplayFeatures", true);
        var initialize = initialization.GetMethod("InitializeIfReady", Flags);
        initialize.Invoke(null, null);
        Check(!(bool)initialization.GetProperty("Initialized", Flags).GetValue(null, null) &&
            PatchInfo(typeof(GameGadget).GetMethod("Update")) == null,
            "Bootstrap/menu creates no new gameplay detours before a location exists");
        Gui.GameLocation = new object();
        initialize.Invoke(null, null);
        foreach (string name in new[] { "MovementSpeed", "DialogueChoices", "DialogueAutosave", "LootConvenience" })
            Check((bool)plugin.GetType("SolastaCoop." + name, true).GetField("installed", Flags).GetValue(null),
                "Real Harmony initializes " + name + " after location load");
        Type speed = plugin.GetType("SolastaCoop.CombatSpeed", true);
        Check(Math.Abs((float)speed.GetProperty("Factor", Flags).GetValue(null, null) - 1.5f) < 0.001f &&
            Math.Abs((float)speed.GetProperty("ExplorationFactor", Flags).GetValue(null, null) - 1.75f) < 0.001f,
            "Default preferences load combat x1.5 and exploration x1.75");
        Check(PatchInfo(typeof(GameGadgetListener).GetMethod("AcknowledgeTrigger")) == null &&
            PatchInfo(typeof(TestListener).GetMethod("AcknowledgeTrigger")) == null,
            "Neither empty base acknowledgement nor its overrides are detoured");
        typeof(GameGadgetListener).GetMethod("AcknowledgeTrigger").Invoke(new GameGadgetListener(), null);
        Console.WriteLine("PASS Empty acknowledgement remains safely callable");
        var gadget = new GameGadget(); bool interrupted = false;
        StartupEvents.Events.Clear(); gadget.Update(null, null, false, ref interrupted);
        Check(StartupEvents.Events.SequenceEqual(new[] { "save", "acknowledge", "execute" }),
            "Call-site hook saves before acknowledgement and preserves native flow order");
        Type autosave = plugin.GetType("SolastaCoop.DialogueAutosave", true);
        autosave.GetField("lastSaved", Flags).SetValue(null, -100f);
        ServiceRepository.Serialization.CanSave = false;
        StartupEvents.Events.Clear(); gadget.Update(null, null, false, ref interrupted);
        Check(StartupEvents.Events.SequenceEqual(new[] { "acknowledge", "execute" }),
            "Native save prohibition is respected without blocking dialogue execution");
        var swapper = new WorldGadgetHighlightSwapper(); swapper.worldGadget.GameGadget.ItemContainer = new FakeContainer();
        swapper.RefreshVisibility();
        Check(swapper.wantedVisible && !swapper.visible, "Empty highlight filtering preserves the native visibility request");
        initialize.Invoke(null, null);
        Check((bool)initialization.GetProperty("Initialized", Flags).GetValue(null, null), "Repeated initialization stays idempotent");
        Console.WriteLine("Startup Harmony checks: PASS (including a genuinely empty virtual native-shape method)");
        return 0;
    }
}
