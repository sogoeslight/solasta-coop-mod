using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

public interface INarrativeDirectionService { }
public sealed class FakeNarrative : INarrativeDirectionService { public bool IsSequenceInProgress; }
public static class ServiceRepository
{
    public static FakeNarrative Narrative = new FakeNarrative();
    public static object GetService(Type type) { return type == typeof(INarrativeDirectionService) ? Narrative : null; }
}
public static class Gui { public static object GameLocation = new object(), Battle = new object(); }
public static class NetworkingDefinitions
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static uint DurationInFrames(float seconds) { return (uint)Math.Ceiling(seconds / 0.02f); }
}
public sealed class WorldLocationCharacter
{
    public object GraphicsCharacter { get { return null; } }
    [MethodImpl(MethodImplOptions.NoInlining)] private float GetForwardedAdjustedTime(float current, float duration) { return current + Time.deltaTime; }
    [MethodImpl(MethodImplOptions.NoInlining)] private IEnumerator WaitForSeconds(float seconds) { yield return seconds; }
    private IEnumerator WaitForEvent(int kind, bool alternate = false, bool elapsed = false) { yield return NetworkingDefinitions.DurationInFrames(0.6f); }
    private IEnumerator AnimateMove(float distance) { yield return distance / Time.deltaTime; }
    public int Move(float duration, out float position)
    {
        float current = 0f; int frames = 0;
        while (current < duration - 0.00001f) { current = GetForwardedAdjustedTime(current, duration); frames++; }
        position = Math.Min(current / duration, 1f);
        return frames;
    }
    public float WaitSeconds() { var iterator = WaitForSeconds(0.6f); iterator.MoveNext(); return (float)iterator.Current; }
    public uint WaitFrames() { var iterator = WaitForEvent(1); iterator.MoveNext(); return (uint)iterator.Current; }
    public float AnimationSpeed() { var iterator = AnimateMove(0.04f); iterator.MoveNext(); return (float)iterator.Current; }
}
public static class VerifyMovement
{
    private const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    public static int Main(string[] args)
    {
        Assembly plugin = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Type movement = plugin.GetType("SolastaCoop.MovementSpeed", true), speed = plugin.GetType("SolastaCoop.CombatSpeed", true);
        movement.GetMethod("Install", Flags).Invoke(null, null);
        Console.WriteLine(movement.GetProperty("Status", Flags).GetValue(null, null));
        Check((bool)movement.GetField("installed", Flags).GetValue(null), "Real Harmony installs interpolation, wait and walking-animation hooks");
        Action<float> set = value => speed.GetMethod("SetFactor", Flags).Invoke(null, new object[] { value });
        var world = new WorldLocationCharacter(); float position;
        foreach (float factor in new[] { 1f, 2f, 3f })
        {
            set(factor);
            int frames = world.Move(1f, out position);
            Check(frames == (int)Math.Ceiling(50f / factor) && Math.Abs(position - 1f) < 0.001f,
                "Combat x" + factor + " finishes a one-second move in " + frames + " frames at the same destination");
            Check(Math.Abs(world.WaitSeconds() - 0.6f / factor) < 0.001f && world.WaitFrames() <= Math.Ceiling(30f / factor) + 1,
                "Combat x" + factor + " scales movement waits and baked frame waits");
            Check(Math.Abs(world.AnimationSpeed() - 2f / factor) < 0.001f,
                "Walking blend compensates for the separately accelerated Animator at x" + factor);
        }
        Gui.Battle = null;
        speed.GetMethod("SetExplorationFactor", Flags).Invoke(null, new object[] { 2f });
        set(3f);
        Check(world.Move(1f, out position) == 25, "Exploration uses its own factor independently of combat");
        ServiceRepository.Narrative.IsSequenceInProgress = true;
        Check(world.Move(1f, out position) == 50 && Math.Abs(world.WaitSeconds() - 0.6f) < 0.001f,
            "Cinematic movement and waits remain at vanilla timing");
        ServiceRepository.Narrative.IsSequenceInProgress = false;
        Gui.GameLocation = null;
        Check(world.Move(1f, out position) == 50, "No loaded location keeps native movement timing");
        Console.WriteLine("Movement Harmony checks: PASS (isolated Unity time shim, native-shape iterators)");
        return 0;
    }
}
