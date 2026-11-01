using System;
using System.Linq;
using UnityEngine;

namespace SolastaCoop
{
    // UI-only camera follow. Does not issue movement/actions or Photon commands.
    internal sealed class CameraAutoFollow
    {
        private ulong targetGuid;
        private float nextCheck;
        private bool applied;
        internal bool Enabled { get; private set; }
        internal string Status { get; private set; } = "Выключено.";

        internal void SelectTarget(object character)
        {
            object guid = Read.Get(Read.Get(character, "RulesetCharacter"), "Guid");
            if (!(guid is ulong) || (ulong)guid == 0)
            {
                if (Enabled) Status = "У выбранного персонажа нет корректного GUID.";
                return;
            }
            targetGuid = (ulong)guid;
            applied = false;
            nextCheck = 0f;
            if (Enabled) Status = "Выбран: " + Read.Name(character);
        }

        internal void SetEnabled(bool enabled, object character = null)
        {
            if (!enabled)
            {
                Enabled = false;
                applied = false;
                Status = "Выключено.";
                try
                {
                    object controller = Read.Get(Read.Service("ICameraService"), "CurrentCameraController");
                    object mode = Read.Get(controller, "CurrentCameraMode");
                    if (mode != null && mode.GetType().Name == "CameraModeExplorationFollow")
                        Read.Call(controller, "SwitchToManualExplorationCamera");
                }
                catch (Exception e) { Log.Error("core", "Disable camera follow", e); }
                return;
            }
            if (character != null) SelectTarget(character);
            if (targetGuid == 0)
            {
                Status = "Выбери персонажа в F8 перед включением автофоллоу.";
                return;
            }
            Enabled = true;
            nextCheck = 0f;
            Status = "Включено, ожидание исследования.";
        }

        internal void Tick()
        {
            if (!Enabled || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.6f;
            try
            {
                if (Read.Get(Read.Type("Gui"), "GameLocation") == null ||
                    Read.Get(Read.Type("Gui"), "Battle") != null)
                {
                    applied = false;
                    Status = "Приостановлено вне исследования или в бою.";
                    return;
                }
                object controller = Read.Get(Read.Service("ICameraService"), "CurrentCameraController");
                Type native = Read.Type("CameraControllerLocation");
                if (controller == null || native == null || !native.IsInstanceOfType(controller))
                {
                    applied = false;
                    Status = "Камера локации недоступна.";
                    return;
                }
                object target = Read.Items(Read.Get(Read.Service("IGameLocationCharacterService"),
                    "PartyCharacters")).FirstOrDefault(character =>
                        System.Object.Equals(Read.Get(Read.Get(character, "RulesetCharacter"), "Guid"), targetGuid));
                if (target == null)
                {
                    applied = false;
                    Status = "Выбранный персонаж недоступен на локации.";
                    return;
                }
                object mode = Read.Get(controller, "CurrentCameraMode");
                if (applied && mode != null && mode.GetType().Name == "CameraModeExplorationFollow") return;
                if (Read.Call(controller, "FollowCharacterForExploration", target, false) == null)
                {
                    // Void native method returns null on success. Confirm mode on the next update.
                }
                applied = true;
                Status = "Камера следует за " + Read.Name(target) + ".";
                Log.Write("Auto-follow camera target=" + targetGuid);
            }
            catch (Exception e)
            {
                applied = false;
                Enabled = false;
                Status = "Автофоллоу отключён: " + e.GetBaseException().Message;
                Log.Error("core", "Camera auto-follow", e);
            }
        }
    }
}
