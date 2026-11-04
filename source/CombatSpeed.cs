using System;
using System.Collections.Generic;
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

        internal static float Factor { get; private set; } = 2f;
        internal static float ExplorationFactor { get; private set; } = 2f;
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
            foreach (string property in new[] { "MoveInProgress", "MoveStepInProgress", "MovingToDestination", "Falling", "Jumping", "Climbing", "Dropping" })
                if (Object.Equals(Read.Get(character, property), true)) return true;
            return false;
        }

        private static void AccelerateAnimators()
        {
            bool battle = Read.Get(Read.Type("Gui"), "Battle") != null;
            Type world = Read.Type("WorldLocationCharacter");
            foreach (Animator animator in UnityEngine.Object.FindObjectsOfType<Animator>())
            {
                if (!battle)
                {
                    Component owner = world == null ? null : animator.GetComponentInParent(world);
                    if (owner == null || !IsMoving(Read.Get(owner, "GameLocationCharacter"))) continue;
                }
                ApplyAnimator(animator);
            }
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
