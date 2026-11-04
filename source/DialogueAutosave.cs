using System;
using System.Linq;
using System.Reflection;
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
                Type listener = Read.Type("GameGadgetListener");
                foreach (Type type in listener.Assembly.GetTypes().Where(t => t == listener || t.IsSubclassOf(listener)))
                    foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .Where(m => m.Name == "AcknowledgeTrigger" && !m.IsAbstract))
                        HarmonySupport.Patch(method, typeof(DialogueAutosave).GetMethod("BeforeTrigger", BindingFlags.NonPublic | BindingFlags.Static));
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

        private static void BeforeTrigger(object __instance)
        {
            if (Enabled && HasDialogue(__instance)) TrySave("dialogue trigger acknowledgement");
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
