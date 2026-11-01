using System;
using SolastaCoop;

public static class VerifyTransport
{
    public enum Protocol { Udp, Tcp }
    public sealed class Settings
    {
        public Protocol Protocol = Protocol.Udp;
        public bool EnableProtocolFallback = true;
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
    public static int Main()
    {
        var settings = new Settings();
        var transport = new PhotonTransport();
        transport.Change(settings, "Disconnected", false, false);
        Require(settings.Protocol == Protocol.Tcp && !settings.EnableProtocolFallback,
            "TCP selected; protocol fallback disabled");
        transport.Change(settings, "PeerCreated", false, false);
        transport.Change(settings, "Disconnected", false, true);
        Require(settings.Protocol == Protocol.Udp && settings.EnableProtocolFallback,
            "Repeated TCP selection preserves original settings for restore");
        foreach (string state in new[] { "ConnectingToNameServer", "ConnectingToMasterServer", "ConnectedToMasterServer", "Joined", "Disconnecting", "?" })
        {
            string message = transport.Change(settings, state, true, false);
            Require(settings.Protocol == Protocol.Tcp && !settings.EnableProtocolFallback &&
                message.Contains("Текущее подключение не изменено"),
                "Next-connection selection allowed with explicit reconnect notice in state " + state);
            transport.Change(settings, state, true, true);
            Require(settings.Protocol == Protocol.Udp && settings.EnableProtocolFallback,
                "Restore also works while connected in state " + state);
        }
        bool nullRejected = false;
        try { new PhotonTransport().Change(null, "Disconnected", false, false); }
        catch (InvalidOperationException) { nullRejected = true; }
        Require(nullRejected, "Missing settings are rejected");
        var initiallyTcp = new Settings { Protocol = Protocol.Tcp, EnableProtocolFallback = false };
        var secondTransport = new PhotonTransport();
        secondTransport.Change(initiallyTcp, "Disconnected", false, false);
        secondTransport.Change(initiallyTcp, "Disconnected", false, true);
        Require(initiallyTcp.Protocol == Protocol.Tcp && !initiallyTcp.EnableProtocolFallback,
            "Restore respects an originally TCP configuration");
        bool replacementRejected = false;
        try { transport.Change(new Settings(), "Disconnected", false, false); }
        catch (InvalidOperationException) { replacementRejected = true; }
        Require(replacementRejected, "Settings object replacement is rejected");
        Console.WriteLine("Transport tests: PASS");
        return 0;
    }
}
