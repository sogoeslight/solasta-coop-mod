using System;
using System.Collections.Generic;
using System.Reflection;
using SolastaCoop;

public sealed class GameCampaignScreen { public bool Visible, Hiding; }
public static class Gui { public static readonly FakeGuiService GuiService = new FakeGuiService(); }
public sealed class FakeGuiService
{
    public readonly GameCampaignScreen Map = new GameCampaignScreen();
    public object GetScreen(Type t) { return t == typeof(GameCampaignScreen) ? Map : null; }
}
namespace Photon.Pun
{
    public sealed class FakePlayer { public int ActorNumber; }
    public sealed class FakeRoom
    {
        public readonly Dictionary<int, FakePlayer> Players = new Dictionary<int, FakePlayer> {
            { 1, new FakePlayer { ActorNumber = 1 } }, { 2, new FakePlayer { ActorNumber = 2 } }, { 3, new FakePlayer { ActorNumber = 3 } } };
    }
    public sealed class FakeEvent { public byte Code = 190; public int Sender; public object CustomData; }
    public sealed class FakeClient
    {
        public object LoadBalancingPeer = new FakePeer();
        public event Action<FakeEvent> EventReceived;
        public int Subscribers { get { return EventReceived == null ? 0 : EventReceived.GetInvocationList().Length; } }
        public void Receive(int actor, object[] data) { if (EventReceived != null) EventReceived(new FakeEvent { Sender = actor, CustomData = data }); }
    }
    public sealed class FakePeer { public int QueuedOutgoingCommands; }
    public sealed class FakeOptions { public int[] TargetActors; }
    public struct FakeSend { public bool Reliability; }
    public static class PhotonNetwork
    {
        public static bool InRoom = true, OfflineMode, IsMasterClient;
        public static FakePlayer MasterClient = new FakePlayer { ActorNumber = 1 };
        public static FakeRoom CurrentRoom = new FakeRoom();
        public static FakeClient NetworkingClient = new FakeClient();
        public static int Sends;
        public static byte LastCode;
        public static object[] LastPacket;
        public static bool RaiseEvent(byte code, object content, FakeOptions options, FakeSend send)
        { Sends++; LastCode = code; LastPacket = (object[])content; return true; }
    }
}
public static class VerifyTravelShare
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    private static void Check(bool value, string text) { if (!value) throw new Exception(text); Console.WriteLine("PASS " + text); }
    public static int Main(string[] args)
    {
        byte[] jpeg = { 255,216,255,192,0,8,8,0,32,0,32,0,255,217 };
        Check(TravelWire.Valid(new object[] { TravelWire.Magic, TravelWire.Hello, true }), "Handshake is a bounded, explicitly versioned packet");
        Check(TravelWire.Jpeg(jpeg), "Image header allows dimensions within the configured capture limit");
        byte[] large = (byte[])jpeg.Clone(); large[9] = 127;
        Check(!TravelWire.Jpeg(large) && !TravelWire.Jpeg(new byte[TravelWire.MaxFrameBytes + 1]), "Oversized dimensions and payloads are rejected before texture allocation");
        Check(!TravelWire.Valid(new object[] { "foreign-mod", 0, true }) && !TravelWire.Valid(new object[] { TravelWire.Magic, 4, true }), "Foreign and unknown packets cannot affect the travel display");
        Check(!TravelWire.Valid(new object[] { TravelWire.Magic, TravelWire.Pointer, 1, Single.NaN, 0f, true }) &&
            !TravelWire.Valid(new object[] { TravelWire.Magic, TravelWire.Pointer, 1, 1.1f, 0f, true }), "NaN and out-of-window pointer coordinates are rejected");
        Check(TravelWire.HostFrame(1, 1, true) && !TravelWire.HostFrame(3, 1, true) && !TravelWire.HostFrame(1, 1, false), "Only the current room host can supply frames");
        Assembly plugin = Assembly.LoadFrom(args[0]); Type type = plugin.GetType("SolastaCoop.TravelShare", true);
        object share = Activator.CreateInstance(type, true);
        Action tick = () => type.GetMethod("Tick", F).Invoke(share, null);
        Func<string, object> get = n => type.GetField(n, F).GetValue(share);
        tick();
        Check(!(bool)get("previewShown") && Photon.Pun.PhotonNetwork.LastPacket[2].Equals(false),
            "Travel preview starts hidden and does not request frames before F6");
        type.GetMethod("TogglePreview", F).Invoke(share, null); tick();
        Check((bool)get("previewShown") && Photon.Pun.PhotonNetwork.LastPacket[2].Equals(true),
            "F6 explicitly enables the preview and advertises interest to the host");
        var first = Photon.Pun.PhotonNetwork.NetworkingClient;
        Check(first.Subscribers == 1 && Photon.Pun.PhotonNetwork.LastCode == 190 && Photon.Pun.PhotonNetwork.LastPacket[0].Equals(TravelWire.Magic), "Actual display adapter subscribes once and sends the private-channel handshake");
        first.Receive(3, new object[] { TravelWire.Magic, TravelWire.Hello, true });
        first.Receive(3, new object[] { TravelWire.Magic, TravelWire.Frame, 1, jpeg });
        tick(); Check(get("image") == null, "Non-host frame is ignored by the actual receive adapter");
        first.Receive(1, new object[] { TravelWire.Magic, TravelWire.Hello, true });
        first.Receive(3, new object[] { TravelWire.Magic, TravelWire.Pointer, 1, 0.5f, 0.25f, true });
        tick();
        Check(((System.Collections.IDictionary)get("peers")).Count == 2, "In-room participant pointers and handshakes are accepted without game commands");
        type.GetField("nextPointer", F).SetValue(share, DateTime.MaxValue);
        Photon.Pun.PhotonNetwork.IsMasterClient = true; Gui.GuiService.Map.Visible = true;
        tick();
        Check((bool)type.GetProperty("ShouldCapture", F).GetValue(share, null), "Host capture is requested only with a visible travel screen and interested peers");
        Gui.GuiService.Map.Visible = false; tick();
        Check(!(bool)type.GetProperty("ShouldCapture", F).GetValue(share, null) && (int)Photon.Pun.PhotonNetwork.LastPacket[1] == TravelWire.End,
            "Leaving the travel screen sends End and stops capture");
        Photon.Pun.PhotonNetwork.NetworkingClient = new Photon.Pun.FakeClient(); tick();
        Check(first.Subscribers == 0 && Photon.Pun.PhotonNetwork.NetworkingClient.Subscribers == 1, "Network client replacement detaches the old event handler");
        type.GetMethod("SetEnabled", F).Invoke(share, new object[] { false });
        Check(Photon.Pun.PhotonNetwork.NetworkingClient.Subscribers == 0, "Disabling removes the display-channel subscription");
        Console.WriteLine("Travel view checks: PASS (wire validation and adapter fixtures; live Unity rendering not tested)");
        return 0;
    }
}
