using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

public static class VerifyLobbyNative
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static int changed;
    public static void CompositionChanged() { changed++; }
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    private static FieldInfo Field(object target, string name)
    {
        for (Type t = target.GetType(); t != null; t = t.BaseType)
        { FieldInfo field = t.GetField(name, Flags | BindingFlags.DeclaredOnly); if (field != null) return field; }
        throw new MissingFieldException(target.GetType().Name, name);
    }
    private static void Set(object target, string name, object value) { Field(target, name).SetValue(target, value); }
    private static object Property(object target, string name) { return target.GetType().GetProperty(name, Flags).GetValue(target, null); }
    public static int Main(string[] args)
    {
        Assembly plugin = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Assembly game = Assembly.Load("Assembly-CSharp");
        Type type = game.GetType("GameLocationCharacterManager", true);
        object manager = FormatterServices.GetUninitializedObject(type);
        FieldInfo field = Field(manager, "partyCharacters");
        IList party = (IList)Activator.CreateInstance(field.FieldType);
        Set(manager, "partyCharacters", party);
        Set(manager, "readOnlyPartyCharacters", party.GetType().GetMethod("AsReadOnly").Invoke(party, null));
        object[] heroes = new object[4];
        for (int i = 0; i < heroes.Length; i++)
        {
            heroes[i] = FormatterServices.GetUninitializedObject(game.GetType("RulesetCharacterHero", true));
            Set(heroes[i], "guid", (ulong)(100 + i)); Set(heroes[i], "sortIndex", i);
            object character = FormatterServices.GetUninitializedObject(game.GetType("GameLocationCharacter", true));
            Set(character, "rulesetActor", heroes[i]); party.Add(character);
        }
        PropertyInfo notice = type.GetProperty("PartyCompositionChanged", Flags);
        notice.SetValue(manager, Delegate.CreateDelegate(notice.PropertyType, typeof(VerifyLobbyNative).GetMethod("CompositionChanged")), null);
        string[] initial = { "A", "B", "C", "D" }, desired = { "D", "B", "C", "A" };
        var plan = (System.Collections.Generic.List<int[]>)plugin.GetType("SolastaCoop.LobbyOrderPolicy", true)
            .GetMethod("Plan", Flags).Invoke(null, new object[] { initial, desired });
        MethodInfo swap = type.GetMethod("SwapPartyMembers", Flags);
        foreach (int[] pair in plan) swap.Invoke(manager, new object[] { pair[0], pair[1] });
        object[] actual = party.Cast<object>().Select(c => Property(c, "RulesetCharacter")).ToArray();
        Check(actual.SequenceEqual(new[] { heroes[3], heroes[1], heroes[2], heroes[0] }), "Real Solasta SwapPartyMembers produces the planned hero order");
        Check(actual.Select(h => (ulong)Property(h, "Guid")).SequenceEqual(new ulong[] { 103, 101, 102, 100 }),
            "Native swap preserves GUIDs and existing hero instances");
        Check(actual.Select(h => (int)Property(h, "SortIndex")).SequenceEqual(new[] { 0, 1, 2, 3 }) && changed == 1,
            "Native sort indexes and party-composition notification update together");
        swap.Invoke(manager, new object[] { -1, 3 }); swap.Invoke(manager, new object[] { 0, 4 });
        Check(changed == 1 && party.Count == 4, "Native invalid-slot guards leave the party intact");
        Console.WriteLine("Native lobby carryover checks: PASS (real Solasta 1.5.97 method IL; no live lobby or Unity rendering)");
        return 0;
    }
}
