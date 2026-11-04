using System;

namespace SolastaCoop
{
    internal static class GameplayFeatures
    {
        internal static bool Initialized { get; private set; }

        internal static void InitializeIfReady()
        {
            if (Initialized || Read.Get(Read.Type("Gui"), "GameLocation") == null) return;
            Initialized = true;
            Log.Write("Gameplay feature initialization started after location load.");
            Initialize("session preferences", SessionSettings.Load);
            Initialize("movement speed", MovementSpeed.Install);
            Initialize("dialogue voting", DialogueChoices.Install);
            Initialize("dialogue autosave", DialogueAutosave.Install);
            Initialize("loot convenience", LootConvenience.Install);
            Log.Write("Gameplay feature initialization finished.");
        }

        private static void Initialize(string name, Action action)
        {
            Log.Write("Initializing " + name + ".");
            try
            {
                action();
                Log.Write("Initialization returned: " + name + ".");
            }
            catch (Exception ex) { Log.Error("core", "Initialize " + name, ex); }
        }
    }
}
