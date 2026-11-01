using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using SolastaCoop;

public enum TestCause { ClientTimeout }
public sealed class MainMenuScreen { public bool Visible = true, Hiding; public int LobbyEntries; public void Entered() { LobbyEntries++; } }
public struct TestRoomInfo { public int Id; }
public sealed class NetworkingManager
{
    public int Requests, Cleanup, Rejects, Joins;
    public object LastInfo;
    public Action OnRoomEntered { get; set; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void CreateRoom(TestRoomInfo info) { Requests++; LastInfo = info; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void JoinRoom(string room, string password) { Requests++; LastInfo = password; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnDisconnected(TestCause cause) { Cleanup++; OnRoomEntered = null; Photon.Pun.PhotonNetwork.OfflineMode = true; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnJoinedRoom() { Joins++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnCreateRoomFailed(short code, string text) { Rejects++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnJoinRoomFailed(short code, string text) { Rejects++; }
}
public sealed class MenuSubpanel { public int Cancellations; [MethodImpl(MethodImplOptions.NoInlining)] public void OnCancelCb() { Cancellations++; } }
public static class VerifyConnectionRetry
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
    private static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
    public static int Main(string[] args)
    {
        DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var b = new RetryBudget(); b.Begin();
        for (int i = 0; i < 3; i++)
        {
            Check(b.Fail("ClientTimeout", now), "Transient failure schedules retry " + (i + 1));
            Check(!b.Fail("ClientTimeout", now), "Duplicate failure does not schedule a second retry");
            Check(!b.Take(now, true) && !b.Take(now.AddSeconds(4), false), "Retry respects delay and disconnected-state gate");
            Check(b.Take(now.AddSeconds(4), true), "Due retry runs only once"); now = now.AddSeconds(15);
        }
        Check(!b.Fail("ClientTimeout", now) && !b.Active, "Three retries are a hard upper bound");
        foreach (string reason in new[] { "DisconnectByClientLogic", "InvalidAuthentication", "MaxCcuReached", "OperationNotAllowedInCurrentState" })
        { b.Begin(); Check(!b.Fail(reason, now) && !b.Active, "No retry for permanent/user cancellation: " + reason); }
        b.Begin(); b.Fail("ServerTimeout", now); b.Cancel(); Check(!b.Take(now.AddSeconds(10), true), "Cancel invalidates pending retry");
        Assembly plugin = Assembly.LoadFrom(args[0]); Type t = plugin.GetType("SolastaCoop.ConnectionRetry", true);
        Action<string, object[]> call = (n, a) => t.GetMethod(n, Flags).Invoke(null, a);
        Func<bool> pending = () => (bool)t.GetProperty("Pending", Flags).GetValue(null, null);
        call("Install", null); var manager = new NetworkingManager(); var roomInfo = new TestRoomInfo { Id = 42 };
        manager.CreateRoom(roomInfo); manager.OnDisconnected(TestCause.ClientTimeout);
        Check(manager.Cleanup == 1 && !pending() && manager.Requests == 1,
            "CreateRoom never retries after a timeout, preventing duplicate room names");
        manager.OnCreateRoomFailed(32766, "exists");
        Check(!pending() && manager.Rejects == 1, "32766 server error is passed through to native handler");
        var menu = new MainMenuScreen(); manager.OnRoomEntered = menu.Entered;
        manager.JoinRoom("private-room", "private-password"); manager.OnDisconnected(TestCause.ClientTimeout);
        Check(manager.OnRoomEntered == null && pending(),
            "Native menu cleanup still runs and JoinRoom is eligible for a guarded retry");
        object budget = t.GetField("budget", Flags).GetValue(null);
        budget.GetType().GetField("Due", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(budget, DateTime.MinValue);
        Photon.Pun.PhotonNetwork.NetworkingClient.State = Photon.Pun.FakeState.Disconnected;
        int requests = manager.Requests;
        call("Tick", null);
        Check(manager.Requests == requests && pending(), "JoinRoom does NOT retry while Photon error is unacknowledged");
        call("ConfirmErrorDismissed", null);
        call("Tick", null);
        Check(manager.Requests == requests + 1 && manager.LastInfo.Equals("private-password") &&
            manager.OnRoomEntered != null && manager.OnRoomEntered.GetInvocationList().Length == 1,
            "Acknowledged retry restores menu callback and retains private password without duplicate callbacks");
        manager.OnRoomEntered(); Check(menu.LobbyEntries == 1, "Restored callback opens the native lobby");
        manager.OnDisconnected(TestCause.ClientTimeout);
        requests = manager.Requests;
        call("Tick", null);
        Check(manager.Requests == requests && pending(), "Every new failure requires another acknowledgement");
        new MenuSubpanel().OnCancelCb();
        Check(!pending(), "Native menu cancel clears pending retry");
        manager.JoinRoom("private-room", "private-password"); manager.OnDisconnected(TestCause.ClientTimeout);
        Photon.Pun.PhotonNetwork.OfflineMode = false; Photon.Pun.PhotonNetwork.InRoom = true; manager.OnJoinedRoom();
        Check(!pending(), "Actual online join cancels the retry chain");
        Photon.Pun.PhotonNetwork.InRoom = false;
        Check(Photon.Pun.PhotonNetwork.NetworkingClient.LoadBalancingPeer.DisconnectTimeout == 10000,
            "Native ten-second timeout remains unchanged");
        call("SetEnabled", new object[] { false }); manager.JoinRoom("private-room", "password");
        manager.OnDisconnected(TestCause.ClientTimeout);
        Check(!pending(), "Disabled feature schedules no retries");
        Console.WriteLine("Connection retry checks: PASS (pure state machine and real Harmony with fake network manager)");
        return 0;
    }
}
