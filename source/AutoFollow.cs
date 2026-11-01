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
        private const float CommandCooldown = 1.0f;
        private const float DesiredDistance = 1.5f;
        private const float ResumeDistance = 1.9f;
        private const float RecentMovementSeconds = 4.0f;
        private const float MovementEpsilon = 0.65f;

        private sealed class Track
        {
            internal int X, Y, Z;
            internal float LastMoveTime;
            internal bool Initialized;
        }

        private readonly Dictionary<ulong, Track> observations = new Dictionary<ulong, Track>();
        private readonly Dictionary<ulong, float> lastCommand = new Dictionary<ulong, float>();
        private ulong leaderGuid;
        private bool manualLeader;
        private float nextTick;
        private float nextDiagnostic;
        private int issuedCommands;
        private int skippedBusy;
        private string lastError;
        internal bool Enabled { get; private set; }
        internal string Status { get; private set; } = "Выключено.";

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
            if (Enabled) Status = "Выбран ведущий: " + Read.Name(character);
        }

        internal void SetEnabled(bool enabled, object selected = null)
        {
            if (!enabled)
            {
                Enabled = false;
                lastCommand.Clear();
                observations.Clear();
                Status = "Автоходьба выключена.";
                return;
            }
            if (selected != null) SelectTarget(selected);
            Enabled = true;
            nextTick = 0;
            lastCommand.Clear();
            observations.Clear();
            issuedCommands = 0;
            skippedBusy = 0;
            lastError = null;
            Status = "Ищу движущегося персонажа тиммейта.";
        }

        internal void SelectAutomaticLeader()
        {
            manualLeader = false;
            leaderGuid = 0UL;
            if (Enabled) Status = "Ведущий выбирается автоматически по движению.";
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
                if (Read.Get(Read.Type("Gui"), "GameLocation") == null ||
                    Read.Get(Read.Type("Gui"), "Battle") != null)
                {
                    Status = "Приостановлено: не исследование или начался бой.";
                    return;
                }
                object cursor = Read.Get(Read.Service("ICursorService"), "CurrentCursor");
                if (cursor == null || !cursor.GetType().Name.StartsWith("CursorLocationExploration", StringComparison.Ordinal))
                {
                    Status = "Приостановлено: диалог, меню, катсцена или загрузка.";
                    return;
                }
                var party = Read.Items(Read.Get(Read.Service("IGameLocationCharacterService"), "PartyCharacters")).ToList();
                var classified = party.Select(p => new { Character = p, Local = LocallyControlled(p) }).ToList();
                if (classified.Count < 2 || classified.Any(c => !c.Local.HasValue))
                {
                    Status = "Нет команды или не удалось безопасно определить владельцев персонажей.";
                    return;
                }
                var locals = classified.Where(c => c.Local == true).Select(c => c.Character).ToList();
                var remotes = classified.Where(c => c.Local == false).Select(c => c.Character).ToList();
                if (locals.Count == 0 || remotes.Count == 0)
                {
                    Status = "Нужны локальный герой и герой тиммейта.";
                    return;
                }
                ObserveRemoteCharacters(remotes);
                object leader = ChooseLeader(remotes, locals);
                if (leader == null) return;
                leaderGuid = Id(leader);
                object leaderPosition = Read.Get(leader, "LocationPosition");
                if (leaderPosition == null) return;
                int lx = Coordinate(leaderPosition, "x"), ly = Coordinate(leaderPosition, "y"), lz = Coordinate(leaderPosition, "z");
                object commands = Read.Service("ICommandService");
                object actionService = Read.Service("IGameLocationActionService");
                if (commands == null || actionService == null)
                {
                    Status = "Служба команд или действий Solasta ещё недоступна.";
                    return;
                }
                MethodInfo move = commands.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "MoveCharacter" && m.GetParameters().Length == 8);
                if (move == null) throw new MissingMethodException("ICommandService.MoveCharacter(8)");

                int sent = 0, busy = 0, near = 0, index = 0;
                foreach (object actor in locals)
                {
                    ulong id = Id(actor);
                    object position = Read.Get(actor, "LocationPosition");
                    if (id == 0 || position == null) continue;
                    int fx = Coordinate(position, "x"), fy = Coordinate(position, "y"), fz = Coordinate(position, "z");
                    double distance = Distance(fx, fz, lx, lz);
                    // Do not freeze when the leader changes elevation on stairs or ramps.
                    // The game's pathfinding decides whether the target is reachable.
                    if (distance < ResumeDistance)
                    {
                        near++;
                        continue;
                    }
                    float last;
                    if (lastCommand.TryGetValue(id, out last) && Time.unscaledTime - last < CommandCooldown) continue;
                    if (object.Equals(Read.Call(actionService, "IsCharacterActing", actor), true))
                    {
                        busy++;
                        skippedBusy++;
                        continue;
                    }
                    // Aim only one cell behind the leader. The previous 2-3 cell offset
                    // combined with a 3.25-cell stop threshold made units halt too early.
                    double dx = fx - lx, dz = fz - lz;
                    double offset = DesiredDistance;
                    int tx = lx + (int)Math.Round(dx / distance * offset);
                    int tz = lz + (int)Math.Round(dz / distance * offset);
                    // A second follower takes a slightly different adjacent cell.
                    if (index++ % 2 == 1)
                    {
                        tx += (int)Math.Round(-dz / distance);
                        tz += (int)Math.Round(dx / distance);
                    }
                    if (tx == fx && tz == fz) { near++; continue; }
                    object orientation = Read.Get(actor, "Orientation");
                    object stance = Read.Call(actor, "GetMoveStance");
                    if (orientation == null || stance == null) continue;
                    move.Invoke(commands, new object[] { actor, GridPosition(tx, ly, tz), orientation, 0f,
                        stance, null, false, true });
                    lastCommand[id] = Time.unscaledTime;
                    issuedCommands++;
                    sent++;
                }
                Status = "Следую за " + Read.Name(leader) + (manualLeader ? " (выбран)" : " (авто)") +
                    ". Команд: " + issuedCommands + ", отправлено сейчас: " + sent +
                    ", уже рядом: " + near + ", заняты: " + busy + ".";
                if (Log.Verbose && sent > 0 && Time.unscaledTime > nextDiagnostic)
                {
                    nextDiagnostic = Time.unscaledTime + 5f;
                    Log.Event("core", "DEBUG", "Auto-follow; leader=" + leaderGuid +
                        "; sent=" + sent + "; near=" + near + "; busy=" + busy +
                        "; busySkippedTotal=" + skippedBusy);
                }
            }
            catch (Exception ex)
            {
                Enabled = false;
                lastCommand.Clear();
                lastError = ex.GetBaseException().Message;
                Status = "Автоходьба остановлена: " + lastError;
                Log.Error("core", "Party auto-follow", ex);
            }
        }
    }
}
