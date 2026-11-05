using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = System.Object;

namespace SolastaCoop
{
    // Playback and native movement share one factor. Global clocks remain native.
    internal static class CombatSpeed
    {
        private const float ScanIntervalSeconds = 0.2f;
        private static readonly Dictionary<Animator, float> AnimatorOriginal = new Dictionary<Animator, float>();
        private static readonly Dictionary<AnimationState, float> LegacyOriginal = new Dictionary<AnimationState, float>();
        private static readonly Dictionary<ParticleSystem, float> ParticleOriginal = new Dictionary<ParticleSystem, float>();
        private static float nextScan, playbackFactor = 1f;
        private static bool active;
        private static string lastError = "";
        private static object explorationLocation;
        private static UnityEngine.Object[] explorationCharacters = new UnityEngine.Object[0];
        private static float nextCharacterScan;
        private static readonly Dictionary<Type, MemberInfo[]> MovementMembers = new Dictionary<Type, MemberInfo[]>();

        internal const float DefaultCombatFactor = 1.5f;
        internal const float DefaultExplorationFactor = 1.75f;
        internal static float Factor { get; private set; } = DefaultCombatFactor;
        internal static float ExplorationFactor { get; private set; } = DefaultExplorationFactor;
        internal static string Status
        {
            get { return "Combat ×" + Factor.ToString("0.00") + "; exploration ×" + ExplorationFactor.ToString("0.00") +
                "; active=" + active + "; Animator=" + AnimatorOriginal.Count + "; VFX=" + ParticleOriginal.Count +
                (lastError.Length == 0 ? "" : "; error=" + lastError); }
        }

        private static float Normalize(float value)
        {
            return (float)Math.Round(Math.Max(1f, Math.Min(3f, value)) * 4f) / 4f;
        }

        internal static void SetFactor(float value)
        {
            if (Single.IsNaN(value) || Single.IsInfinity(value)) return;
            float normalized = Normalize(value);
            if (Math.Abs(normalized - Factor) < 0.01f) return;
            RestoreAll();
            Factor = normalized;
            nextScan = 0f;
            Log.Event("core", "INFO", "Combat speed factor=" + Factor.ToString("0.00"));
        }

        internal static void SetExplorationFactor(float value)
        {
            if (Single.IsNaN(value) || Single.IsInfinity(value)) return;
            float normalized = Normalize(value);
            if (Math.Abs(normalized - ExplorationFactor) < 0.01f) return;
            RestoreAll();
            ExplorationFactor = normalized;
            nextScan = 0f;
            Log.Event("core", "INFO", "Exploration movement speed factor=" + ExplorationFactor.ToString("0.00"));
        }

        internal static void Tick()
        {
            try
            {
                bool inBattle = Read.Get(Read.Type("Gui"), "Battle") != null;
                if (Time.unscaledTime < nextScan) return;
                nextScan = Time.unscaledTime + ScanIntervalSeconds;
                RestoreAll();
                playbackFactor = MovementSpeed.Factor;
                if (playbackFactor <= 1f) return;
                active = true;
                AccelerateAnimators();
                if (inBattle)
                {
                    AccelerateLegacyAnimations();
                    AccelerateParticles();
                }
            }
            catch (Exception ex)
            {
                string message = ex.GetBaseException().Message;
                if (message != lastError) Log.Error("core", "Playback acceleration", ex);
                lastError = message;
                RestoreAll();
                nextScan = Time.unscaledTime + 2f;
            }
        }

        private static bool IsWorldEffect(Component component)
        {
            return component != null && component.gameObject != null &&
                component.gameObject.activeInHierarchy && component.GetComponentInParent<Canvas>() == null;
        }

        private static void ApplyAnimator(Animator animator)
        {
            if (!IsWorldEffect(animator) || AnimatorOriginal.ContainsKey(animator) || animator.speed <= 0f) return;
            AnimatorOriginal.Add(animator, animator.speed);
            animator.speed *= playbackFactor;
        }

        internal static void AccelerateCharacter(object world, float factor)
        {
            if (Math.Abs(playbackFactor - factor) > 0.001f) { RestoreAll(); playbackFactor = factor; }
            Animator animator = Read.Get(Read.Get(world, "GraphicsCharacter"), "Animator") as Animator;
            if (animator != null) ApplyAnimator(animator);
        }

        private static bool IsMoving(object character)
        {
            if (character == null) return false;
            Type type = character.GetType();
            MemberInfo[] members;
            if (!MovementMembers.TryGetValue(type, out members))
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var found = new List<MemberInfo>();
                foreach (string name in new[] { "MoveInProgress", "MoveStepInProgress", "MovingToDestination", "Falling", "Jumping", "Climbing", "Dropping" })
                {
                    PropertyInfo property = type.GetProperty(name, flags);
                    if (property != null) found.Add(property);
                    else { FieldInfo field = type.GetField(name, flags); if (field != null) found.Add(field); }
                }
                members = found.ToArray(); MovementMembers[type] = members;
            }
            foreach (MemberInfo member in members)
            {
                PropertyInfo property = member as PropertyInfo;
                object value = property != null ? property.GetValue(character, null) : ((FieldInfo)member).GetValue(character);
                if (Object.Equals(value, true)) return true;
            }
            return false;
        }

        private static void AccelerateAnimators()
        {
            bool battle = Read.Get(Read.Type("Gui"), "Battle") != null;
            if (!battle)
            {
                object current = Read.Get(Read.Type("Gui"), "GameLocation");
                if (current == null) { explorationCharacters = new UnityEngine.Object[0]; explorationLocation = null; return; }
                if (!Object.ReferenceEquals(current, explorationLocation) || Time.unscaledTime >= nextCharacterScan)
                {
                    explorationLocation = current;
                    nextCharacterScan = Time.unscaledTime + 1f;
                    Type world = Read.Type("WorldLocationCharacter");
                    explorationCharacters = world == null ? new UnityEngine.Object[0] : UnityEngine.Object.FindObjectsOfType(world);
                }
                foreach (UnityEngine.Object owner in explorationCharacters)
                    if (owner != null && IsMoving(Read.Get(owner, "GameLocationCharacter")))
                        ApplyAnimator(Read.Get(Read.Get(owner, "GraphicsCharacter"), "Animator") as Animator);
                return;
            }
            foreach (Animator animator in UnityEngine.Object.FindObjectsOfType<Animator>()) ApplyAnimator(animator);
        }

        private static void AccelerateLegacyAnimations()
        {
            foreach (Animation animation in UnityEngine.Object.FindObjectsOfType<Animation>())
            {
                if (!IsWorldEffect(animation)) continue;
                foreach (AnimationState state in animation)
                {
                    if (state == null || state.speed <= 0f) continue;
                    LegacyOriginal[state] = state.speed;
                    state.speed *= playbackFactor;
                }
            }
        }

        private static void AccelerateParticles()
        {
            foreach (ParticleSystem particle in UnityEngine.Object.FindObjectsOfType<ParticleSystem>())
            {
                if (!IsWorldEffect(particle)) continue;
                ParticleSystem.MainModule main = particle.main;
                if (main.simulationSpeed <= 0f) continue;
                ParticleOriginal[particle] = main.simulationSpeed;
                main.simulationSpeed *= playbackFactor;
            }
        }

        internal static void RestoreAll()
        {
            foreach (var entry in AnimatorOriginal)
                try { if (entry.Key != null && Math.Abs(entry.Key.speed - entry.Value * playbackFactor) < 0.001f) entry.Key.speed = entry.Value; }
                catch (Exception) { }
            AnimatorOriginal.Clear();
            foreach (var entry in LegacyOriginal)
                try { if (entry.Key != null && Math.Abs(entry.Key.speed - entry.Value * playbackFactor) < 0.001f) entry.Key.speed = entry.Value; }
                catch (Exception) { }
            LegacyOriginal.Clear();
            foreach (var entry in ParticleOriginal)
                try
                {
                    if (entry.Key == null) continue;
                    ParticleSystem.MainModule main = entry.Key.main;
                    if (Math.Abs(main.simulationSpeed - entry.Value * playbackFactor) < 0.001f) main.simulationSpeed = entry.Value;
                }
                catch (Exception) { }
            ParticleOriginal.Clear();
            active = false;
        }
    }
}
