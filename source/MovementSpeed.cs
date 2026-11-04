using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Object = System.Object;

namespace SolastaCoop
{
    internal static class MovementSpeed
    {
        private static bool installed;
        internal static string Status { get; private set; } = "Movement speed is not initialized.";

        internal static float Factor
        {
            get
            {
                if (!installed || Read.Get(Read.Type("Gui"), "GameLocation") == null ||
                    Object.Equals(Read.Get(Read.Service("INarrativeDirectionService"), "IsSequenceInProgress"), true)) return 1f;
                return Read.Get(Read.Type("Gui"), "Battle") != null ? CombatSpeed.Factor : CombatSpeed.ExplorationFactor;
            }
        }

        internal static void Install()
        {
            if (installed) return;
            try
            {
                Type world = Read.Type("WorldLocationCharacter");
                MethodInfo elapsed = world.GetMethod("GetForwardedAdjustedTime", BindingFlags.Instance | BindingFlags.NonPublic);
                if (elapsed == null || elapsed.ReturnType != typeof(float) || elapsed.GetParameters().Length != 2)
                    throw new MissingMethodException("WorldLocationCharacter.GetForwardedAdjustedTime");
                HarmonySupport.Patch(elapsed, typeof(MovementSpeed).GetMethod("ForwardTime", BindingFlags.Static | BindingFlags.NonPublic));
                MethodInfo wait = world.GetMethod("WaitForSeconds", BindingFlags.Instance | BindingFlags.NonPublic);
                HarmonySupport.Patch(wait, typeof(MovementSpeed).GetMethod("ShortenWait", BindingFlags.Static | BindingFlags.NonPublic));
                MethodInfo eventWait = world.GetMethod("WaitForEvent", BindingFlags.Instance | BindingFlags.NonPublic);
                var iterator = eventWait.GetCustomAttributes(typeof(IteratorStateMachineAttribute), false)
                    .Cast<IteratorStateMachineAttribute>().Single().StateMachineType;
                MethodInfo moveNext = iterator.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                HarmonySupport.Patch(moveNext, transpiler: typeof(MovementSpeed).GetMethod("ShortenBakedWait", BindingFlags.Static | BindingFlags.NonPublic));
                MethodInfo walking = world.GetMethod("AnimateMove", BindingFlags.Instance | BindingFlags.NonPublic);
                Type walkingIterator = walking.GetCustomAttributes(typeof(IteratorStateMachineAttribute), false)
                    .Cast<IteratorStateMachineAttribute>().Single().StateMachineType;
                HarmonySupport.Patch(walkingIterator.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                    transpiler: typeof(MovementSpeed).GetMethod("NormalizeWalkAnimation", BindingFlags.Static | BindingFlags.NonPublic));
                installed = true;
                Status = "Native movement interpolation and baked movement waits are accelerated.";
            }
            catch (Exception ex)
            {
                Status = "Movement acceleration unavailable: " + ex.GetBaseException().Message;
                Log.Error("core", "Install movement speed", ex);
            }
        }

        private static void ForwardTime(object __instance, ref float __0)
        {
            float factor = Factor;
            if (factor <= 1f) return;
            // The native method adds one delta; add only the extra portion.
            __0 += Time.deltaTime * (factor - 1f);
            CombatSpeed.AccelerateCharacter(__instance, factor);
        }

        private static void ShortenWait(ref float __0) { __0 = ScaleDuration(__0); }

        // Public because this call is emitted into the game's iterator body.
        public static float ScaleDuration(float seconds) { return seconds / Factor; }

        // Walking derives its animation blend speed from displacement/delta.
        // Compensate that delta because Animator playback already applies Factor.
        public static float AnimationDelta() { return Time.deltaTime * Factor; }

        private static IEnumerable<CodeInstruction> NormalizeWalkAnimation(IEnumerable<CodeInstruction> instructions)
        {
            var body = instructions.ToList();
            int matches = 0;
            foreach (CodeInstruction instruction in body)
            {
                MethodInfo method = instruction.operand as MethodInfo;
                if (method != null && method.DeclaringType == typeof(Time) && method.Name == "get_deltaTime")
                {
                    instruction.operand = typeof(MovementSpeed).GetMethod("AnimationDelta", BindingFlags.Static | BindingFlags.Public);
                    matches++;
                }
            }
            if (matches != 1) throw new InvalidOperationException("Expected one walking animation delta, found " + matches);
            return body;
        }

        private static IEnumerable<CodeInstruction> ShortenBakedWait(IEnumerable<CodeInstruction> instructions)
        {
            var body = instructions.ToList();
            MethodInfo scale = typeof(MovementSpeed).GetMethod("ScaleDuration", BindingFlags.Static | BindingFlags.Public);
            int matches = body.Count(i => i.operand is MethodInfo && ((MethodInfo)i.operand).Name == "DurationInFrames");
            if (matches != 1) throw new InvalidOperationException("Expected one native baked movement wait, found " + matches);
            foreach (CodeInstruction instruction in body)
            {
                MethodInfo method = instruction.operand as MethodInfo;
                if (method != null && method.Name == "DurationInFrames")
                {
                    var call = new CodeInstruction(OpCodes.Call, scale);
                    call.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    call.blocks.AddRange(instruction.blocks);
                    instruction.blocks.Clear();
                    yield return call;
                }
                yield return instruction;
            }
        }
    }
}
