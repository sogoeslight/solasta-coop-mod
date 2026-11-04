using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = System.Object;

namespace SolastaCoop
{
    internal static class LootConvenience
    {
        private sealed class WindowState
        {
            internal object Source;
            internal float Opened, EmptySince = -1f;
            internal bool Closing;
        }
        private static ConditionalWeakTable<object, WindowState> windows = new ConditionalWeakTable<object, WindowState>();
        private static readonly HashSet<object> suppressedHighlights = new HashSet<object>();
        private static UnityEngine.Object[] highlightSwappers = new UnityEngine.Object[0];
        private static bool installed;
        private static float nextPoll, nextHighlightPoll;
        private static Type swapperType;
        private static object location;
        internal static bool AutoClose { get; private set; } = true;
        internal static bool HideEmpty { get; private set; } = true;
        internal static string Status { get; private set; } = "Loot convenience is not initialized.";

        internal static void Install()
        {
            if (installed) return;
            AutoClose = PlayerPrefs.GetInt("SolastaCoopMod.CloseEmptyLoot", 1) != 0;
            HideEmpty = PlayerPrefs.GetInt("SolastaCoopMod.HideEmptyLoot", 1) != 0;
            try
            {
                swapperType = Read.Type("WorldGadgetHighlightSwapper");
                if (swapperType == null || Read.Type("ContainerLootModal") == null || Read.Type("ProximityLootModal") == null)
                    throw new MissingMemberException("Native loot window or highlight type is unavailable.");
                installed = true;
                Status = "Empty loot checks are ready; native loot and highlight methods are not detoured.";
                Log.Event("loot", "INFO", Status);
            }
            catch (Exception ex)
            {
                Status = "Loot convenience unavailable: " + ex.GetBaseException().Message;
                Log.Error("loot", "Install loot convenience", ex);
            }
        }

        internal static void SetAutoClose(bool enabled)
        {
            AutoClose = enabled;
            PlayerPrefs.SetInt("SolastaCoopMod.CloseEmptyLoot", enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        internal static void SetHideEmpty(bool enabled)
        {
            HideEmpty = enabled;
            PlayerPrefs.SetInt("SolastaCoopMod.HideEmptyLoot", enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static bool Empty(object container)
        {
            object slots = Read.Get(container, "InventorySlots");
            return slots is IEnumerable && !Read.Items(slots).Any(s => Read.Get(s, "EquipedItem") != null);
        }

        private static void RefreshHighlight(object swapper)
        {
            bool wanted = Object.Equals(Read.Get(swapper, "wantedVisible"), true);
            if (!HideEmpty || !wanted)
            {
                if (suppressedHighlights.Contains(swapper))
                {
                    Read.Call(swapper, "RefreshVisibility");
                    suppressedHighlights.Remove(swapper);
                }
                return;
            }
            object world = Read.Get(swapper, "worldGadget");
            object container = Read.Get(Read.Get(world, "GameGadget"), "ItemContainer");
            bool hide = container != null &&
                Object.Equals(Read.Get(container, "HasBeenOpenedOnce"), true) && Empty(container);
            if (hide)
            {
                // Apply the native renderer update after gameplay updates, preserving its request for later restoration.
                ObserverHud.SetMember(swapper, "wantedVisible", false);
                try
                {
                    Read.Call(swapper, "RefreshVisibility");
                    suppressedHighlights.Add(swapper);
                }
                finally { ObserverHud.SetMember(swapper, "wantedVisible", wanted); }
            }
            else if (suppressedHighlights.Contains(swapper))
            {
                Read.Call(swapper, "RefreshVisibility");
                suppressedHighlights.Remove(swapper);
            }
        }

        internal static void Tick()
        {
            if (!installed) return;
            object current = Read.Get(Read.Type("Gui"), "GameLocation");
            if (!Object.ReferenceEquals(location, current))
            {
                location = current;
                windows = new ConditionalWeakTable<object, WindowState>();
                highlightSwappers = new UnityEngine.Object[0];
                suppressedHighlights.Clear();
                nextPoll = nextHighlightPoll = 0f;
            }
            if (current == null || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.1f;
            foreach (string name in new[] { "ContainerLootModal", "ProximityLootModal" })
            {
                try
                {
                    object modal = Read.Call(Read.Get(Read.Type("Gui"), "GuiService"), "GetScreen", Read.Type(name));
                    CheckWindow(modal, name == "ProximityLootModal", Time.unscaledTime);
                }
                catch (Exception ex)
                {
                    Status = "Empty loot check failed: " + ex.GetBaseException().Message;
                    Log.Error("loot", "Close empty loot", ex);
                }
            }
        }

        private static void CheckWindow(object modal, bool groundLoot, float now)
        {
            if (modal == null) return;
            if (!AutoClose || !Object.Equals(Read.Get(modal, "Visible"), true) ||
                Object.Equals(Read.Get(modal, "Hiding"), true))
            {
                windows.Remove(modal);
                return;
            }
            object source = Read.Get(modal, groundLoot ? "groundSlots" : "container");
            WindowState state;
            if (!windows.TryGetValue(modal, out state) || !Object.ReferenceEquals(state.Source, source))
            {
                windows.Remove(modal);
                state = new WindowState { Source = source, Opened = now };
                windows.Add(modal, state);
            }
            if (state.Closing || now - state.Opened < 0.5f) return;
            bool empty = groundLoot ? source is IEnumerable &&
                !Read.Items(source).Any(s => Read.Get(s, "EquipedItem") != null) : Empty(source);
            bool pending = Read.Items(Read.Get(modal, "itemsToLootCache")).Any();
            if (!empty || pending) { state.EmptySince = -1f; return; }
            if (state.EmptySince < 0f) state.EmptySince = now;
            if (now - state.EmptySince < 0.25f) return;
            Read.Call(modal, "OnCancelCb");
            state.Closing = true;
            Status = "Closed empty " + modal.GetType().Name + " through the native cancel handler.";
            Log.Event("loot", "INFO", Status);
        }

        internal static void LateTick()
        {
            if (!installed || location == null) return;
            try
            {
                if (Time.unscaledTime >= nextHighlightPoll)
                {
                    nextHighlightPoll = Time.unscaledTime + 0.5f;
                    highlightSwappers = UnityEngine.Object.FindObjectsOfType(swapperType);
                    suppressedHighlights.RemoveWhere(s => s is UnityEngine.Object && (UnityEngine.Object)s == null);
                }
                foreach (UnityEngine.Object swapper in highlightSwappers)
                    if (swapper != null) RefreshHighlight(swapper);
            }
            catch (Exception ex) { Log.Error("loot", "Refresh empty container highlights", ex); }
        }
    }
}
