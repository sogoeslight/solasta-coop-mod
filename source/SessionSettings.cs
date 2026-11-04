using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = System.Object;

namespace SolastaCoop
{
    // Room properties carry host policy, never character commands or inventory.
    internal static class SessionSettings
    {
        private const string PolicyKey = "SolastaCoopMod.policy/1";
        private const string ResultKey = "SolastaCoopMod.dialogue/2";
        private static object lastRoom;
        private static string published;
        private static float nextPoll;
        private static float localCombat = CombatSpeed.DefaultCombatFactor, localExploration = CombatSpeed.DefaultExplorationFactor;
        private static int localDialogue = 2;
        internal static string Status { get; private set; } = "Local settings.";

        internal static object Network { get { return Read.Service("INetworkingService"); } }
        internal static bool Multiplayer { get { return Object.Equals(Read.Get(Network, "IsMultiplayerGame"), true); } }
        internal static bool CanEdit { get { return !Multiplayer || Object.Equals(Read.Get(Network, "IsMasterClient"), true); } }
        private static object Room { get { return Read.Get(Read.Type("Photon.Pun.PhotonNetwork"), "CurrentRoom"); } }

        internal static void Load()
        {
            localCombat = PlayerPrefs.GetFloat("SolastaCoopMod.CombatSpeed", CombatSpeed.DefaultCombatFactor);
            localExploration = PlayerPrefs.GetFloat("SolastaCoopMod.ExplorationSpeed", CombatSpeed.DefaultExplorationFactor);
            localDialogue = Math.Max(0, Math.Min(2, PlayerPrefs.GetInt("SolastaCoopMod.DialogueMode", 2)));
            Apply(localCombat, localExploration, localDialogue);
        }

        internal static void Changed()
        {
            if (!CanEdit) return;
            localCombat = CombatSpeed.Factor;
            localExploration = CombatSpeed.ExplorationFactor;
            localDialogue = (int)DialogueChoices.Mode;
            PlayerPrefs.SetFloat("SolastaCoopMod.CombatSpeed", localCombat);
            PlayerPrefs.SetFloat("SolastaCoopMod.ExplorationSpeed", localExploration);
            PlayerPrefs.SetInt("SolastaCoopMod.DialogueMode", localDialogue);
            PlayerPrefs.Save();
            published = null;
            nextPoll = 0f;
        }

        private static void Apply(float combat, float exploration, int dialogue)
        {
            CombatSpeed.SetFactor(combat);
            CombatSpeed.SetExplorationFactor(exploration);
            DialogueChoices.SetMode((DialogueMode)dialogue);
        }

        internal static void Tick()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.25f;
            try
            {
                object room = Multiplayer ? Room : null;
                if (!Object.ReferenceEquals(room, lastRoom))
                {
                    lastRoom = room;
                    published = null;
                    Apply(localCombat, localExploration, localDialogue);
                }
                if (room == null) { Status = "Local settings."; return; }
                if (CanEdit)
                {
                    string signature = CombatSpeed.Factor + ":" + CombatSpeed.ExplorationFactor + ":" + (int)DialogueChoices.Mode;
                    if (signature != published && SetProperty(PolicyKey, new object[] {
                        1, CombatSpeed.Factor, CombatSpeed.ExplorationFactor, (int)DialogueChoices.Mode }))
                        published = signature;
                    Status = "Host settings are shared with the room.";
                }
                else
                {
                    object[] data = GetProperty(PolicyKey) as object[];
                    if (data == null || data.Length != 4 || !Object.Equals(data[0], 1) ||
                        !(data[1] is float) || !(data[2] is float) || !(data[3] is int) ||
                        !ValidFactor((float)data[1]) || !ValidFactor((float)data[2]) ||
                        (int)data[3] < 0 || (int)data[3] > 2)
                    {
                        // Never accelerate deterministic motion with an unknown host policy.
                        Apply(1f, 1f, 0);
                        Status = "Waiting for host settings. Movement uses vanilla speed.";
                        return;
                    }
                    Apply((float)data[1], (float)data[2], (int)data[3]);
                    Status = "Using host speed and dialogue settings.";
                }
            }
            catch (Exception ex)
            {
                if (!CanEdit) Apply(1f, 1f, 0);
                Status = "Settings sync failed: " + ex.GetBaseException().Message;
            }
        }

        private static bool ValidFactor(float value)
        {
            return !Single.IsNaN(value) && !Single.IsInfinity(value) && value >= 1f && value <= 3f;
        }

        private static object GetProperty(string key)
        {
            IDictionary properties = Read.Get(Room, "CustomProperties") as IDictionary;
            return properties != null && properties.Contains(key) ? properties[key] : null;
        }

        private static bool SetProperty(string key, object value)
        {
            object room = Room;
            if (room == null || !CanEdit) return false;
            MethodInfo method = room.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Single(m => m.Name == "SetCustomProperties" && m.GetParameters().Length == 3);
            IDictionary properties = Activator.CreateInstance(method.GetParameters()[0].ParameterType) as IDictionary;
            if (properties == null) throw new InvalidOperationException("Photon room property table is unavailable.");
            properties[key] = value;
            return Object.Equals(method.Invoke(room, new object[] { properties, null, null }), true);
        }

        internal static string ChoiceNonce
        {
            get
            {
                object[] data = GetProperty(ResultKey) as object[];
                return data != null && data.Length == 4 ? data[3] as string : null;
            }
        }

        internal static bool PublishChoice(string context, int answer, string nonce)
        {
            return SetProperty(ResultKey, new object[] { 2, context, answer, nonce });
        }

        internal static bool TryGetChoice(string context, string previousNonce, out int answer)
        {
            answer = -1;
            object[] data = GetProperty(ResultKey) as object[];
            if (data == null || data.Length != 4 || !Object.Equals(data[0], 2) ||
                !Object.Equals(data[1], context) || !(data[2] is int) || !(data[3] is string) ||
                Object.Equals(data[3], previousNonce)) return false;
            answer = (int)data[2];
            return true;
        }
    }
}
