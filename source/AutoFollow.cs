using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SolastaCoop
{
    // Moves locally controlled heroes by using Solasta's multiplayer command service.
    // Never moves a remote hero, never teleports and never issues commands in combat.
    internal sealed class PartyAutoFollow
    {
        private const float DesiredDistance = 2.5f;
        private const float MoveInterval = 3.0f;
        private const float TickInterval = 0.65f;
        private readonly Dictionary<ulong, float> lastMove = new Dictionary<ulong, float>();
        private ulong leaderGuid;
        private float nextTick;
        private bool warned;
        internal bool Enabled { get; private set; }
        internal string Status { get; private set; } = "Выключено.";

        internal void SelectTarget(object character)
        {
            ulong guid = Id(character);
            if (guid == 0) return;
            leaderGuid = guid;
            if (Enabled) Status = "Выбран ведущий: " + Read.Name(character);
        }

        private static ulong Id(object character)
        {
            object value = Read.Get(character, "Guid") ?? Read.Get(Read.Get(character, "RulesetCharacter"), "Guid");
            return value is ulong ? (ulong)value : 0;
        }

        private static bool? LocallyControlled(object character)
        {
            object ruleset = Read.Get(character, "RulesetCharacter");
            object players = Read.Service("IPlayerControllerService");
            object network = Read.Service("INetworkingService");
            if (ruleset == null || players == null || network == null) return null;
            object id = Read.Call(players, "GetPlayerControllingHero", ruleset);
            if (!(id is int)) return null;
            object controller = Read.Call(players, "GetPlayerController", id);
            if (controller == null) return null;
            object result = Read.Call(network, "IsLocalPlayerController", controller);
            return result is bool ? (bool?)((bool)result) : null;
        }

        internal void SetEnabled(bool enabled, object selected = null)
        {
            if (!enabled)
            {
                Enabled = false;
                lastMove.Clear();
                Status = "Автоходьба выключена.";
                return;
            }
            if (selected != null) SelectTarget(selected);
            Enabled = true;
            warned = false;
            nextTick = 0f;
            Status = "Автоходьба включена: ищу персонажа другого игрока.";
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
            // int3 is a struct. Prefer its native constructor, fall back to public fields.
            var constructor = grid.GetConstructor(new[] { typeof(int), typeof(int), typeof(int) });
            if (constructor != null) return constructor.Invoke(new object[] { x, y, z });
            object position = Activator.CreateInstance(grid);
            ObserverHud.SetMember(position, "x", x);
            ObserverHud.SetMember(position, "y", y);
            ObserverHud.SetMember(position, "z", z);
            return position;
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
                    Status = "Приостановлено вне исследования или во время боя.";
                    return;
                }
                object cursor = Read.Get(Read.Service("ICursorService"), "CurrentCursor");
                if (cursor == null || !cursor.GetType().Name.Contains("Exploration"))
                {
                    Status = "Приостановлено: идёт диалог, загрузка или другой режим.";
                    return;
                }
                var party = Read.Items(Read.Get(Read.Service("IGameLocationCharacterService"),
                    "PartyCharacters")).ToList();
                if (party.Count < 2) { Status = "Нужны два или больше персонажей."; return; }
                var owners = party.Select(p => new { Character = p, Local = LocallyControlled(p) }).ToList();
                if (owners.Any(p => !p.Local.HasValue))
                {
                    Status = "Не удалось определить принадлежность героев. Движение заблокировано.";
                    return;
                }
                var leader = owners.FirstOrDefault(p => Id(p.Character) == leaderGuid && p.Local == false);
                if (leader == null) leader = owners.FirstOrDefault(p => p.Local == false);
                if (leader == null)
                {
                    Status = "Не найден персонаж тиммейта, за которым следовать.";
                    return;
                }
                leaderGuid = Id(leader.Character);
                object destination = Read.Get(leader.Character, "LocationPosition");
                if (destination == null) return;
                int lx = Coordinate(destination, "x"), ly = Coordinate(destination, "y"), lz = Coordinate(destination, "z");
                object command = Read.Service("ICommandService");
                object actions = Read.Service("IGameLocationActionService");
                if (command == null || actions == null)
                {
                    Status = "Службы команд игры недоступны. Движение не выполняется.";
                    return;
                }
                int ordered = 0;
                foreach (var follower in owners.Where(p => p.Local == true))
                {
                    object actor = follower.Character;
                    ulong id = Id(actor);
                    object current = Read.Get(actor, "LocationPosition");
                    if (current == null || id == 0) continue;
                    int fx = Coordinate(current, "x"), fy = Coordinate(current, "y"), fz = Coordinate(current, "z");
                    double dx = fx - lx, dz = fz - lz;
                    double distance = Math.Sqrt(dx * dx + dz * dz);
                    if (distance <= DesiredDistance + 0.75f || Math.Abs(fy - ly) > 2) continue;
                    float last;
                    if (lastMove.TryGetValue(id, out last) && Time.unscaledTime - last < MoveInterval) continue;
                    if (object.Equals(Read.Call(actions, "IsCharacterActing", actor), true)) continue;
                    int offset = ordered++ % 2 == 0 ? 2 : 3;
                    int destX = lx + (int)Math.Round(dx / distance * offset);
                    int destZ = lz + (int)Math.Round(dz / distance * offset);
                    object grid = GridPosition(destX, ly, destZ);
                    object orientation = Read.Get(actor, "Orientation");
                    object stance = Read.Call(actor, "GetMoveStance");
                    if (orientation == null || stance == null) continue;
                    // ICommandService sends Solasta's ordinary authoritative movement command.
                    // No custom Photon movement RPCs, no position assignments.
                    MethodInfo requestMove = command.GetType().GetMethods(BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "MoveCharacter" && m.GetParameters().Length == 8);
                    if (requestMove == null) throw new MissingMethodException("ICommandService.MoveCharacter(8)");
                    requestMove.Invoke(command, new object[] { actor, grid, orientation, 0f,
                        stance, null, false, true });
                    lastMove[id] = Time.unscaledTime;
                    if (!warned)
                    {
                        Log.Write("Party auto-follow movement requested through ICommandService.MoveCharacter");
                        warned = true;
                    }
                }
                Status = "Мои персонажи следуют за " + Read.Name(leader.Character) + ". Ходьба, не камера.";
            }
            catch (Exception ex)
            {
                Enabled = false;
                lastMove.Clear();
                Status = "Автоходьба остановлена после ошибки: " + ex.GetBaseException().Message;
                Log.Error("core", "Party auto-follow command", ex);
            }
        }
    }
}
