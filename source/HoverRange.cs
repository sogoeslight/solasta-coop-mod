using System;
using System.Linq;
using System.Reflection;

namespace SolastaCoop
{
    internal sealed class RangeSnapshot
    {
        internal readonly string Source, Target, Spell;
        internal readonly string DiagnosticDetails;
        internal readonly double Distance, Maximum;
        internal readonly bool? Visible;
        private readonly bool? nativeWithinRange;
        internal bool WithinRange { get { return nativeWithinRange ?? Distance <= Maximum + 0.01; } }
        internal bool HighlightGreen { get { return WithinRange && Visible == true; } }

        internal RangeSnapshot(string source, string target, string spell, double distance, double maximum,
            bool? withinRange = null, string diagnosticDetails = "", bool? visible = null)
        {
            if (Double.IsNaN(distance) || Double.IsInfinity(distance) || distance < 0 ||
                Double.IsNaN(maximum) || Double.IsInfinity(maximum) || maximum < 0)
                throw new ArgumentOutOfRangeException("distance", "Некорректная дальность.");
            Source = source;
            Target = target;
            Spell = spell;
            Distance = distance;
            Maximum = maximum;
            nativeWithinRange = withinRange;
            DiagnosticDetails = diagnosticDetails;
            Visible = visible;
        }
    }

    internal static class HoverRange
    {
        internal static string LastStatus { get; private set; }
        private static RangeSnapshot Hidden(string reason) { LastStatus = reason; return null; }
        internal static RangeSnapshot Capture()
        {
            LastStatus = "capture-started";
            object cursor = Read.Get(Read.Service("ICursorService"), "CurrentCursor");
            if (cursor == null) return Hidden("no-current-cursor");
            if (!IsTargeting(cursor)) return Hidden("not-targeting:" + cursor.GetType().Name);
            object action = Read.Get(cursor, "ActionParams");
            object caster = Read.Get(action, "ActingCharacter");
            object effect = Read.Get(action, "RulesetEffect");
            object attackMode = Read.Get(action, "AttackMode");
            Type spellType = Read.Type("RulesetEffectSpell");
            bool isSpell = effect != null && spellType != null && spellType.IsInstanceOfType(effect);
            if (caster == null) return Hidden("no-acting-character");
            if (!isSpell && attackMode == null) return Hidden("no-supported-spell-or-attack");

            object description = Read.Get(cursor, "effectDescription") ?? Read.Get(effect, "EffectDescription");
            string rangeType = Read.Text(Read.Get(description, "RangeType"));
            if (isSpell && rangeType != "RangeHit" && rangeType != "Distance") return Hidden("unsupported-spell-range:" + rangeType);
            object selection = Read.Get(cursor, "SelectionService") ?? Read.Service("IGameLocationSelectionService");
            object target = Read.Items(Read.Get(selection, "HoveredCharacters")).FirstOrDefault();
            if (target == null) return Hidden("no-hovered-unit");
            if (Read.Same(caster, target)) return Hidden("hovered-self");
            object position = Read.Get(caster, "LocationPosition");
            object targetPosition = Read.Get(target, "LocationPosition");
            if (position == null || targetPosition == null) return Hidden("missing-position");
            if (Read.Service("IGameLocationPositioningService") == null) return Hidden("missing-positioning-service");

            Type evaluationType = Read.Type("BattleDefinitions+AttackEvaluationParams");
            if (evaluationType == null) return Hidden("missing-evaluation-type");
            bool isRanged = Read.Get(attackMode, "Ranged") is bool && (bool)Read.Get(attackMode, "Ranged");
            string method = isSpell ? "FillForMagicDistance" :
                isRanged ? "FillForPhysicalRangeAttack" : "FillForPhysicalReachAttack";
            MethodInfo fill = evaluationType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == method && m.GetParameters().Length == (isSpell ? 8 : 6));
            if (fill == null) throw new MissingMethodException("AttackEvaluationParams." + method);
            object definition = Read.Get(effect, "SpellDefinition");
            object metamagic = Read.Get(cursor, "metamagicOption") ?? Read.Get(effect, "MetamagicOption");
            object evaluation = Activator.CreateInstance(evaluationType);
            fill.Invoke(evaluation, isSpell ? new[] { caster, position, description,
                Read.Get(definition, "Name") as string ?? "", target, targetPosition, null, metamagic } :
                new[] { caster, position, attackMode, target, targetPosition, null });
            object distance = Read.Get(evaluation, "distance");
            object maximum = Read.Get(evaluation, "maxRange");
            if (distance == null || maximum == null) return Hidden("missing-evaluation-fields");
            bool? within = null;
            if (!isSpell && !isRanged)
            {
                // Melee reach uses occupied cells, not centre-to-centre Euclidean distance.
                object result = Read.Call(Read.Service("IGameLocationBattleService"), "IsWithinXCells",
                    caster, position, target, targetPosition, Convert.ToInt32(maximum));
                if (!(result is bool)) return Hidden("missing-melee-reach-check");
                within = (bool)result;
            }
            LastStatus = "ready:" + method;
            bool? visible = null;
            string visibilityReason;
            try
            {
                object sight = Read.Call(Read.Service("IGameLocationBattleService"), "CanAttackerSeeCharacterFromPosition",
                    targetPosition, position, target, caster);
                if (sight is bool) visible = (bool)sight;
                visibilityReason = sight is bool ? "native-CanAttackerSeeCharacterFromPosition" : "visibility-check-unavailable";
            }
            catch (Exception e) { visibilityReason = "visibility-error:" + e.GetBaseException().GetType().Name; }
            string diagnosticDetails = "evaluator=" + method + "; rangeType=" + (isSpell ? rangeType : "weapon") +
                "; effect=" + (isSpell ? Read.Text(Read.Get(definition, "Name")) : "weapon") +
                "; sourcePosition=" + Read.Text(position) + "; targetPosition=" + Read.Text(targetPosition) +
                "; metamagic=" + Read.Text(Read.Get(metamagic, "Name")) +
                "; visibilityCheck=" + visibilityReason + "; colourRule=withinRange&&visible; rangeRule=" +
                (within.HasValue ? "native-occupied-cell-reach" : "distance<=maximumRaw+0.01");
            return new RangeSnapshot(Read.Name(caster), Read.Name(target), Read.Title(definition),
                Convert.ToDouble(distance), Convert.ToDouble(maximum), within, diagnosticDetails, visible);
        }

        private static bool IsTargeting(object cursor)
        {
            foreach (string name in new[] { "CursorLocationSelectTarget", "CursorLocationSelectPosition",
                "CursorLocationGeometricShape" })
            {
                Type type = Read.Type(name);
                if (type != null && type.IsInstanceOfType(cursor)) return true;
            }
            return false;
        }
    }
}
