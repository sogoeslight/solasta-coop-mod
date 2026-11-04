using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = System.Object;

namespace SolastaCoop
{
    internal static class LootConvenience
    {
        private sealed class WindowState { internal float Opened, EmptySince = -1f; }
        private static readonly ConditionalWeakTable<object, WindowState> Windows = new ConditionalWeakTable<object, WindowState>();
        private static bool installed;
        private static float nextPoll, nextHighlightPoll;
        private static Type swapperType;
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
                MethodInfo highlight = swapperType.GetMethod("RefreshVisibility", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                HarmonySupport.Patch(highlight,
                    typeof(LootConvenience).GetMethod("FilterHighlight", BindingFlags.NonPublic | BindingFlags.Static),
                    typeof(LootConvenience).GetMethod("RestoreHighlightRequest", BindingFlags.NonPublic | BindingFlags.Static));
                foreach (string name in new[] { "ContainerLootModal", "ProximityLootModal" })
                {
                    Type type = Read.Type(name);
                    MethodInfo show = type.GetMethod(name == "ContainerLootModal" ? "ShowContainerLoot" : "ShowProximityLoot",
                        BindingFlags.Instance | BindingFlags.Public);
                    HarmonySupport.Patch(show, postfix: typeof(LootConvenience).GetMethod("Opened", BindingFlags.NonPublic | BindingFlags.Static));
                }
                installed = true;
                Status = "Close empty loot and hide opened empty container highlights are ready.";
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

        private static void FilterHighlight(object __instance, out bool __state)
        {
            __state = Object.Equals(Read.Get(__instance, "wantedVisible"), true);
            if (!HideEmpty || !__state) return;
            object world = Read.Get(__instance, "worldGadget");
            object container = Read.Get(Read.Get(world, "GameGadget"), "ItemContainer");
            if (container != null && Object.Equals(Read.Get(container, "HasBeenOpenedOnce"), true) && Empty(container))
                ObserverHud.SetMember(__instance, "wantedVisible", false);
        }

        private static void RestoreHighlightRequest(object __instance, bool __state)
        {
            ObserverHud.SetMember(__instance, "wantedVisible", __state);
        }

        private static void Opened(object __instance)
        {
            Windows.Remove(__instance);
            Windows.Add(__instance, new WindowState { Opened = Time.unscaledTime });
        }

        internal static void Tick()
        {
            if (!installed) return;
            if (Time.unscaledTime >= nextHighlightPoll)
            {
                nextHighlightPoll = Time.unscaledTime + 0.5f;
                try
                {
                    foreach (UnityEngine.Object swapper in UnityEngine.Object.FindObjectsOfType(swapperType))
                        if (Object.Equals(Read.Get(swapper, "wantedVisible"), true)) Read.Call(swapper, "RefreshVisibility");
                }
                catch (Exception ex) { Log.Error("loot", "Refresh empty container highlights", ex); }
            }
            if (!AutoClose || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.1f;
            foreach (string name in new[] { "ContainerLootModal", "ProximityLootModal" })
            {
                try
                {
                    object modal = Read.Call(Read.Get(Read.Type("Gui"), "GuiService"), "GetScreen", Read.Type(name));
                    WindowState state;
                    if (modal == null || !Windows.TryGetValue(modal, out state) ||
                        !Object.Equals(Read.Get(modal, "Visible"), true) || Object.Equals(Read.Get(modal, "Hiding"), true)) continue;
                    if (Time.unscaledTime - state.Opened < 0.5f) continue;
                    bool empty;
                    if (name == "ContainerLootModal") empty = Empty(Read.Get(modal, "container"));
                    else
                    {
                        // This is the native source inventory, not the visible slot widget count.
                        object ground = Read.Get(modal, "groundSlots");
                        empty = ground is IEnumerable && !Read.Items(ground).Any(s => Read.Get(s, "EquipedItem") != null);
                    }
                    bool pending = Read.Items(Read.Get(modal, "itemsToLootCache")).Any();
                    if (!empty || pending) { state.EmptySince = -1f; continue; }
                    if (state.EmptySince < 0f) state.EmptySince = Time.unscaledTime;
                    if (Time.unscaledTime - state.EmptySince < 0.25f) continue;
                    Windows.Remove(modal);
                    Read.Call(modal, "OnCancelCb");
                    Status = "Closed empty " + name + " through the native cancel handler.";
                    Log.Event("loot", "INFO", Status);
                }
                catch (Exception ex)
                {
                    Status = "Empty loot check failed: " + ex.GetBaseException().Message;
                    Log.Error("loot", "Close empty loot", ex);
                }
            }
        }
    }
}
