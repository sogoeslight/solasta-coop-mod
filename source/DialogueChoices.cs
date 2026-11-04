using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SolastaCoop
{
    internal static class DialogueChoices
    {
        private sealed class Context
        {
            internal string Key;
            internal DialogueMode Mode;
            internal int Winner = -1;
            internal string PreviousNonce, Nonce = Guid.NewGuid().ToString("N");
        }

        private static readonly ConditionalWeakTable<object, Context> Contexts = new ConditionalWeakTable<object, Context>();
        private static readonly Random Random = new Random();
        private static bool installed;
        internal static DialogueMode Mode { get; private set; } = DialogueMode.FollowHost;
        internal static string Status { get; private set; } = "Dialogue voting is not initialized.";

        internal static void SetMode(DialogueMode mode) { if ((int)mode >= 0 && (int)mode <= 2) Mode = mode; }

        internal static void Install()
        {
            if (installed) return;
            try
            {
                Type type = Read.Type("NarrativeStateAnswerChoice");
                Patch(type, "Begin", "AfterBegin", false);
                Patch(type, "GetVoteWinner", "GetWinner", true);
                Patch(type, "CompleteVotingSession", "Complete", true);
                installed = true;
                Status = "Host controls the dialogue policy; random ties use equal player votes.";
            }
            catch (Exception ex)
            {
                Status = "Dialogue voting unavailable: " + ex.GetBaseException().Message;
                Log.Error("core", "Install dialogue voting", ex);
            }
        }

        private static void Patch(Type type, string name, string callback, bool prefix)
        {
            MethodInfo target = type == null ? null : type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SingleOrDefault(m => m.Name == name && m.DeclaringType == type);
            MethodInfo hook = typeof(DialogueChoices).GetMethod(callback, BindingFlags.NonPublic | BindingFlags.Static);
            HarmonySupport.Patch(target, prefix ? hook : null, prefix ? null : hook);
        }

        private static void AfterBegin(object __instance)
        {
            object sequence = Read.Get(__instance, "NarrativeSequence");
            object description = Read.Get(__instance, "NarrativeStateDescription");
            object tree = Read.Get(sequence, "NarrativeTreeDefinition");
            object tick = Read.Get(Read.Get(SessionSettings.Network, "Tick"), "LocalTick");
            object stateIndex = Read.Get(description, "Index");
            Contexts.Remove(__instance);
            Contexts.Add(__instance, new Context {
                Key = Read.Text(Read.Get(tree, "Name")) + ":" + Read.Text(stateIndex) + ":" + Read.Text(tick),
                Mode = tick == null || stateIndex == null ? DialogueMode.Vanilla : Mode,
                PreviousNonce = SessionSettings.ChoiceNonce
            });
        }

        private static int[] Candidates(object state, Context context, out bool everyone)
        {
            var votes = Read.Get(state, "playerVotesPerChoice") as IDictionary<int, List<int>>;
            var players = new HashSet<int>(Read.Items(Read.Call(SessionSettings.Network, "GetPlayersInRoom"))
                .Select(p => Convert.ToInt32(Read.Get(p, "actorNumber"))));
            int host = Convert.ToInt32(Read.Get(SessionSettings.Network, "MasterClientNumber"));
            if (votes == null || players.Count == 0 || players.Contains(0))
                throw new InvalidOperationException("Native dialogue vote data is unavailable.");
            return VotePolicy.Candidates(votes, players, host, context.Mode, out everyone);
        }

        private static bool GetWinner(object __instance, ref int __0, ref bool __1)
        {
            Context context;
            if (!installed || !SessionSettings.Multiplayer || !Contexts.TryGetValue(__instance, out context) ||
                context.Mode == DialogueMode.Vanilla) return true;
            try
            {
                int[] candidates = Candidates(__instance, context, out __1);
                __0 = candidates.Length == 0 ? 0 : candidates[0];
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("core", "Read dialogue votes", ex);
                return true;
            }
        }

        private static bool Complete(object __instance, ref int __0)
        {
            Context context;
            if (!installed || !SessionSettings.Multiplayer || !Contexts.TryGetValue(__instance, out context) ||
                context.Mode == DialogueMode.Vanilla) return true;
            try
            {
                bool everyone;
                int[] candidates = Candidates(__instance, context, out everyone);
                if (candidates.Length == 0) return false; // FollowHost waits for the host, including after the vanilla timer.
                if (SessionSettings.CanEdit)
                {
                    if (context.Winner < 0)
                        context.Winner = candidates[Random.Next(candidates.Length)];
                    if (!SessionSettings.PublishChoice(context.Key, context.Winner, context.Nonce)) return false;
                    __0 = context.Winner;
                    Status = "Host resolved dialogue choice " + __0 + " (" + context.Mode + ").";
                    return true;
                }
                int winner;
                // A single host-authored outcome, never independent random draws on guests.
                if (!SessionSettings.TryGetChoice(context.Key, context.PreviousNonce, out winner) || !candidates.Contains(winner)) return false;
                __0 = winner;
                return true;
            }
            catch (Exception ex)
            {
                Status = "Dialogue resolution error: " + ex.GetBaseException().Message;
                Log.Error("core", "Resolve dialogue vote", ex);
                return true;
            }
        }
    }
}
