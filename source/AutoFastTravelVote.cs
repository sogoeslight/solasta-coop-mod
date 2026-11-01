using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SolastaCoop
{
    // Accepts only the native fast-travel vote modal. Does not touch multiplayer
    // story votes, rest confirmation, invitations, or loading confirmations.
    internal sealed class AutoFastTravelVote
    {
        private const float ConfirmationDelay = 0.55f;
        private const float VisibilityTimeout = 4f;
        private static AutoFastTravelVote instance;
        private Type fastTravelType;
        private object pending;
        private float pendingSince;
        private bool installed;
        private int accepted;

        internal bool Enabled { get; private set; } = true;
        internal string Status { get; private set; } = "Автопринятие голосования за быстрое перемещение: включено.";
        internal bool VotePending { get { return pending != null; } }

        internal void Install()
        {
            if (installed) return;
            instance = this;
            try
            {
                fastTravelType = Read.Type("MultiplayerFastTravelVoteModal");
                Type baseType = Read.Type("MultiplayerVoteModal");
                if (fastTravelType == null || baseType == null)
                    throw new MissingMemberException("MultiplayerFastTravelVoteModal / MultiplayerVoteModal");
                MethodInfo showVote = baseType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .SingleOrDefault(m => m.Name == "ShowVote" && m.GetParameters().Length == 4);
                MethodInfo afterShow = typeof(AutoFastTravelVote).GetMethod("AfterVoteShown",
                    BindingFlags.Static | BindingFlags.NonPublic);
                HarmonySupport.Patch(showVote, null, afterShow);
                installed = true;
                Status = "Автопринятие быстрого перемещения готово.";
                Log.Event("core", "INFO", "Fast-travel vote auto-accept: native ShowVote hook installed.");
            }
            catch (Exception ex)
            {
                Enabled = false;
                Status = "Автопринятие недоступно: " + ex.GetBaseException().Message;
                Log.Error("core", "Install fast-travel vote hook", ex);
            }
        }

        internal void Dispose()
        {
            pending = null;
            if (ReferenceEquals(instance, this)) instance = null;
        }

        internal void SetEnabled(bool enabled)
        {
            Enabled = enabled && installed;
            if (!Enabled) pending = null;
            Status = enabled && !installed ? "Не удалось подключить штатный обработчик голосований." :
                (Enabled ? "Быстрое перемещение принимается автоматически." : "Автопринятие выключено.");
        }

        private static void AfterVoteShown(object __instance)
        {
            AutoFastTravelVote current = instance;
            if (current == null || !current.Enabled || __instance == null || current.fastTravelType == null ||
                !current.fastTravelType.IsInstanceOfType(__instance)) return;
            current.pending = __instance;
            current.pendingSince = Time.unscaledTime;
            current.Status = "Обнаружено голосование за быстрое перемещение. Ожидание появления окна.";
            Log.Event("core", "INFO", "Fast-travel vote shown; auto-accept queued.");
        }

        internal void Tick()
        {
            if (!Enabled || pending == null) return;
            float elapsed = Time.unscaledTime - pendingSince;
            if (elapsed < ConfirmationDelay) return;
            try
            {
                // GUI modal is a Unity component. Never invoke the vote action on
                // an inactive or already closed modal.
                Behaviour behaviour = pending as Behaviour;
                if (behaviour == null || !behaviour.isActiveAndEnabled ||
                    behaviour.gameObject == null || !behaviour.gameObject.activeInHierarchy)
                {
                    if (elapsed > VisibilityTimeout)
                    {
                        pending = null;
                        Status = "Окно голосования исчезло до автопринятия.";
                    }
                    return;
                }
                MethodInfo accept = Read.Type("MultiplayerVoteModal").GetMethod("ProcessVoteAccepted",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (accept == null) throw new MissingMethodException("MultiplayerVoteModal.ProcessVoteAccepted");
                object modal = pending;
                pending = null; // At most one acceptance per ShowVote invocation.
                accept.Invoke(modal, null);
                accepted++;
                Status = "Быстрое перемещение принято автоматически (" + accepted + ").";
                Log.Event("core", "INFO", "Fast-travel vote accepted through native modal; count=" + accepted);
            }
            catch (Exception ex)
            {
                pending = null;
                Status = "Ошибка автопринятия: " + ex.GetBaseException().Message;
                Log.Error("core", "Accept native fast-travel vote", ex);
            }
        }
    }
}
