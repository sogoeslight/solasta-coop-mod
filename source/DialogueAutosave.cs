using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Object = System.Object;

namespace SolastaCoop
{
    // Checkpoints precede interaction/sequence execution. Never bypass CanSave,
    // and never serialize a live narrative state (the game does not restore it).
    internal static class DialogueAutosave
    {
        private static bool installed, saving;
        private static float lastSaved = -100f;
        internal static bool Enabled { get; private set; } = true;
        internal static string Status { get; private set; } = "Dialogue autosave is not initialized.";

        internal static void Install()
        {
            if (installed) return;
            Enabled = PlayerPrefs.GetInt("SolastaCoopMod.DialogueAutosave", 1) != 0;
            try
            {
                Type gadget = Read.Type("GameGadget");
                MethodInfo interact = gadget.GetMethods(BindingFlags.Instance | BindingFlags.Public).Single(m => m.Name == "Interact");
                HarmonySupport.Patch(interact, typeof(DialogueAutosave).GetMethod("BeforeGadgetFlow", BindingFlags.NonPublic | BindingFlags.Static));
                // The base acknowledgement is an empty method. Detouring tiny
                // virtual implementations can corrupt adjacent Mono trampolines.
                // Intercept its call inside the substantial Update method instead.
                MethodInfo update = gadget.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Single(m => m.Name == "Update" && m.DeclaringType == gadget);
                HarmonySupport.Patch(update, transpiler: typeof(DialogueAutosave).GetMethod(
                    "BeforeAcknowledgement", BindingFlags.NonPublic | BindingFlags.Static));
                installed = true;
                Status = "Autosave before dialogue flows when native saving is allowed (host only).";
            }
            catch (Exception ex)
            {
                Status = "Dialogue autosave unavailable: " + ex.GetBaseException().Message;
                Log.Error("core", "Install dialogue autosave", ex);
            }
        }

        internal static void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            PlayerPrefs.SetInt("SolastaCoopMod.DialogueAutosave", enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static void BeforeGadgetFlow(object __instance, int __0)
        {
            if (!Enabled) return;
            object[] listeners = Read.Items(Read.Get(__instance, "ActiveListeners")).ToArray();
            if (__0 < 0 || __0 >= listeners.Length) return;
            if (HasDialogue(listeners[__0])) TrySave("dialogue interaction");
        }

        private static bool HasDialogue(object listener)
        {
            return Read.Items(Read.Get(listener, "FunctorParams"))
                .Any(p => { string type = Read.Get(p, "Type") as string;
                    return type == "StartDialogSequence" || type == "StartGameplayDialogSequence"; });
        }

        // Public because the helper is emitted into the game's Update body.
        public static void BeforeTrigger(object listener)
        {
            if (Enabled && HasDialogue(listener)) TrySave("dialogue trigger acknowledgement");
        }

        private static IEnumerable<CodeInstruction> BeforeAcknowledgement(IEnumerable<CodeInstruction> instructions)
        {
            var body = instructions.ToList();
            Type listener = Read.Type("GameGadgetListener");
            int matches = body.Count(i => i.operand is MethodInfo &&
                ((MethodInfo)i.operand).Name == "AcknowledgeTrigger" && ((MethodInfo)i.operand).DeclaringType == listener);
            if (matches != 1) throw new InvalidOperationException("Expected one native trigger acknowledgement call, found " + matches);
            MethodInfo before = typeof(DialogueAutosave).GetMethod("BeforeTrigger", BindingFlags.Public | BindingFlags.Static);
            foreach (CodeInstruction instruction in body)
            {
                MethodInfo method = instruction.operand as MethodInfo;
                if (method != null && method.Name == "AcknowledgeTrigger" && method.DeclaringType == listener)
                {
                    var duplicate = new CodeInstruction(OpCodes.Dup);
                    duplicate.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    duplicate.blocks.AddRange(instruction.blocks);
                    instruction.blocks.Clear();
                    yield return duplicate;
                    yield return new CodeInstruction(OpCodes.Call, before);
                }
                yield return instruction;
            }
        }

        private static void TrySave(string reason)
        {
            if (!installed || !Enabled || saving || !SessionSettings.CanEdit || Time.unscaledTime - lastSaved < 2f) return;
            try
            {
                if (Read.Get(Read.Type("Gui"), "GameLocation") == null || Read.Get(Read.Type("Gui"), "Battle") != null ||
                    Object.Equals(Read.Get(Read.Service("INarrativeDirectionService"), "IsSequenceInProgress"), true)) return;
                object service = Read.Service("IGameSerializationService");
                if (!Object.Equals(Read.Get(service, "CanSave"), true))
                {
                    Status = "Skipped dialogue checkpoint: the game currently forbids saving.";
                    return;
                }
                MethodInfo autosave = service.GetType().GetMethod("AutoSaveGame", BindingFlags.Instance | BindingFlags.Public);
                if (autosave == null) throw new MissingMethodException("GameSerializationManager.AutoSaveGame");
                saving = true;
                autosave.Invoke(service, null); // Synchronous native checkpoint before this flow continues.
                lastSaved = Time.unscaledTime;
                Status = "Requested native autosave before " + reason + ".";
                Log.Event("core", "INFO", Status);
            }
            catch (Exception ex)
            {
                Status = "Dialogue autosave failed: " + ex.GetBaseException().Message;
                Log.Error("core", "Dialogue autosave", ex);
            }
            finally { saving = false; }
        }
    }
}
