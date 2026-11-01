using System;
using System.Collections.Generic;
using UnityEngine;

namespace SolastaCoop
{
    // Visual playback acceleration only. Never touch Time.timeScale, fixedDeltaTime,
    // game commands, Photon timers, or combat turn scheduling.
    internal static class CombatSpeed
    {
        private const float ScanIntervalSeconds = 0.55f;
        private static readonly Dictionary<Animator, float> AnimatorOriginal =
            new Dictionary<Animator, float>();
        private static readonly Dictionary<AnimationState, float> LegacyOriginal =
            new Dictionary<AnimationState, float>();
        private static readonly Dictionary<ParticleSystem, float> ParticleOriginal =
            new Dictionary<ParticleSystem, float>();
        private static float nextScan;
        private static bool active;
        private static string lastError = "";

        internal static float Factor { get; private set; } = 2f;
        internal static string Status
        {
            get
            {
                return "Анимации боя: ×" + Factor.ToString("0.00") +
                    "; бой=" + (active ? "да" : "нет") +
                    "; Animator=" + AnimatorOriginal.Count +
                    "; AnimationState=" + LegacyOriginal.Count +
                    "; VFX=" + ParticleOriginal.Count +
                    (lastError.Length == 0 ? "" : "; ошибка=" + lastError);
            }
        }

        internal static void SetFactor(float value)
        {
            if (Single.IsNaN(value) || Single.IsInfinity(value)) return;
            float rounded = (float)Math.Round(Math.Max(1f, Math.Min(3f, value)) * 4f) / 4f;
            if (Math.Abs(rounded - Factor) < 0.01f) return;
            RestoreAll();
            Factor = rounded;
            nextScan = 0f;
            Log.Event("core", "INFO", "Combat visual speed factor=" + Factor.ToString("0.00") + ", Time.timeScale unchanged");
        }

        internal static void Tick()
        {
            try
            {
                bool inBattle = Read.Get(Read.Type("Gui"), "Battle") != null;
                if (!inBattle || Factor <= 1f)
                {
                    if (active || AnimatorOriginal.Count != 0 || LegacyOriginal.Count != 0 || ParticleOriginal.Count != 0)
                        RestoreAll();
                    return;
                }
                if (Time.unscaledTime < nextScan) return;
                nextScan = Time.unscaledTime + ScanIntervalSeconds;
                if (!active)
                {
                    active = true;
                    Log.Event("core", "INFO", "Combat visual acceleration enabled, factor=" + Factor.ToString("0.00"));
                }
                AccelerateAnimators();
                AccelerateLegacyAnimations();
                AccelerateParticles();
            }
            catch (Exception ex)
            {
                string message = ex.GetBaseException().Message;
                if (message != lastError) Log.Error("core", "Combat visual acceleration", ex);
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

        private static void AccelerateAnimators()
        {
            foreach (Animator animator in UnityEngine.Object.FindObjectsOfType<Animator>())
            {
                if (!IsWorldEffect(animator)) continue;
                float original;
                if (!AnimatorOriginal.TryGetValue(animator, out original))
                {
                    original = animator.speed;
                    if (original <= 0f) continue;
                    AnimatorOriginal.Add(animator, original);
                }
                // Reapply when the game reinitializes the playback speed.
                if (Math.Abs(animator.speed - original) < 0.001f)
                    animator.speed = original * Factor;
            }
        }

        private static void AccelerateLegacyAnimations()
        {
            foreach (Animation animation in UnityEngine.Object.FindObjectsOfType<Animation>())
            {
                if (!IsWorldEffect(animation)) continue;
                foreach (AnimationState state in animation)
                {
                    if (state == null) continue;
                    float original;
                    if (!LegacyOriginal.TryGetValue(state, out original))
                    {
                        original = state.speed;
                        if (original <= 0f) continue;
                        LegacyOriginal.Add(state, original);
                    }
                    if (Math.Abs(state.speed - original) < 0.001f)
                        state.speed = original * Factor;
                }
            }
        }

        private static void AccelerateParticles()
        {
            foreach (ParticleSystem particle in UnityEngine.Object.FindObjectsOfType<ParticleSystem>())
            {
                if (!IsWorldEffect(particle)) continue;
                ParticleSystem.MainModule main = particle.main;
                float original;
                if (!ParticleOriginal.TryGetValue(particle, out original))
                {
                    original = main.simulationSpeed;
                    if (original <= 0f) continue;
                    ParticleOriginal.Add(particle, original);
                }
                if (Math.Abs(main.simulationSpeed - original) < 0.001f)
                    main.simulationSpeed = original * Factor;
            }
        }

        internal static void RestoreAll()
        {
            foreach (KeyValuePair<Animator, float> entry in AnimatorOriginal)
            {
                try
                {
                    if (entry.Key != null && entry.Key &&
                        Math.Abs(entry.Key.speed - entry.Value * Factor) < 0.001f)
                        entry.Key.speed = entry.Value;
                }
                catch (Exception) { }
            }
            AnimatorOriginal.Clear();
            foreach (KeyValuePair<AnimationState, float> entry in LegacyOriginal)
            {
                try
                {
                    if (entry.Key != null &&
                        Math.Abs(entry.Key.speed - entry.Value * Factor) < 0.001f)
                        entry.Key.speed = entry.Value;
                }
                catch (Exception) { }
            }
            LegacyOriginal.Clear();
            foreach (KeyValuePair<ParticleSystem, float> entry in ParticleOriginal)
            {
                try
                {
                    if (entry.Key != null && entry.Key)
                    {
                        ParticleSystem.MainModule main = entry.Key.main;
                        if (Math.Abs(main.simulationSpeed - entry.Value * Factor) < 0.001f)
                            main.simulationSpeed = entry.Value;
                    }
                }
                catch (Exception) { }
            }
            ParticleOriginal.Clear();
            if (active) Log.Event("core", "INFO", "Combat visual acceleration restored to game playback values.");
            active = false;
        }
    }
}
