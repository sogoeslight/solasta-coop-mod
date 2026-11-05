using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

public sealed class LootTestSlot { public object EquipedItem; }
public sealed class LootTestContainer { public List<LootTestSlot> InventorySlots = new List<LootTestSlot>(); }
public sealed class LootTestWindow
{
    public bool Visible = true, Hiding;
    public object container;
    public List<LootTestSlot> groundSlots = new List<LootTestSlot>();
    public List<object> itemsToLootCache = new List<object>();
    public int Closes;
    public void OnCancelCb() { Closes++; Hiding = true; }
}

public static class VerifyLoot
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static Assembly game;
    private static Type loot;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }
    private static FieldInfo Field(object target, string name)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo field = type.GetField(name, Flags | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }
    private static void Set(object target, string name, object value) { Field(target, name).SetValue(target, value); }
    private static object Get(object target, string name) { return Field(target, name).GetValue(target); }
    private static object Native(string name) { return FormatterServices.GetUninitializedObject(game.GetType(name, true)); }
    private static IList EmptyList(object target, string name)
    {
        var list = (IList)Activator.CreateInstance(Field(target, name).FieldType);
        Set(target, name, list);
        return list;
    }
    private static void Call(string method, params object[] args) { loot.GetMethod(method, Flags).Invoke(null, args); }
    private static void Poll(LootTestWindow window, bool ground, float time) { Call("CheckWindow", window, ground, time); }
    private static int PatchCount(MethodInfo method)
    {
        Type harmony = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("HarmonyLib.Harmony", false)).FirstOrDefault(t => t != null);
        object info = harmony == null ? null : harmony.GetMethod("GetPatchInfo", Flags).Invoke(null, new object[] { method });
        if (info == null) return 0;
        int count = 0;
        foreach (string kind in new[] { "Prefixes", "Postfixes", "Transpilers", "Finalizers" })
        {
            FieldInfo field = info.GetType().GetField(kind, Flags);
            object patches = field != null ? field.GetValue(info) : info.GetType().GetProperty(kind, Flags).GetValue(info, null);
            count += ((IEnumerable)patches).Cast<object>().Count();
        }
        return count;
    }
    public static int Main(string[] args)
    {
        game = Assembly.Load("Assembly-CSharp");
        Assembly plugin = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        loot = plugin.GetType("SolastaCoop.LootConvenience", true);
        // Reproduce the real load order: legacy shared-loot generic postfixes exist before the new features initialize.
        MethodInfo shareOpen = plugin.GetType("SolastaCoop.LootShare", true).GetMethod("PatchOpened", Flags);
        shareOpen.Invoke(null, new object[] { "ContainerLootModal", "ShowContainerLoot", 2, "ContainerOpened" });
        shareOpen.Invoke(null, new object[] { "ProximityLootModal", "ShowProximityLoot", 1, "ProximityOpened" });
        Check(PatchCount(game.GetType("ContainerLootModal").GetMethod("ShowContainerLoot", Flags)) == 1 &&
            PatchCount(game.GetType("ProximityLootModal").GetMethod("ShowProximityLoot", Flags)) == 1,
            "Existing shared-loot generic callbacks are installed on real game windows first");
        Call("Install");
        Check((bool)loot.GetField("installed", Flags).GetValue(null), "Loot initialization returns with real Solasta game types");
        foreach (string name in new[] { "WorldGadgetHighlightSwapper", "ContainerLootModal", "ProximityLootModal" })
        {
            string method = name == "WorldGadgetHighlightSwapper" ? "RefreshVisibility" :
                name == "ContainerLootModal" ? "ShowContainerLoot" : "ShowProximityLoot";
            int expected = name == "WorldGadgetHighlightSwapper" ? 0 : 1;
            Check(PatchCount(game.GetType(name, true).GetMethod(method, Flags)) == expected,
                "No added native detour; shared loot retained: " + name + "." + method);
        }

        object container = Native("RulesetContainer"), slot = Native("RulesetInventorySlot");
        IList slots = EmptyList(container, "inventorySlots"); slots.Add(slot);
        object gadget = Native("GameGadget"), world = Native("WorldGadget"), swapper = Native("WorldGadgetHighlightSwapper");
        Set(gadget, "itemContainer", container); Set(gadget, "revealed", true);
        Set(world, "gameGadget", gadget); Set(world, "m_CachedPtr", new IntPtr(1));
        Set(swapper, "worldGadget", world); Set(swapper, "wantedVisible", true); Set(swapper, "visible", true);
        EmptyList(swapper, "objectsActiveWhenVisible"); EmptyList(swapper, "objectsActiveWhenHidden"); EmptyList(swapper, "materialSwappers");
        Call("RefreshHighlight", swapper);
        Check((bool)Get(swapper, "visible"), "Unopened containers retain native highlighting");
        Set(container, "hasBeenOpenedOnce", true);
        Call("RefreshHighlight", swapper);
        Check(!(bool)Get(swapper, "visible") && (bool)Get(swapper, "wantedVisible"),
            "Real native RefreshVisibility hides opened empty loot and preserves the request");
        Check((bool)loot.GetMethod("CanHide", Flags).Invoke(null, new[] { swapper }), "Cached highlight eligibility uses the actual opened empty inventory");
        Call("ApplyHighlight", swapper, true);
        Check(!(bool)Get(swapper, "visible") && (bool)Get(swapper, "wantedVisible"),
            "Per-frame cached suppression preserves native state without another inventory walk");
        swapper.GetType().GetMethod("SetVisible").Invoke(swapper, new object[] { true });
        Check((bool)Get(swapper, "visible"), "Original game visibility method remains callable without interception");
        Call("RefreshHighlight", swapper);
        Check(!(bool)Get(swapper, "visible"), "Late refresh suppresses a renewed native highlight request");
        object item = Native("RulesetItem"); Set(slot, "equipedItem", item);
        Call("RefreshHighlight", swapper);
        Check((bool)Get(swapper, "visible") && ReferenceEquals(Get(slot, "equipedItem"), item) && slots.Count == 1,
            "Refilled container restores its highlight without changing inventory");
        Set(slot, "equipedItem", null); Call("RefreshHighlight", swapper);
        Call("SetHideEmpty", false); Call("RefreshHighlight", swapper);
        Check((bool)Get(swapper, "visible") && (bool)Get(swapper, "wantedVisible"), "Disabling the option restores native highlighting");
        Call("SetHideEmpty", true); Call("RefreshHighlight", swapper);
        Set(swapper, "wantedVisible", false); Call("RefreshHighlight", swapper);
        Check(!(bool)Get(swapper, "visible") && !(bool)Get(swapper, "wantedVisible"), "Native hide request is never reversed");

        var source = new LootTestContainer(); var testSlot = new LootTestSlot { EquipedItem = new object() };
        source.InventorySlots.Add(testSlot);
        var window = new LootTestWindow { container = source };
        Poll(window, false, 0f); Poll(window, false, 1f);
        Check(window.Closes == 0, "Nonempty container stays open");
        testSlot.EquipedItem = null; window.itemsToLootCache.Add(new object());
        Poll(window, false, 2f); Poll(window, false, 3f);
        Check(window.Closes == 0, "Pending native transfers prevent closing");
        window.itemsToLootCache.Clear(); Poll(window, false, 4f); Poll(window, false, 4.1f);
        Check(window.Closes == 0, "Empty inventory must remain stable before closing");
        Poll(window, false, 4.3f); Poll(window, false, 5f);
        Check(window.Closes == 1 && window.Hiding, "Empty container closes once using native cancel");
        window.Visible = false; Poll(window, false, 6f);
        window.Visible = true; window.Hiding = false; Poll(window, false, 7f); Poll(window, false, 7.2f);
        Check(window.Closes == 1, "Reopened empty window receives a new opening grace period");
        Poll(window, false, 7.6f); Poll(window, false, 7.9f);
        Check(window.Closes == 2, "Reopened empty window closes after the grace period");

        var ground = new LootTestWindow(); var groundSlot = new LootTestSlot { EquipedItem = new object() };
        ground.groundSlots.Add(groundSlot); Poll(ground, true, 0f); Poll(ground, true, 1f);
        Check(ground.Closes == 0, "Ground loot checks source items rather than visible UI slots");
        groundSlot.EquipedItem = null; Poll(ground, true, 2f); Poll(ground, true, 2.3f);
        Check(ground.Closes == 1, "Empty ground loot uses native cancel too");

        var switched = new LootTestWindow { container = new LootTestContainer() };
        Poll(switched, false, 0f); Poll(switched, false, 0.6f);
        switched.container = new LootTestContainer(); Poll(switched, false, 0.9f); Poll(switched, false, 1.1f);
        Check(switched.Closes == 0, "Changing source container resets close timing");
        Call("SetAutoClose", false); Poll(switched, false, 2f); Poll(switched, false, 3f);
        Check(switched.Closes == 0, "Disabled autoclose leaves loot open");
        Call("SetAutoClose", true); Poll(switched, false, 4f); Poll(switched, false, 4.2f);
        Check(switched.Closes == 0, "Re-enabling autoclose grants a new grace period");
        Console.WriteLine("Loot checks: PASS (real game highlight IL; isolated inventories; no Unity rendering or live save load)");
        return 0;
    }
}
