using System;
using System.IO;
using System.Reflection;

namespace Photon.Pun
{
    public enum FakeState { PeerCreated, ConnectingToNameServer, ConnectingToMasterServer, ConnectedToMasterServer, Disconnected }
    public enum FakeProtocol { Udp, Tcp }
    public sealed class FakeSettings { public FakeProtocol Protocol; public bool EnableProtocolFallback = true; }
    public sealed class FakeServerSettings { public readonly FakeSettings AppSettings = new FakeSettings(); }
    public sealed class FakePeer
    {
        public FakeProtocol TransportProtocol = FakeProtocol.Udp;
        public int DisconnectTimeout { get; set; } = 10000;
        public string ServerAddress = "eu.example.test:4531", PeerState = "Connected";
        public long BytesIn = 1200, BytesOut = 800;
        public int RoundTripTime = 74, RoundTripTimeVariance = 9, ResentReliableCommands = 3;
        public int QueuedIncomingCommands = 0, QueuedOutgoingCommands = 1;
    }
    public sealed class FakeOperationResponse
    {
        public byte OperationCode;
        public short ReturnCode;
        public string DebugMessage = "secret-debug", Parameters = "secret-room-payload";
    }
    public sealed class FakeClient
    {
        public readonly FakePeer LoadBalancingPeer = new FakePeer();
        public FakeState State;
        public string DisconnectedCause = "None";
        public event Action<FakeState, FakeState> StateChanged;
        public event Action<FakeOperationResponse> OpResponseReceived;
        public int OperationSubscribers { get { return OpResponseReceived == null ? 0 : OpResponseReceived.GetInvocationList().Length; } }
        public void Respond(byte code, short result) { if (OpResponseReceived != null) OpResponseReceived(new FakeOperationResponse { OperationCode = code, ReturnCode = result }); }
        public string Server = "GameServer";
        public int Subscribers { get { return StateChanged == null ? 0 : StateChanged.GetInvocationList().Length; } }
        public void Change(FakeState state)
        {
            FakeState previous = State; State = state;
            if (StateChanged != null) StateChanged(previous, state);
        }
    }
    public static class PhotonNetwork
    {
        public static FakeClient NetworkingClient = new FakeClient();
        public static readonly FakeServerSettings PhotonServerSettings = new FakeServerSettings();
        public static FakeState NetworkClientState { get { return OfflineMode ? FakeState.ConnectedToMasterServer : NetworkingClient.State; } }
        public static bool IsConnected { get { return NetworkClientState == FakeState.ConnectedToMasterServer; } }
        public static bool IsConnectedAndReady { get { return IsConnected; } }
        public static bool InRoom = false, IsMasterClient = false;
        public static bool OfflineMode;
        public static string CloudRegion = "eu", Server = "MasterServer";
    }
}

public static class VerifyRuntime
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
        string isolated = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "solasta-runtime-" + Guid.NewGuid().ToString("N"));
        Type writerType = plugin.GetType("SolastaCoop.DiagnosticWriter", true);
        object writer = Activator.CreateInstance(writerType, Flags, null, new object[] { isolated, 1048576L, 3 }, null);
        plugin.GetType("SolastaCoop.Log", true).GetField("Writer", Flags).SetValue(null, writer);
        Type type = plugin.GetType("SolastaCoop.RuntimeDiagnostics", true);
        object runtime = Activator.CreateInstance(type, true);
        Action<string, object[]> call = (name, values) => type.GetMethod(name, Flags).Invoke(runtime, values);
        Func<string, string> get = name => (string)type.GetProperty(name, Flags).GetValue(runtime, null);
        call("Start", null);
        var first = Photon.Pun.PhotonNetwork.NetworkingClient;
        Check(first.Subscribers == 1, "Runtime attaches one Photon state event handler");
        Check(first.OperationSubscribers == 1, "Runtime attaches one Photon operation-response handler");
        call("Tick", new object[] { true });
        Check(first.Subscribers == 1, "Repeated sampling does not duplicate event subscriptions");
        first.Change(Photon.Pun.FakeState.ConnectingToNameServer);
        first.Change(Photon.Pun.FakeState.ConnectingToMasterServer);
        first.Change(Photon.Pun.FakeState.ConnectedToMasterServer);
        Photon.Pun.PhotonNetwork.PhotonServerSettings.AppSettings.Protocol = Photon.Pun.FakeProtocol.Tcp;
        call("Tick", new object[] { true });
        Check(get("LatestNetwork").Contains("actualProtocol=Udp") && get("LatestNetwork").Contains("nextProtocol=Tcp"),
            "Diagnostics distinguish requested TCP from an existing UDP connection");
        Check(get("LatestNetwork").Contains("rttMs=74") && get("LatestNetwork").Contains("resentReliable=3"),
            "Runtime captures peer latency and resend counters");
        first.DisconnectedCause = "ClientTimeout";
        first.Change(Photon.Pun.FakeState.Disconnected);
        Photon.Pun.PhotonNetwork.OfflineMode = true;
        first.Respond(227, 32758);
        call("Tick", new object[] { true });
        Check(Directory.GetFiles(isolated, "diagnostics-0.1.10-*.txt").Length > 0,
            "Rejected operations automatically save a failure report on the main tick");
        File.Delete((string)writerType.GetMethod("FilePath", Flags).Invoke(writer, new object[] { "network", 0 }));
        Check(get("LatestNetwork").Contains("disconnectCause=ClientTimeout"), "Runtime records the Photon disconnection cause");
        Check(get("LatestNetwork").Contains("state=Disconnected; facadeState=ConnectedToMasterServer; offline=True") &&
            get("LatestNetwork").Contains("disconnectTimeoutMs=10000") && get("LatestNetwork").Contains("eu.example.test:4531"),
            "Raw disconnected state is distinguished from the game's offline facade with endpoint and timeout");
        Type snapshot = plugin.GetType("SolastaCoop.RangeSnapshot", true);
        object range = snapshot.GetConstructors(Flags)[0].Invoke(new object[] {
            "Caster", "Target", "Spell", 7.2, 24.1, null, "evaluator=FillForMagicDistance; colourRule=withinRange&&visible", true });
        call("ObserveRange", new[] { range, "ready:FillForMagicDistance" });
        Check(get("LatestRange").Contains("textColor=green") && get("LatestRange").Contains("maximumRaw=24.1"),
            "Range diagnostics include raw range, displayed range and the colour decision");
        string report = (string)type.GetMethod("SaveReport", Flags).Invoke(runtime, null);
        string text = File.ReadAllText(report);
        Check(text.Contains("OperationResponse; code=227; returnCode=32758") && !text.Contains("secret-debug") && !text.Contains("secret-room-payload"),
            "Operation-response result codes are logged without private payloads or debug content");
        Check(text.Contains("LAST NETWORK FAILURE") && text.Contains("SESSION ERRORS") && text.Contains("cause=ClientTimeout") &&
            text.Contains("ConnectingToNameServer -> ConnectingToMasterServer"),
            "Failure cause and original connection history survive removal of the on-disk network log");
        Check(text.Contains("previousPhaseMs=") && text.Contains("bytesIn=1200"), "State events retain phase timing and socket byte counters");
        Check(text.Contains("ConnectingToNameServer -> ConnectingToMasterServer"),
            "Event logging preserves fast transitions between periodic samples");
        Check(text.Contains("CURRENT NETWORK") && text.Contains("CURRENT RANGE") && text.Contains("API CHECKS") &&
            text.Contains("No automated visual or multiplayer"), "Report includes context, API checks and explicit test limits");
        string root = Path.GetDirectoryName(report);
        for (int i = 0; i < 6; i++)
            Check(type.GetMethod("SaveReport", Flags).Invoke(runtime, null) != null, "Report save succeeds " + i);
        Check(Directory.GetFiles(root, "diagnostics-0.1.10-*.txt").Length == 5, "Report retention keeps five reports");
        var replacement = new Photon.Pun.FakeClient();
        Photon.Pun.PhotonNetwork.NetworkingClient = replacement;
        call("Tick", new object[] { true });
        Check(first.Subscribers == 0 && replacement.Subscribers == 1 && first.OperationSubscribers == 0 && replacement.OperationSubscribers == 1,
            "Client replacement detaches old state and operation callbacks");
        call("Dispose", null);
        Check(replacement.Subscribers == 0 && replacement.OperationSubscribers == 0, "Overlay shutdown removes state and operation callbacks");
        Console.WriteLine("Runtime diagnostics tests: PASS (fake Photon and game services)");
        return 0;
    }
}
