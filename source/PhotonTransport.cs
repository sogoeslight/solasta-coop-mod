using System;
using System.Reflection;

namespace SolastaCoop
{
    internal sealed class PhotonTransport
    {
        private object capturedSettings;
        private object originalProtocol;
        private bool originalFallback;

        internal bool TcpSelected(object settings)
        {
            if (settings == null) return false;
            object protocol = Read.Get(settings, "Protocol");
            return protocol != null && String.Equals(protocol.ToString(), "Tcp", StringComparison.OrdinalIgnoreCase);
        }

        internal string Change(object settings, string state, bool connected, bool restore)
        {
            if (settings == null)
                throw new InvalidOperationException("Photon settings are not available yet.");
            FieldInfo protocol = settings.GetType().GetField("Protocol");
            FieldInfo fallback = settings.GetType().GetField("EnableProtocolFallback");
            if (protocol == null || !protocol.FieldType.IsEnum || fallback == null || fallback.FieldType != typeof(bool))
                throw new InvalidOperationException("This Photon settings version is not supported.");
            object tcp = Enum.Parse(protocol.FieldType, "Tcp");
            if (capturedSettings != null && !ReferenceEquals(settings, capturedSettings))
                throw new InvalidOperationException("The settings instance changed. Restart the game before switching transport.");
            if (restore && capturedSettings == null)
                return "Original settings have not been changed.";

            object beforeProtocol = protocol.GetValue(settings);
            bool beforeFallback = (bool)fallback.GetValue(settings);
            object nextProtocol = restore ? originalProtocol : tcp;
            bool nextFallback = restore ? originalFallback : false;
            try
            {
                protocol.SetValue(settings, nextProtocol);
                fallback.SetValue(settings, nextFallback);
            }
            catch
            {
                protocol.SetValue(settings, beforeProtocol);
                fallback.SetValue(settings, beforeFallback);
                throw;
            }
            if (capturedSettings == null)
            {
                capturedSettings = settings;
                originalProtocol = beforeProtocol;
                originalFallback = beforeFallback;
            }
            return "Next connection: " + nextProtocol + "; automatic protocol fallback: " +
                (nextFallback ? "enabled" : "disabled") + "." +
                (connected || (state != "PeerCreated" && state != "Disconnected")
                    ? " The current connection is unchanged. Leave multiplayer completely and reconnect without restarting the game."
                    : " Connect normally.");
        }
    }
}
