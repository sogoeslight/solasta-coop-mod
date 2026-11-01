using System;
using System.Reflection;
using UnityEngine;

namespace SolastaCoop
{
    // Handles only the native post-loading "press any key to continue" prompt.
    // No votes, dialogs, invitations, or warning messages are acknowledged.
    internal sealed class AutoLoadingProceed
    {
        private const float ReadyDelaySeconds = 0.65f;
        private static AutoLoadingProceed instance;
        private MethodInfo proceedMethod;
        private object currentModal;
        private float readySince;
        private bool attempted;
        private bool installed;
        private int completed;

        internal bool Enabled { get; private set; } = true;
        internal string Status { get; private set; } = "Waiting for a loading screen.";

        internal void Install()
        {
            if (installed) return;
            try
            {
                Type loadingType = Read.Type("LoadingModal");
                if (loadingType == null) throw new MissingMemberException("LoadingModal");
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public |
                    BindingFlags.DeclaredOnly;
                MethodInfo updateMethod = loadingType.GetMethod("OnUpdateHook", flags, null, Type.EmptyTypes, null);
                proceedMethod = loadingType.GetMethod("OnProceedToLocationCb", flags, null, Type.EmptyTypes, null);
                if (updateMethod == null || proceedMethod == null || proceedMethod.ReturnType != typeof(void))
                    throw new MissingMemberException("LoadingModal.OnUpdateHook / OnProceedToLocationCb");

                instance = this;
                MethodInfo postfix = typeof(AutoLoadingProceed).GetMethod("AfterLoadingUpdate",
                    BindingFlags.NonPublic | BindingFlags.Static);
                HarmonySupport.Patch(updateMethod, null, postfix);
                installed = true;
                Status = "Automatically continue after loading: ON.";
                Log.Event("core", "INFO", "Native loading screen auto-continue hook installed.");
            }
            catch (Exception ex)
            {
                Enabled = false;
                Status = "Loading auto-continue unavailable: " + ex.GetBaseException().Message;
                Log.Error("core", "Install loading screen auto-continue", ex);
            }
        }

        internal void Dispose()
        {
            currentModal = null;
            if (ReferenceEquals(instance, this)) instance = null;
        }

        internal void SetEnabled(bool enabled)
        {
            Enabled = enabled && installed;
            ResetPending();
            Status = enabled && !installed ? "Native loading screen hook is unavailable." :
                Enabled ? "Automatically continue after loading: ON." :
                "Automatically continue after loading: OFF.";
        }

        private static void AfterLoadingUpdate(object __instance)
        {
            AutoLoadingProceed current = instance;
            if (current == null || !current.Enabled || __instance == null) return;
            current.Observe(__instance);
        }

        private void ResetPending()
        {
            currentModal = null;
            readySince = 0f;
            attempted = false;
        }

        private void Observe(object modal)
        {
            try
            {
                Behaviour component = modal as Behaviour;
                if (component == null || !component.isActiveAndEnabled ||
                    component.gameObject == null || !component.gameObject.activeInHierarchy)
                {
                    ResetPending();
                    return;
                }

                object value = Read.Get(modal, "ReadyToProceed");
                if (!(value is bool) || !(bool)value)
                {
                    ResetPending();
                    return;
                }

                if (!ReferenceEquals(currentModal, modal))
                {
                    currentModal = modal;
                    readySince = Time.unscaledTime;
                    attempted = false;
                    Status = "Loading complete. Waiting to continue...";
                }

                if (attempted || Time.unscaledTime - readySince < ReadyDelaySeconds) return;
                object button = Read.Get(modal, "startButton");
                object interactable = Read.Call(button, "IsInteractable");
                if (!(interactable is bool) || !(bool)interactable) return;

                // The game's own callback checks the start button and advances loading.
                // Do not synthesize keypresses or bypass native ready-state checks.
                attempted = true;
                proceedMethod.Invoke(modal, null);
                completed++;
                Status = "Loading screen continued automatically (" + completed + ").";
                Log.Event("core", "INFO", "Native LoadingModal confirmation invoked; count=" + completed);
            }
            catch (Exception ex)
            {
                attempted = true;
                Status = "Loading auto-continue error: " + ex.GetBaseException().Message;
                Log.Error("core", "Automatically continue loading modal", ex);
            }
        }
    }
}
