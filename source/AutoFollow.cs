using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SolastaCoop
{
    // Uses native ICommandService.MoveCharacter for locally controlled characters only.
    // Does not teleport, modify pathfinding, or change ownership/network state.
    internal sealed class PartyAutoFollow
    {
        private const float TickInterval = 0.3f;
        private const float CommandCooldown = 2.5f;
        private const float DesiredDistance = 1.5f;
        private const float ResumeDistance = 1.9f;
        private const float RecentMovementSeconds = 4.0f;
        private const float MovementEpsilon = 0.65f;
        private const float WaypointStartGrace = 2f;

        private sealed class IssuedMove { internal int X, Y, Z; }

        private sealed class Track
        {
            internal int X, Y, Z;
            internal float LastMoveTime;
            internal bool Initialized;
        }

        private readonly Dictionary<ulong, Track> observations = new Dictionary<ulong, Track>();
        private readonly Dictionary<ulong, float> lastCommand = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, IssuedMove> autoMoves = new Dictionary<ulong, IssuedMove>();
        private object location;
        private bool wasExploring;
        private ulong leaderGuid;
        private bool manualLeader;
        private float nextTick;
        private float nextDiagnostic;
        private ulong hostMoveLeader;
        private int hostMoveX, hostMoveY, hostMoveZ;
        private float lastHostMoveAt;
        private readonly Dictionary<ulong, int> lastHostMoveCommand = new Dictionary<ulong, int>();
        private int hostMoveVersion;
        private bool hostWaypointActive;
        private int issuedCommands;
        private int skippedBusy;
        private int retargeted, recoveries;
        private string lastError;
        private float nextErrorLog;
        private string lastSample = "No exploration sample yet.";
        internal bool Enabled { get; private set; }
        internal string DiagnosticSummary { get { return Status + "\nenabled=" + Enabled + "; leader=" + leaderGuid +
            "; manualLeader=" + manualLeader + "; hostWaypoint=" + hostWaypointActive + "; waypointVersion=" + hostMoveVersion +
            "; issued=" + issuedCommands + "; retargeted=" + retargeted + "; busySkipped=" + skippedBusy +
            "; recoveries=" + recoveries + "; lastError=" + lastError + "\n" + lastSample; } }
        internal static bool IsIssuingMove { get; private set; }
        // Only host-issued movement commands create a new destination. A stale
        // destination is discarded when the exploration mode changes.
        internal void OnHostMove(ulong leader, int x, int y, int z)
        {
            if (!Enabled || leader == 0UL) return;
            if (manualLeader && leaderGuid != leader) return;
            object currentLocation = Read.Get(Read.Type("Gui"), "GameLocation");
            if (currentLocation == null || Read.Get(Read.Type("Gui"), "Battle") != null) return;
            if (!object.ReferenceEquals(location, currentLocation))
            {
                ResetMovement();
                location = currentLocation;
                wasExploring = false;
            }
            hostMoveLeader = leader;
            hostMoveX = x; hostMoveY = y; hostMoveZ = z;
            lastHostMoveAt = Time.unscaledTime;
            hostMoveVersion++;
            hostWaypointActive = true;
            if (!manualLeader) leaderGuid = leader;
            Status = "Host selected destination (" + x + ", " + y + ", " + z + "). Following host command.";
        }
        internal string Status { get; private set; } = "Disabled.";

        private void ResetMovement()
        {
            lastCommand.Clear();
            autoMoves.Clear();
            lastHostMoveCommand.Clear();
            hostWaypointActive = false;
            observations.Clear();
        }

        private void Pause(string reason)
        {
            if (wasExploring || hostWaypointActive) ResetMovement();
            wasExploring = false;
            Status = reason;
        }

        internal void OnManualMove(object character)
        {
            if (!Enabled || IsIssuingMove || LocallyControlled(character) != true) return;
            ulong id = Id(character);
            autoMoves.Remove(id);
            lastCommand[id] = Time.unscaledTime;
        }

        private static ulong Id(object character)
        {
            object id = Read.Get(character, "Guid") ?? Read.Get(Read.Get(character, "RulesetCharacter"), "Guid");
            return id is ulong ? (ulong)id : 0UL;
        }

        private static bool? LocallyControlled(object character)
        {
            object ruleset = Read.Get(character, "RulesetCharacter");
            object players = Read.Service("IPlayerControllerService");
            object network = Read.Service("INetworkingService");
            if (ruleset == null || players == null || network == null) return null;
            object playerId = Read.Call(players, "GetPlayerControllingHero", ruleset);
            if (!(playerId is int)) return null;
            object controller = Read.Call(players, "GetPlayerController", playerId);
            if (controller == null) return null;
            object result = Read.Call(network, "IsLocalPlayerController", controller);
            return result is bool ? (bool?)(bool)result : null;
        }

        private static int Coordinate(object position, string name)
        {
            object value = Read.Get(position, name);
            return value is int ? (int)value : 0;
        }

        private static object GridPosition(int x, int y, int z)
        {
            Type grid = Read.Type("TA.int3");
            if (grid == null) throw new MissingMemberException("TA.int3");
            ConstructorInfo constructor = grid.GetConstructor(new[] { typeof(int), typeof(int), typeof(int) });
            if (constructor != null) return constructor.Invoke(new object[] { x, y, z });
            object position = Activator.CreateInstance(grid);
            ObserverHud.SetMember(position, "x", x);
            ObserverHud.SetMember(position, "y", y);
            ObserverHud.SetMember(position, "z", z);
            return position;
        }

        private static double Distance(int x1, int z1, int x2, int z2)
        {
            double dx = x1 - x2, dz = z1 - z2;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        internal void SelectTarget(object character)
        {
            if (character == null) return;
            ulong id = Id(character);
            if (id == 0 || LocallyControlled(character) != false) return;
            leaderGuid = id;
            manualLeader = true;
            if (Enabled) Status = "Selected leader: " + Read.Name(character);
        }

        internal void SetEnabled(bool enabled, object selected = null)
        {
            if (!enabled)
            {
                Enabled = false;
                ResetMovement();
                wasExploring = false;
                Status = "Auto-follow disabled.";
                Log.Event("core", "INFO", Status);
                return;
            }
            if (selected != null) SelectTarget(selected);
            Enabled = true;
            nextTick = 0;
            ResetMovement();
            wasExploring = false;
            issuedCommands = 0;
            skippedBusy = 0;
            retargeted = recoveries = 0;
            lastError = null;
            Status = "Looking for a moving teammate hero.";
            Log.Event("core", "INFO", "Auto-follow enabled.");
        }

        internal void SelectAutomaticLeader()
        {
            manualLeader = false;
            leaderGuid = 0UL;
            hostWaypointActive = false;
            if (Enabled) Status = "Leader is selected automatically based on movement.";
        }

        private bool UseHostWaypoint(List<object> remotes, out object leader)
        {
            leader = hostWaypointActive ? remotes.FirstOrDefault(p => Id(p) == hostMoveLeader) : null;
            if (!hostWaypointActive) return false;
            float age = Time.unscaledTime - lastHostMoveAt;
            Track observation;
            bool moving = leader != null && (object.Equals(Read.Get(leader, "MovingToDestination"), true) ||
                object.Equals(Read.Get(leader, "MoveInProgress"), true) ||
                (observations.TryGetValue(Id(leader), out observation) && observation.LastMoveTime > 0f &&
                    Time.unscaledTime - observation.LastMoveTime < WaypointStartGrace));
            if (leader == null || age >= 75f || (age > WaypointStartGrace && !moving))
            {
                hostWaypointActive = false;
                return false;
            }
            return true;
        }

        private bool CanRetarget(object actor, object actionService)
        {
            IssuedMove previous;
            if (!autoMoves.TryGetValue(Id(actor), out previous)) return false;
            Type actionId = Read.Type("ActionDefinitions+Id");
            if (actionId == null || !Enum.IsDefined(actionId, "ExplorationMove") ||
                !object.Equals(Read.Call(actionService, "IsCharacterExecutingAction", actor,
                    Enum.Parse(actionId, "ExplorationMove")), true)) return false;
            object destination = Read.Get(actor, "DestinationPosition");
            return destination != null && Coordinate(destination, "x") == previous.X &&
                Coordinate(destination, "y") == previous.Y && Coordinate(destination, "z") == previous.Z;
        }

        private void ObserveRemoteCharacters(IEnumerable<object> remotes)
        {
            foreach (object character in remotes)
            {
                ulong id = Id(character);
                object position = Read.Get(character, "LocationPosition");
                if (id == 0 || position == null) continue;
                int x = Coordinate(position, "x"), y = Coordinate(position, "y"), z = Coordinate(position, "z");
                Track observation;
                if (!observations.TryGetValue(id, out observation))
                {
                    observation = new Track();
                    observations[id] = observation;
                }
                if (observation.Initialized && Distance(x, z, observation.X, observation.Z) >= MovementEpsilon)
                    observation.LastMoveTime = Time.unscaledTime;
                observation.X = x;
                observation.Y = y;
                observation.Z = z;
                observation.Initialized = true;
            }
        }

        private object ChooseLeader(List<object> remotes, List<object> locals)
        {
            object chosen = remotes.FirstOrDefault(p => Id(p) == leaderGuid);
            if (manualLeader && chosen != null) return chosen;
            if (manualLeader && chosen == null) manualLeader = false;

            // Keep the present leader while moving. Switch only if another teammate
            // is moving and our current leader has stopped.
            float now = Time.unscaledTime;
            Track current;
            bool moving = chosen != null && observations.TryGetValue(Id(chosen), out current) &&
                now - current.LastMoveTime <= RecentMovementSeconds && current.LastMoveTime > 0f;
            if (moving) return chosen;

            object recentlyMoving = remotes
                .Where(p => observations.ContainsKey(Id(p)) && observations[Id(p)].LastMoveTime > 0f &&
                    now - observations[Id(p)].LastMoveTime <= RecentMovementSeconds)
                .OrderByDescending(p => observations[Id(p)].LastMoveTime)
                .FirstOrDefault();
            if (recentlyMoving != null) return recentlyMoving;
            if (chosen != null) return chosen;

            // No movement observed yet, pick the teammate nearest to our heroes.
            return remotes.OrderBy(p =>
            {
                object position = Read.Get(p, "LocationPosition");
                if (position == null) return double.MaxValue;
                int x = Coordinate(position, "x"), z = Coordinate(position, "z");
                return locals.Select(c => Read.Get(c, "LocationPosition"))
                    .Where(pos => pos != null)
                    .Select(pos => Distance(x, z, Coordinate(pos, "x"), Coordinate(pos, "z")))
                    .DefaultIfEmpty(double.MaxValue).Min();
            }).FirstOrDefault();
        }

        internal void Tick()
        {
            if (!Enabled || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + TickInterval;
            try
            {
                object currentLocation = Read.Get(Read.Type("Gui"), "GameLocation");
                if (!object.ReferenceEquals(location, currentLocation))
                {
                    location = currentLocation;
                    ResetMovement();
                    wasExploring = false;
                }
                if (currentLocation == null ||
                    Read.Get(Read.Type("Gui"), "Battle") != null)
                {
                    Pause("Paused: not exploring or combat started.");
                    return;
                }
                object cursor = Read.Get(Read.Service("ICursorService"), "CurrentCursor");
                if (cursor == null || !cursor.GetType().Name.StartsWith("CursorLocationExploration", StringComparison.Ordinal))
                {
                    Pause("Paused: dialogue, menu, cutscene, or loading.");
                    return;
                }
                wasExploring = true;
                var party = Read.Items(Read.Get(Read.Service("IGameLocationCharacterService"), "PartyCharacters")).ToList();
                var classified = party.Select(p => new { Character = p, Local = LocallyControlled(p) }).ToList();
                if (classified.Count < 2 || classified.Any(c => !c.Local.HasValue))
                {
                    Status = "No command available or character ownership could not be verified safely.";
                    return;
                }
                var locals = classified.Where(c => c.Local == true).Select(c => c.Character).ToList();
                var remotes = classified.Where(c => c.Local == false).Select(c => c.Character).ToList();
                if (locals.Count == 0 || remotes.Count == 0)
                {
                    Status = "Both a local hero and a teammate hero are required.";
                    return;
                }
                ObserveRemoteCharacters(remotes);
                // When the host provides destinations we do not continuously
                // chase his current position: we act on the command itself.
                object leader;
                bool followHostCommand = UseHostWaypoint(remotes, out leader);
                if (!followHostCommand) leader = ChooseLeader(remotes, locals);
                if (leader == null) return;
                leaderGuid = Id(leader);
                object leaderPosition = Read.Get(leader, "LocationPosition");
                if (leaderPosition == null) return;
                int lx = followHostCommand ? hostMoveX : Coordinate(leaderPosition, "x");
                int ly = followHostCommand ? hostMoveY : Coordinate(leaderPosition, "y");
                int lz = followHostCommand ? hostMoveZ : Coordinate(leaderPosition, "z");
                object commands = Read.Service("ICommandService");
                object actionService = Read.Service("IGameLocationActionService");
                if (commands == null || actionService == null)
                {
                    Status = "Solasta command or action service is not available yet.";
                    return;
                }
                MethodInfo move = commands.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "MoveCharacter" && m.GetParameters().Length == 8);
                if (move == null) throw new MissingMethodException("ICommandService.MoveCharacter(8)");

                int sent = 0, busy = 0, near = 0, index = 0;
                var samples = new List<string>();
                foreach (object actor in locals)
                {
                    ulong id = Id(actor);
                    object position = Read.Get(actor, "LocationPosition");
                    if (id == 0 || position == null) continue;
                    int fx = Coordinate(position, "x"), fy = Coordinate(position, "y"), fz = Coordinate(position, "z");
                    double distance = Distance(fx, fz, lx, lz);
                    samples.Add(id + "@" + fx + "," + fy + "," + fz);
                    // Do not freeze when the leader changes elevation on stairs or ramps.
                    // The game's pathfinding decides whether the target is reachable.
                    if (distance < ResumeDistance && fy == ly)
                    {
                        near++;
                        continue;
                    }
                    float last;
                    int handledVersion;
                    bool firstCommandForWaypoint = followHostCommand &&
                        (!lastHostMoveCommand.TryGetValue(id, out handledVersion) || handledVersion != hostMoveVersion);
                    // Bound commands while the previous request may still be awaiting its network echo.
                    if (lastCommand.TryGetValue(id, out last) &&
                        Time.unscaledTime - last < CommandCooldown) continue;
                    if (followHostCommand && !firstCommandForWaypoint &&
                        lastCommand.TryGetValue(id, out last) && Time.unscaledTime - last < 7f) continue;
                    bool acting = object.Equals(Read.Call(actionService, "IsCharacterActing", actor), true);
                    bool retarget = acting && CanRetarget(actor, actionService);
                    if (acting && !retarget)
                    {
                        busy++;
                        skippedBusy++;
                        continue;
                    }
                    // The native MoveCharacter command replaces a walk when enqueue=false. Waiting for
                    // IsCharacterActing to become false prevents following a new destination during long walks.
                    if (retarget && lastCommand.TryGetValue(id, out last) && Time.unscaledTime - last < CommandCooldown) continue;
                    // Aim only one cell behind the leader. The previous 2-3 cell offset
                    // combined with a 3.25-cell stop threshold made units halt too early.
                    double dx = fx - lx, dz = fz - lz;
                    double horizontal = Math.Max(1d, distance);
                    double offset = DesiredDistance;
                    int tx = lx + (int)Math.Round(dx / horizontal * offset);
                    int tz = lz + (int)Math.Round(dz / horizontal * offset);
                    // A second follower takes a slightly different adjacent cell.
                    if (index++ % 2 == 1)
                    {
                        tx += (int)Math.Round(-dz / horizontal);
                        tz += (int)Math.Round(dx / horizontal);
                    }
                    if (tx == fx && tz == fz && ly == fy) { near++; continue; }
                    IssuedMove previous;
                    if (retarget && autoMoves.TryGetValue(id, out previous) &&
                        previous.X == tx && previous.Y == ly && previous.Z == tz) { busy++; continue; }
                    object orientation = Read.Get(actor, "Orientation");
                    object stance = Read.Call(actor, "GetMoveStance");
                    if (orientation == null || stance == null) continue;
                    try
                    {
                        IsIssuingMove = true;
                        move.Invoke(commands, new object[] { actor, GridPosition(tx, ly, tz), orientation, 0f,
                            stance, null, false, true });
                    }
                    finally { IsIssuingMove = false; }
                    lastCommand[id] = Time.unscaledTime;
                    autoMoves[id] = new IssuedMove { X = tx, Y = ly, Z = tz };
                    if (followHostCommand) lastHostMoveCommand[id] = hostMoveVersion;
                    issuedCommands++;
                    if (retarget) retargeted++;
                    sent++;
                }
                lastSample = "leader@" + lx + "," + ly + "," + lz + "; locals=" + string.Join(";", samples);
                Status = (followHostCommand ? "Moving toward host waypoint behind " : "Following ") + Read.Name(leader) + (manualLeader ? " (manual)" : " (auto)") +
                    ". Commands: " + issuedCommands + ", issued now: " + sent +
                    ", already near: " + near + ", busy: " + busy + ".";
            }
            catch (Exception ex)
            {
                ResetMovement();
                nextTick = Time.unscaledTime + CommandCooldown;
                string error = ex.GetBaseException().Message;
                Status = "Auto-follow paused; retrying: " + error;
                recoveries++;
                if (error != lastError || Time.unscaledTime >= nextErrorLog)
                {
                    nextErrorLog = Time.unscaledTime + 15f;
                    Log.Error("core", "Party auto-follow (will retry)", ex);
                }
                lastError = error;
            }
            finally
            {
                if (Log.Verbose && Time.unscaledTime >= nextDiagnostic)
                {
                    nextDiagnostic = Time.unscaledTime + 10f;
                    Log.Event("core", "DEBUG", "Auto-follow; " + DiagnosticSummary);
                }
            }
        }
    }
}
