using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SolastaCoop
{
    internal sealed class RuntimeDiagnostics : IDisposable
    {
        private DateTime nextPoll, nextNetworkSample, nextRangeSample, nextGameSample, stateSince, nextStallWarning;
        private string lastNetwork = "", lastState = "", lastRangeKey = "", lastGame = "";
        private string lastPollError = "", reportMessage = "";
        private object hookedClient;
        private EventInfo stateEvent;
        private Delegate stateHandler;
        private EventInfo operationEvent;
        private Delegate operationHandler;
        private DateTime lastEventAt;
        private readonly object failureSync = new object();
        private string pendingFailure = "", lastFailure = "none";
        private bool savingReport;
        internal string LatestNetwork { get; private set; }
        internal string LatestRange { get; private set; }
        internal string Checks { get; private set; }
        internal string ReportMessage { get { return reportMessage; } }
        internal string HudStatus = "Observer HUD disabled.";
        internal string TravelStatus = "Travel sharing not sampled yet.";

        internal RuntimeDiagnostics()
        {
            LatestNetwork = "Waiting for first network sample.";
            LatestRange = "No range sample yet.";
            Checks = "Checks have not run yet.";
        }

        private static string Value(object target, string name)
        {
            try { return Read.Text(Read.Get(target, name)); }
            catch (Exception e) { return "unavailable(" + e.GetBaseException().GetType().Name + ")"; }
        }

        internal void Start()
        {
            Log.Write("Session start; version=" + BuildInfo.Version + "; os=" + Environment.OSVersion +
                "; processBits=" + (IntPtr.Size * 8) + "; runtime=" + Environment.Version);
            Type application = Read.Type("UnityEngine.Application");
            Log.Write("Runtime; gameVersion=" + Value(application, "version") +
                "; unity=" + Value(application, "unityVersion"));
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name).Take(128))
                Log.Event("core", "INFO", "Assembly " + assembly.GetName().Name + " " + assembly.GetName().Version);
            RunChecks();
            Tick(true);
        }

        internal void SetVerbose(bool enabled)
        {
            Log.Verbose = enabled;
            Log.Write("Detailed diagnostics=" + enabled);
            nextRangeSample = DateTime.MinValue;
        }

        internal void RunChecks()
        {
            var result = new StringBuilder();
            int missing = 0;
            foreach (string name in new[] { "Photon.Pun.PhotonNetwork", "BattleDefinitions+AttackEvaluationParams",
                "ICursorService", "CursorLocationSelectTarget", "CharacterActionParams", "RulesetAttackMode" })
            {
                bool found = Read.Type(name) != null;
                if (!found) missing++;
                result.AppendLine((found ? "OK " : "MISSING ") + name);
            }
            Type evaluation = Read.Type("BattleDefinitions+AttackEvaluationParams");
            foreach (string method in new[] { "FillForMagicDistance", "FillForPhysicalRangeAttack", "FillForPhysicalReachAttack" })
            {
                int count = method == "FillForMagicDistance" ? 8 : 6;
                bool found = evaluation != null && evaluation.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Any(m => m.Name == method && m.GetParameters().Length == count);
                if (!found) missing++;
                result.AppendLine((found ? "OK " : "MISSING ") + method);
            }
            Type photon = Read.Type("Photon.Pun.PhotonNetwork");
            object settings = Read.Get(Read.Get(photon, "PhotonServerSettings"), "AppSettings");
            result.AppendLine(settings == null ? "WAIT Photon settings are not loaded yet." : "OK Photon settings loaded.");
            Checks = "API checks: missing=" + missing + "\n" + result;
            Log.Event("core", missing == 0 ? "INFO" : "WARN", Checks);
        }

        internal void Tick(bool force = false)
        {
            DateTime now = DateTime.UtcNow;
            if (!force && now < nextPoll) return;
            nextPoll = now.AddSeconds(1);
            try
            {
                Type photon = Read.Type("Photon.Pun.PhotonNetwork");
                object client = Read.Get(photon, "NetworkingClient");
                AttachStateEvent(client);
                string state = Value(client, "State");
                object peer = Read.Get(client, "LoadBalancingPeer");
                object settings = Read.Get(Read.Get(photon, "PhotonServerSettings"), "AppSettings");
                object room = Read.Get(client, "CurrentRoom");
                string summary = "state=" + state + "; facadeState=" + Value(photon, "NetworkClientState") +
                    "; offline=" + Value(photon, "OfflineMode") + "; peerState=" + Value(peer, "PeerState") +
                    "; endpoint=" + Endpoint(Read.Get(peer, "ServerAddress")) + "; connected=" + Value(photon, "IsConnected") +
                    "; ready=" + Value(photon, "IsConnectedAndReady") + "; inRoom=" + Value(photon, "InRoom") +
                    "; master=" + Value(photon, "IsMasterClient") + "; region=" + Value(photon, "CloudRegion") +
                    "; roomPlayers=" + Value(room, "PlayerCount") + "; maxPlayers=" + Value(room, "MaxPlayers") +
                    "; roomOpen=" + Value(room, "IsOpen") +
                    "; server=" + Value(photon, "Server") + "; actualProtocol=" + Value(peer, "TransportProtocol") +
                    "; nextProtocol=" + Value(settings, "Protocol") + "; fallback=" + Value(settings, "EnableProtocolFallback") +
                    "; disconnectCause=" + Value(client, "DisconnectedCause") +
                    "; disconnectTimeoutMs=" + Value(peer, "DisconnectTimeout");
                string statistics = "rttMs=" + Value(peer, "RoundTripTime") +
                    "; rttVarianceMs=" + Value(peer, "RoundTripTimeVariance") +
                    "; resentReliable=" + Value(peer, "ResentReliableCommands") +
                    "; queuedIncoming=" + Value(peer, "QueuedIncomingCommands") +
                    "; queuedOutgoing=" + Value(peer, "QueuedOutgoingCommands") +
                    "; bytesIn=" + Value(peer, "BytesIn") + "; bytesOut=" + Value(peer, "BytesOut") +
                    "; socketImplementation=" + Value(peer, "SocketImplementation");
                LatestNetwork = summary + "\n" + statistics;
                if (force || summary != lastNetwork || now >= nextNetworkSample)
                {
                    Log.Event("network", "INFO", summary + "; " + statistics);
                    lastNetwork = summary;
                    nextNetworkSample = now.AddSeconds(15);
                }
                if (state != lastState)
                {
                    lastState = state; stateSince = now; nextStallWarning = now.AddSeconds(20);
                }
                if ((state.StartsWith("Connecting", StringComparison.Ordinal) || state == "Joining" || state == "Authenticating") && now >= nextStallWarning)
                {
                    Log.Event("network", "WARN", "Connection remains in " + state + " for " +
                        (int)(now - stateSince).TotalSeconds + " seconds. This does not identify a VPN or routing cause.");
                    nextStallWarning = now.AddSeconds(30);
                }
                object battle = Read.Get(Read.Type("Gui"), "Battle");
                object characters = Read.Service("IGameLocationCharacterService");
                object cursor = Read.Get(Read.Service("ICursorService"), "CurrentCursor");
                string game = "battle=" + (battle != null) + "; round=" + Value(battle, "CurrentRound") +
                    "; partyCount=" + Read.Items(Read.Get(characters, "PartyCharacters")).Count() +
                    "; cursor=" + (cursor == null ? "none" : cursor.GetType().Name);
                if (force || game != lastGame || (Log.Verbose && now >= nextGameSample))
                {
                    Log.Write("Game context; " + game);
                    lastGame = game; nextGameSample = now.AddSeconds(30);
                }
                if (lastPollError.Length > 0) Log.Write("Diagnostic sampling recovered.");
                lastPollError = "";
            }
            catch (Exception e)
            {
                string key = e.GetBaseException().ToString();
                if (key != lastPollError) Log.Error("core", "Diagnostic sampling", e);
                lastPollError = key;
            }
            string failure;
            lock (failureSync)
            {
                failure = savingReport ? "" : pendingFailure;
                if (failure.Length > 0) pendingFailure = "";
            }
            if (failure.Length > 0)
            {
                Log.Event("network", "ERROR", "Automatic failure report requested: " + failure);
                SaveReport();
            }
        }

        private void AttachStateEvent(object client)
        {
            if (ReferenceEquals(client, hookedClient)) return;
            Dispose();
            if (client == null) return;
            // Remember the object even when its event is absent; retry only when the client changes.
            hookedClient = client;
            AttachOperationEvent(client);
            stateEvent = client.GetType().GetEvent("StateChanged");
            if (stateEvent == null)
            {
                Log.Event("network", "WARN", "StateChanged event unavailable; using one-second polling.");
                return;
            }
            ParameterInfo[] parameters = stateEvent.EventHandlerType.GetMethod("Invoke").GetParameters();
            if (parameters.Length != 2 || parameters[0].ParameterType != parameters[1].ParameterType ||
                !parameters[0].ParameterType.IsEnum)
            {
                Log.Event("network", "WARN", "Unsupported StateChanged signature; using polling.");
                return;
            }
            MethodInfo handler = GetType().GetMethod("StateChanged", BindingFlags.NonPublic | BindingFlags.Instance)
                .MakeGenericMethod(parameters[0].ParameterType);
            stateHandler = Delegate.CreateDelegate(stateEvent.EventHandlerType, this, handler);
            stateEvent.AddEventHandler(client, stateHandler);
            Log.Event("network", "INFO", "StateChanged event attached.");
        }

        private void StateChanged<T>(T previous, T current)
        {
            DateTime now = DateTime.UtcNow;
            double elapsed = lastEventAt == DateTime.MinValue ? 0 : (now - lastEventAt).TotalMilliseconds;
            lastEventAt = now;
            try
            {
                object peer = Read.Get(hookedClient, "LoadBalancingPeer");
                Log.Event("network", "INFO", "StateChanged " + previous + " -> " + current +
                    "; previousPhaseMs=" + elapsed.ToString("0", CultureInfo.InvariantCulture) +
                    "; server=" + Value(hookedClient, "Server") + "; endpoint=" + Endpoint(Read.Get(peer, "ServerAddress")) +
                    "; protocol=" + Value(peer, "TransportProtocol") + "; timeoutMs=" + Value(peer, "DisconnectTimeout") +
                    "; cause=" + Value(hookedClient, "DisconnectedCause") + "; bytesIn=" + Value(peer, "BytesIn") +
                    "; bytesOut=" + Value(peer, "BytesOut"));
                string cause = Value(hookedClient, "DisconnectedCause");
                if (current.ToString() == "Disconnected" && cause != "None" && cause != "?" &&
                    cause != "DisconnectByClientLogic" && cause != "ApplicationQuit")
                    RecordFailure("Disconnected; previous=" + previous + "; cause=" + cause +
                        "; endpoint=" + Endpoint(Read.Get(peer, "ServerAddress")) +
                        "; protocol=" + Value(peer, "TransportProtocol") +
                        "; timeoutMs=" + Value(peer, "DisconnectTimeout"));
            }
            catch (Exception e) { Log.Error("network", "StateChanged diagnostics", e); }
        }

        private static string Endpoint(object address)
        {
            string value = address as string;
            return value != null && System.Text.RegularExpressions.Regex.IsMatch(value, @"\A[a-zA-Z0-9.\-:\[\]]{1,128}\z") ? value : "?";
        }

        private void AttachOperationEvent(object client)
        {
            operationEvent = client.GetType().GetEvent("OpResponseReceived");
            if (operationEvent == null) { Log.Event("network", "WARN", "Photon operation-response event unavailable."); return; }
            ParameterInfo[] parameters = operationEvent.EventHandlerType.GetMethod("Invoke").GetParameters();
            if (parameters.Length != 1) { operationEvent = null; return; }
            MethodInfo method = GetType().GetMethod("OperationResponse", BindingFlags.NonPublic | BindingFlags.Instance)
                .MakeGenericMethod(parameters[0].ParameterType);
            operationHandler = Delegate.CreateDelegate(operationEvent.EventHandlerType, this, method);
            operationEvent.AddEventHandler(client, operationHandler);
            Log.Event("network", "INFO", "OpResponseReceived event attached; payloads and room names are not logged.");
        }

        private void OperationResponse<T>(T response)
        {
            try
            {
                object peer = Read.Get(hookedClient, "LoadBalancingPeer");
                Log.Event("network", "INFO", "OperationResponse; code=" + Value(response, "OperationCode") +
                    "; returnCode=" + Value(response, "ReturnCode") + "; clientState=" + Value(hookedClient, "State") +
                    "; endpoint=" + Endpoint(Read.Get(peer, "ServerAddress")) + "; protocol=" + Value(peer, "TransportProtocol"));
                object code = Read.Get(response, "ReturnCode");
                if (code != null && Convert.ToInt32(code, CultureInfo.InvariantCulture) != 0)
                    RecordFailure("Operation rejected; code=" + Value(response, "OperationCode") +
                        "; returnCode=" + Value(response, "ReturnCode") +
                        "; clientState=" + Value(hookedClient, "State"));
            }
            catch (Exception e) { Log.Error("network", "Operation-response diagnostics", e); }
        }

        private void RecordFailure(string reason)
        {
            lock (failureSync)
            {
                lastFailure = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "; " + reason;
                pendingFailure = reason;
            }
            Log.Event("network", "ERROR", reason);
        }

        internal void ObserveRange(RangeSnapshot range, string reason)
        {
            DateTime now = DateTime.UtcNow;
            string text = range == null ? "hidden; reason=" + reason :
                "distance=" + range.Distance.ToString("0.###", CultureInfo.InvariantCulture) +
                "; maximumRaw=" + range.Maximum.ToString("0.###", CultureInfo.InvariantCulture) +
                "; maximumDisplayed=" + Math.Round(range.Maximum).ToString("0", CultureInfo.InvariantCulture) +
                "; within=" + range.WithinRange + "; visible=" + (range.Visible.HasValue ? range.Visible.ToString() : "unknown") +
                "; textColor=" + (range.HighlightGreen ? "green" : "red") +
                "; " + range.DiagnosticDetails;
            LatestRange = text;
            string key = reason + (range == null ? "" : ";within=" + range.WithinRange + ";visible=" + range.Visible);
            if (now < nextRangeSample || (!Log.Verbose && key == lastRangeKey)) return;
            Log.Event("range", "INFO", text);
            lastRangeKey = key; nextRangeSample = now.AddSeconds(1);
        }

        internal string SaveReport()
        {
            if (savingReport) return null;
            savingReport = true;
            try
            {
                Tick(true);
                RunChecks();
                Directory.CreateDirectory(Log.DirectoryPath);
                string path = Path.Combine(Log.DirectoryPath, "diagnostics-" + BuildInfo.Version + "-" +
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" +
                    Guid.NewGuid().ToString("N").Substring(0, 6) + ".txt");
                var text = new StringBuilder();
                text.AppendLine("Solasta Coop View " + BuildInfo.Version + " diagnostic report");
                text.AppendLine("UTC: " + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                text.AppendLine("Session: " + Log.Writer.Session);
                text.AppendLine("Verbose: " + Log.Verbose);
                text.AppendLine("Network connectivity is observed only. No automatic reconnect or network probe was run.");
                text.AppendLine("No automated visual or multiplayer desynchronisation test was performed.");
                text.AppendLine("Log write failures: " + Log.Writer.FailedWrites + "; last=" + Log.Writer.LastError);
                text.AppendLine("Suppressed Unity console messages: " + Log.SuppressedUnityMessages);
                text.AppendLine("\nCURRENT NETWORK\n" + LatestNetwork);
                lock (failureSync) text.AppendLine("\nLAST NETWORK FAILURE\n" + lastFailure);
                text.AppendLine("\nSESSION ERRORS (memory, retained after log deletion)\n" + Log.Writer.ReadFailures());
                text.AppendLine("\nCONNECTION RETRIES\n" + ConnectionRetry.Enabled + "; " + ConnectionRetry.Status);
                text.AppendLine("\nTRAVEL VIEW\n" + TravelStatus);
                text.AppendLine("\nCURRENT RANGE\n" + LatestRange);
                text.AppendLine("\nOBSERVER HUD\n" + HudStatus);
                text.AppendLine("\nDETAILED TOOLTIPS\n" + DetailedTooltips.DiagnosticSummary);
                text.AppendLine("\nCAMERA\n" + CameraZoom.Status);
                text.AppendLine("\nAPI CHECKS\n" + Checks);
                foreach (string category in DiagnosticWriter.Categories)
                    text.AppendLine("\nRECENT " + category.ToUpperInvariant() + "\n" + Log.Writer.ReadTail(category));
                File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
                foreach (FileInfo old in new DirectoryInfo(Log.DirectoryPath)
                    .GetFiles("diagnostics-" + BuildInfo.Version + "-*.txt")
                    .Where(f => f.FullName != path)
                    .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name).Skip(4)) old.Delete();
                reportMessage = "Отчёт сохранён: " + path;
                Log.Write("Diagnostic report saved: " + Path.GetFileName(path));
                return path;
            }
            catch (Exception e)
            {
                reportMessage = "Не удалось сохранить отчёт: " + e.GetBaseException().Message;
                Log.Error("core", "Save diagnostic report", e);
                return null;
            }
            finally { savingReport = false; }
        }

        public void Dispose()
        {
            if (hookedClient != null && operationEvent != null && operationHandler != null)
            {
                try { operationEvent.RemoveEventHandler(hookedClient, operationHandler); }
                catch (Exception e) { Log.Error("network", "Detach operation-response callback", e); }
            }
            if (hookedClient != null && stateEvent != null && stateHandler != null)
            {
                try { stateEvent.RemoveEventHandler(hookedClient, stateHandler); }
                catch (Exception e) { Log.Error("network", "Detach state callback", e); }
            }
            hookedClient = null; stateEvent = null; stateHandler = null;
            operationEvent = null; operationHandler = null; lastEventAt = DateTime.MinValue;
        }
    }
}
