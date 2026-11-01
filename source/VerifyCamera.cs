using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

public sealed class ICameraService { public object CurrentCameraController; }
public sealed class CameraControllerLocation { public object CurrentCameraMode; }
public sealed class FakeOtherCameraController { public object CurrentCameraMode; }
public struct FakeOrbit { public float m_Height, m_Radius; }
public sealed class FakeFreeLook
{
    public FakeOrbit[] m_Orbits = {
        new FakeOrbit { m_Height = 30, m_Radius = 20 },
        new FakeOrbit { m_Height = 20, m_Radius = 15 },
        new FakeOrbit { m_Height = 4, m_Radius = 5 }
    };
}
public class CameraModeManual
{
    public FakeFreeLook FreeLookCamera = new FakeFreeLook();
    public readonly float[] DefaultRadii = { 20, 15, 5 };
    [MethodImpl(MethodImplOptions.NoInlining)] private void AdjustOrbit()
    {
        // Simulate the game's use of the previous frame when smoothing orbit collision corrections.
        for (int i = 0; i < FreeLookCamera.m_Orbits.Length; i++)
            FreeLookCamera.m_Orbits[i].m_Radius = (FreeLookCamera.m_Orbits[i].m_Radius + DefaultRadii[i]) * 0.5f;
    }
    public void Frame() { AdjustOrbit(); }
}
public sealed class CameraModeBattleManual : CameraModeManual { }
public sealed class CameraModeBattleFollow : CameraModeManual { }
public sealed class CameraModeExplorationManual : CameraModeManual { }
public sealed class CameraModeExplorationFollow : CameraModeManual { }
public sealed class FakeCinematicMode : CameraModeManual { }

public static class VerifyCamera
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
    public static int Main(string[] args)
    {
        Assembly plugin = Assembly.LoadFrom(args[0]);
        string directory = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "solasta-camera-" + Guid.NewGuid().ToString("N"));
        Type writer = plugin.GetType("SolastaCoop.DiagnosticWriter", true);
        plugin.GetType("SolastaCoop.Log", true).GetField("Writer", Flags).SetValue(null,
            Activator.CreateInstance(writer, Flags, null, new object[] { directory, 1048576L, 3 }, null));
        Type zoom = plugin.GetType("SolastaCoop.CameraZoom", true);
        Action<float> factor = value => zoom.GetMethod("SetFactor", Flags).Invoke(null, new object[] { value });
        Func<float> current = () => (float)zoom.GetProperty("Factor", Flags).GetValue(null, null);
        var mode = new CameraModeBattleManual();
        var controller = new CameraControllerLocation { CurrentCameraMode = mode };
        var service = new ICameraService { CurrentCameraController = controller };
        ServiceRepository.Services[typeof(ICameraService)] = service;
        factor(2f); mode.Frame();
        Check(current() == 2f && mode.FreeLookCamera.m_Orbits[0].m_Radius == 40 && mode.FreeLookCamera.m_Orbits[0].m_Height == 60,
            "Far orbit height and radius scale with the selected multiplier");
        Check(mode.FreeLookCamera.m_Orbits[1].m_Radius == 22.5f && mode.FreeLookCamera.m_Orbits[2].m_Radius == 5,
            "Middle orbit transitions smoothly and close zoom stays unchanged");
        mode.Frame(); mode.Frame();
        Check(mode.FreeLookCamera.m_Orbits[0].m_Radius == 40 && mode.DefaultRadii[0] == 20,
            "Repeated native smoothing does not compound scale or mutate camera parameter assets");
        factor(3f); mode.Frame();
        Check(mode.FreeLookCamera.m_Orbits[0].m_Radius == 60, "Changing factor replaces the previous scale");
        factor(1f);
        Check(mode.FreeLookCamera.m_Orbits[0].m_Radius == 20 && mode.FreeLookCamera.m_Orbits[0].m_Height == 30,
            "Reset restores the exact unscaled orbit values immediately");
        factor(2f); mode.Frame();
        var cinematic = new FakeCinematicMode { FreeLookCamera = mode.FreeLookCamera };
        controller.CurrentCameraMode = cinematic;
        zoom.GetMethod("Tick", Flags).Invoke(null, null);
        cinematic.Frame();
        Check(cinematic.FreeLookCamera.m_Orbits[0].m_Radius == 20, "Switching to a cinematic mode restores a shared camera");
        service.CurrentCameraController = new FakeOtherCameraController { CurrentCameraMode = mode };
        mode.Frame();
        Check(mode.FreeLookCamera.m_Orbits[0].m_Radius == 20, "Non-location camera controllers are excluded");
        service.CurrentCameraController = controller; controller.CurrentCameraMode = mode;
        mode.Frame();
        var other = new[] { new FakeOrbit { m_Radius = 99 }, new FakeOrbit { m_Radius = 88 }, new FakeOrbit { m_Radius = 77 } };
        mode.FreeLookCamera.m_Orbits = other;
        factor(1f);
        Check(ReferenceEquals(mode.FreeLookCamera.m_Orbits, other) && other[0].m_Radius == 99,
            "Reset respects an orbit array replaced by another mod");
        mode.FreeLookCamera = new FakeFreeLook { m_Orbits = new FakeOrbit[2] };
        factor(2f); mode.Frame();
        Check(current() == 1f, "An unsupported orbit layout disables adjustment without breaking the camera callback");
        factor(Single.NaN);
        Check(current() == 1f, "Invalid slider values leave the default camera mode active");
        Console.WriteLine("Camera zoom tests: PASS (real Harmony, fake native camera)");
        return 0;
    }
}
