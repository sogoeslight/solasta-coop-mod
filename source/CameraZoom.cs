using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SolastaCoop
{
    internal static class CameraZoom
    {
        private sealed class SavedOrbit
        {
            internal object Camera;
            internal Array Original, Applied;
        }
        private static readonly Dictionary<object, SavedOrbit> saved = new Dictionary<object, SavedOrbit>();
        private static bool installed;
        internal static float Factor { get; private set; } = 1f;
        internal static string Status { get; private set; } = "Default camera zoom.";

        private static bool Supported(object mode)
        {
            if (mode == null) return false;
            string name = mode.GetType().Name;
            return name == "CameraModeBattleManual" || name == "CameraModeBattleFollow" ||
                name == "CameraModeExplorationManual" || name == "CameraModeExplorationFollow";
        }

        internal static void SetFactor(float factor)
        {
            try
            {
                if (Single.IsNaN(factor) || Single.IsInfinity(factor) || factor < 1f || factor > 3f)
                    throw new ArgumentOutOfRangeException("factor", "Zoom-out multiplier must be between 1 and 3.");
                factor = (float)Math.Round(factor, 1);
                if (factor > 1f && !installed)
                {
                    Type mode = Read.Type("CameraModeManual");
                    MethodInfo method = mode == null ? null : mode.GetMethod("AdjustOrbit", BindingFlags.NonPublic | BindingFlags.Instance);
                    BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
                    HarmonySupport.Patch(method, typeof(CameraZoom).GetMethod("BeforeOrbit", flags), typeof(CameraZoom).GetMethod("AfterOrbit", flags));
                    installed = true;
                }
                RestoreAll();
                Factor = factor;
                Status = factor == 1f ? "Default camera zoom." : "Maximum camera zoom-out: ×" + factor.ToString("0.0");
                Log.Write("Camera zoom factor=" + factor.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception e)
            {
                RestoreAll(); Factor = 1f;
                Status = "Camera adjustment disabled: " + e.GetBaseException().Message;
                Log.Error("core", "Camera zoom", e);
            }
        }

        internal static void Tick()
        {
            if (saved.Count == 0) return;
            try
            {
                object controller = Read.Get(Read.Service("ICameraService"), "CurrentCameraController");
                Type location = Read.Type("CameraControllerLocation");
                if (location == null || !location.IsInstanceOfType(controller) ||
                    !Supported(Read.Get(controller, "CurrentCameraMode"))) RestoreAll();
            }
            catch (Exception e) { Disable(e); }
        }

        private static bool CurrentGameplayMode(object mode)
        {
            if (!Supported(mode)) return false;
            object controller = Read.Get(Read.Service("ICameraService"), "CurrentCameraController");
            Type location = Read.Type("CameraControllerLocation");
            return location != null && location.IsInstanceOfType(controller) &&
                ReferenceEquals(Read.Get(controller, "CurrentCameraMode"), mode);
        }

        private static void BeforeOrbit(object __instance)
        {
            try { Restore(Read.Get(__instance, "FreeLookCamera")); }
            catch (Exception e) { Disable(e); }
        }

        private static void AfterOrbit(object __instance)
        {
            if (Factor <= 1f) return;
            try
            {
                if (!CurrentGameplayMode(__instance)) return;
                object camera = Read.Get(__instance, "FreeLookCamera");
                Array original = Read.Get(camera, "m_Orbits") as Array;
                if (original == null || original.Length != 3) throw new InvalidOperationException("The three camera orbits were not found.");
                Array result = (Array)original.Clone();
                for (int i = 0; i < 2; i++)
                {
                    object orbit = result.GetValue(i);
                    float weight = i == 0 ? Factor : 1f + (Factor - 1f) * 0.5f;
                    float radius = Convert.ToSingle(Read.Get(orbit, "m_Radius"));
                    float height = Convert.ToSingle(Read.Get(orbit, "m_Height"));
                    ObserverHud.SetMember(orbit, "m_Radius", radius * weight);
                    ObserverHud.SetMember(orbit, "m_Height", height * weight);
                    result.SetValue(orbit, i);
                }
                saved[camera] = new SavedOrbit { Camera = camera, Original = (Array)original.Clone(), Applied = result };
                ObserverHud.SetMember(camera, "m_Orbits", result);
            }
            catch (Exception e) { Disable(e); }
        }

        private static void Restore(object camera)
        {
            SavedOrbit snapshot;
            if (camera == null || !saved.TryGetValue(camera, out snapshot)) return;
            saved.Remove(camera);
            // Respect another mod replacing the orbit array after our adjustment.
            if (ReferenceEquals(Read.Get(camera, "m_Orbits"), snapshot.Applied))
                ObserverHud.SetMember(camera, "m_Orbits", snapshot.Original);
        }

        internal static void RestoreAll()
        {
            foreach (object camera in saved.Keys.ToArray())
            {
                try { Restore(camera); }
                catch (Exception e) { Log.Error("core", "Restore camera orbit", e); }
            }
            saved.Clear();
        }

        private static void Disable(Exception error)
        {
            Factor = 1f;
            RestoreAll();
            Status = "Camera adjustment disabled after error: " + error.GetBaseException().Message;
            Log.Error("core", "Camera orbit adjustment", error);
        }
    }
}
