using System;
using System.Collections.Generic;
using System.Reflection;

public static class ServiceRepository
{
    public static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>();
    public static object GetService(Type type) { object value; return Services.TryGetValue(type, out value) ? value : null; }
}
public sealed class ICursorService { public object CurrentCursor; }
public sealed class IGameLocationPositioningService { }
public sealed class IGameLocationBattleService
{
    public bool WithinReach;
    public bool Visible = true;
    public object LastVisibleCaster, LastVisibleTarget;
    public bool CanAttackerSeeCharacterFromPosition(object targetPosition, object sourcePosition, object target, object caster)
    { LastVisibleCaster = caster; LastVisibleTarget = target; return Visible; }
    public bool IsWithinXCells(object caster, object source, object target, object destination, int cells)
    { return WithinReach; }
}
public sealed class IGameLocationSelectionService { public readonly List<object> HoveredCharacters = new List<object>(); }
public class CursorLocation { public object ActionParams; public object SelectionService; }
public sealed class CursorLocationSelectTarget : CursorLocation { }
public sealed class CursorLocationSelectPosition : CursorLocation { }
public sealed class CursorLocationGeometricShape : CursorLocation { }
public sealed class RulesetEffectSpell { public object EffectDescription; public object SpellDefinition; public object MetamagicOption; }
public sealed class FakeAction { public object ActingCharacter; public object RulesetEffect; public object AttackMode; }
public sealed class FakeAttackMode { public bool Ranged; }
public sealed class FakeDescription { public string RangeType = "RangeHit"; }
public sealed class FakeCharacter { public object RulesetCharacter; public object LocationPosition; }
public sealed class FakeRuleset { public string DisplayName; public ulong Guid; }
public sealed class FakeSpell { public string Name = "TestSpell"; }
public static class BattleDefinitions
{
    public static object LastAttacker, LastDefender, LastSourcePosition, LastTargetPosition, LastMetamagic;
    public static float NativeDistance = 14.25f, NativeMaximum = 24.1f;
    public static object LastAttackMode;
    public static string LastMethod;
    public struct AttackEvaluationParams
    {
        public float distance, maxRange;
        public void FillForMagicDistance(object attacker, object attackPosition, object description, string name,
            object defender, object defensePosition, object modifier, object metamagic)
        {
            LastAttacker = attacker; LastDefender = defender;
            LastSourcePosition = attackPosition; LastTargetPosition = defensePosition; LastMetamagic = metamagic;
            distance = NativeDistance; maxRange = NativeMaximum;
            LastMethod = "Magic";
        }
        public void FillForPhysicalRangeAttack(object caster, object source, object mode, object target, object destination, object modifier)
        {
            LastAttackMode = mode; LastMethod = "Ranged";
            LastAttacker = caster; LastDefender = target;
            distance = NativeDistance; maxRange = NativeMaximum;
        }
        public void FillForPhysicalReachAttack(object caster, object source, object mode, object target, object destination, object modifier)
        {
            LastAttackMode = mode; LastMethod = "Melee";
            distance = NativeDistance; maxRange = NativeMaximum;
        }
    }
}
public static class VerifyHover
{
    private static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static void Check(bool value, string description)
    {
        if (!value) throw new Exception(description);
        Console.WriteLine("PASS " + description);
    }
    public static int Main(string[] args)
    {
        Assembly plugin = Assembly.LoadFrom(args[0]);
        MethodInfo capture = plugin.GetType("SolastaCoop.HoverRange", true).GetMethod("Capture", Flags);
        Func<object> read = () => capture.Invoke(null, null);
        Func<string> status = () => (string)plugin.GetType("SolastaCoop.HoverRange", true)
            .GetProperty("LastStatus", Flags).GetValue(null, null);
        Func<object, string, object> field = (snapshot, name) => snapshot.GetType().GetField(name, Flags).GetValue(snapshot);
        Func<object, bool> inRange = snapshot => (bool)snapshot.GetType().GetProperty("WithinRange", Flags).GetValue(snapshot, null);
        var selection = new IGameLocationSelectionService();
        var caster = new FakeCharacter { RulesetCharacter = new FakeRuleset { DisplayName = "Caster", Guid = 1 },
            LocationPosition = new Tuple<int, int, int>(1, 2, 3) };
        var target = new FakeCharacter { RulesetCharacter = new FakeRuleset { DisplayName = "Target", Guid = 2 },
            LocationPosition = new Tuple<int, int, int>(10, 12, 20) };
        var description = new FakeDescription();
        var metamagic = new object();
        var effect = new RulesetEffectSpell { EffectDescription = description, SpellDefinition = new FakeSpell(), MetamagicOption = metamagic };
        var action = new FakeAction { ActingCharacter = caster, RulesetEffect = effect };
        var cursor = new CursorLocationSelectTarget { ActionParams = action, SelectionService = selection };
        var cursorService = new ICursorService { CurrentCursor = cursor };
        ServiceRepository.Services[typeof(ICursorService)] = cursorService;
        ServiceRepository.Services[typeof(IGameLocationPositioningService)] = new IGameLocationPositioningService();
        var sight = new IGameLocationBattleService { WithinReach = true };
        ServiceRepository.Services[typeof(IGameLocationBattleService)] = sight;
        selection.HoveredCharacters.Add(target);
        object snapshot = read();
        Check(snapshot != null && (string)field(snapshot, "Source") == "Caster" && (string)field(snapshot, "Target") == "Target",
            "Snapshot identifies the targeting caster and hovered unit");
        Check(ReferenceEquals(BattleDefinitions.LastAttacker, caster) && ReferenceEquals(BattleDefinitions.LastDefender, target),
            "Native evaluator receives current action caster and hovered target");
        Check(ReferenceEquals(BattleDefinitions.LastSourcePosition, caster.LocationPosition) &&
            ReferenceEquals(BattleDefinitions.LastTargetPosition, target.LocationPosition),
            "Native evaluator receives full positions including height");
        Check(ReferenceEquals(BattleDefinitions.LastMetamagic, metamagic), "Selected metamagic reaches native evaluator");
        Check(Math.Abs((double)field(snapshot, "Distance") - 14.25) < 0.001 && inRange(snapshot),
            "Adapter reads native distance from the mutated boxed struct");
        Check(((string)field(snapshot, "DiagnosticDetails")).Contains("evaluator=FillForMagicDistance") &&
            ((string)field(snapshot, "DiagnosticDetails")).Contains("colourRule=withinRange&&visible"),
            "Range snapshot explains the evaluator and colour rule");
        Func<object, bool> green = value => (bool)value.GetType().GetProperty("HighlightGreen", Flags).GetValue(value, null);
        Check(green(snapshot) && ReferenceEquals(sight.LastVisibleCaster, caster) && ReferenceEquals(sight.LastVisibleTarget, target),
            "Visibility is evaluated from the actual caster to the hovered target");
        sight.Visible = false;
        snapshot = read();
        Check(inRange(snapshot) && !green(snapshot), "An in-range target without line of sight is red");
        sight.Visible = true;
        Check(green(read()), "Visible target with sufficient range is green");
        BattleDefinitions.NativeDistance = 30;
        Check(!inRange(read()), "Out-of-range native result is displayed as out of range");
        Check(!green(read()), "Visible target beyond range remains red");
        selection.HoveredCharacters.Clear();
        Check(read() == null, "No stale snapshot when no unit is hovered");
        Check(status() == "no-hovered-unit", "Diagnostics identify an absent hovered target");
        selection.HoveredCharacters.Add(caster);
        Check(read() == null, "No distance tooltip for the caster itself");
        selection.HoveredCharacters.Clear(); selection.HoveredCharacters.Add(target);
        description.RangeType = "Touch";
        Check(read() == null, "Touch spells do not show a ranged tooltip");
        Check(status() == "unsupported-spell-range:Touch", "Unsupported spell range has an explicit diagnostic reason");
        description.RangeType = "Distance";
        cursorService.CurrentCursor = new CursorLocationSelectPosition { ActionParams = action, SelectionService = selection };
        Check(read() != null, "Position-targeted ranged spells are supported when a unit is hovered");
        cursorService.CurrentCursor = new CursorLocationGeometricShape { ActionParams = action, SelectionService = selection };
        Check(read() != null, "Area-targeting cursors are supported when a unit is hovered");
        cursorService.CurrentCursor = new CursorLocation { ActionParams = action, SelectionService = selection };
        Check(read() == null, "Ordinary cursors cannot display stale targeting data");
        cursorService.CurrentCursor = cursor;
        action.RulesetEffect = new object();
        Check(read() == null, "Actions without spell or attack mode show no tooltip");
        var attack = new FakeAttackMode { Ranged = true };
        action.AttackMode = attack;
        BattleDefinitions.NativeMaximum = 12;
        BattleDefinitions.NativeDistance = 15;
        snapshot = read();
        Check(snapshot != null && !inRange(snapshot) && BattleDefinitions.LastMethod == "Ranged" &&
            ReferenceEquals(BattleDefinitions.LastAttackMode, attack), "Ranged attack uses selected attack mode and remains visible beyond range");
        BattleDefinitions.NativeDistance = 8;
        Check(inRange(read()), "Ranged attack within maximum range is green");
        attack.Ranged = false;
        var battle = new IGameLocationBattleService { WithinReach = true };
        ServiceRepository.Services[typeof(IGameLocationBattleService)] = battle;
        BattleDefinitions.NativeDistance = 1.414f; BattleDefinitions.NativeMaximum = 1;
        Check(inRange(read()) && BattleDefinitions.LastMethod == "Melee", "Melee colour follows native occupied-cell reach, including diagonals");
        battle.WithinReach = false;
        Check(!inRange(read()), "Melee outside native occupied-cell reach is red");
        ServiceRepository.Services.Remove(typeof(IGameLocationBattleService));
        Check(read() == null, "Missing melee reach service does not produce a false colour");
        Console.WriteLine("Hover adapter tests: PASS (fake game services, not a running Unity session)");
        return 0;
    }
}
