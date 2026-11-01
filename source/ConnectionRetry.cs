using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace SolastaCoop
{
    // A failed Photon create can have already created the room on the server.
    // Only JoinRoom can be retried, and only after the user explicitly confirms
    // that Solasta's native error dialog has been dismissed.
    internal static class ConnectionRetry
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly RetryBudget budget = new RetryBudget();
        private static object manager;
        private static MethodInfo request;
        private static object[] arguments;
        private static readonly Dictionary<string, Delegate[]> menuCallbacks = new Dictionary<string, Delegate[]>();
        private static bool installed, replaying, expectedRegionDisconnect, needsAcknowledgement, acknowledged;
        internal static bool Enabled = true;
        internal static string Status = "Защищённые повторы входа включены. Создание комнат никогда не повторяется автоматически.";
        internal static bool Pending { get { return budget.Pending; } }
        internal static bool NeedsAcknowledgement { get { return budget.Pending && needsAcknowledgement; } }

        internal static void Install()
        {
            if (installed) return;
            Type type = Read.Type("NetworkingManager");
            if (type == null) return;
            foreach (string name in new[] { "CreateRoom", "JoinRoom" })
            {
                MethodInfo target = type.GetMethods(Flags).Single(m => m.Name == name);
                MethodInfo prefix = typeof(ConnectionRetry).GetMethod(name == "CreateRoom" ? "CaptureCreate" : "CaptureJoin", Flags);
                if (prefix.IsGenericMethodDefinition) prefix = prefix.MakeGenericMethod(target.GetParameters()[0].ParameterType);
                HarmonySupport.Patch(target, prefix);
            }
            MethodInfo disconnect = type.GetMethod("OnDisconnected", Flags);
            HarmonySupport.Patch(disconnect, typeof(ConnectionRetry).GetMethod("Disconnected", Flags)
                .MakeGenericMethod(disconnect.GetParameters()[0].ParameterType));
            HarmonySupport.Patch(type.GetMethod("OnJoinedRoom", Flags), null, typeof(ConnectionRetry).GetMethod("Joined", Flags));
            foreach (string name in new[] { "OnCreateRoomFailed", "OnJoinRoomFailed" })
                HarmonySupport.Patch(type.GetMethod(name, Flags), typeof(ConnectionRetry).GetMethod("Rejected", Flags));
            Type menu = Read.Type("MenuSubpanel");
            MethodInfo cancel = menu == null ? null : menu.GetMethod("OnCancelCb", Flags);
            if (cancel != null) HarmonySupport.Patch(cancel, typeof(ConnectionRetry).GetMethod("Cancelled", Flags));
            installed = true;
            Log.Write("Guarded JoinRoom retries installed. CreateRoom is never replayed; user confirmation required after every failure.");
        }

        private static void CaptureCreate<T>(object __instance, MethodBase __originalMethod, T __0)
        { Capture(__instance, __originalMethod, new object[] { __0 }); }
        private static void CaptureJoin(object __instance, MethodBase __originalMethod, string __0, string __1)
        { Capture(__instance, __originalMethod, new object[] { __0, __1 }); }
        private static void Capture(object __instance, MethodBase __originalMethod, object[] captured)
        {
            if (replaying) return;
            Cancel("Начат новый штатный запрос подключения.");
            if (!Enabled) return;
            // A timed-out CreateRoom may already exist on the Photon master server. Retrying it
            // creates GameIdAlreadyExists (32766), even when the local client isn't in the room.
            if (__originalMethod.Name == "CreateRoom")
            {
                Status = "Создание комнаты выполняет игра. Автоповтор создания отключён, чтобы не получать 32766.";
                Log.Event("network", "INFO", "CreateRoom observed, never retried to prevent duplicate room name.");
                return;
            }
            manager = __instance; request = (MethodInfo)__originalMethod;
            arguments = captured;
            CaptureMenuCallbacks();
            budget.Begin(); needsAcknowledgement = false; acknowledged = false;
            Status = "Вход в комнату. При ошибке закройте системное окно Photon и разрешите повтор в разделе Сеть.";
            Log.Event("network", "INFO", "JoinRoom captured; private parameters are not logged.");
        }

        private static void Disconnected<T>(T __0)
        {
            try
            {
                if (!Enabled || !budget.Active) return;
                if (Read.Text(__0) == "DisconnectByClientLogic" && expectedRegionDisconnect)
                { expectedRegionDisconnect = false; return; }
                expectedRegionDisconnect = false;
                if (budget.Fail(Read.Text(__0), DateTime.UtcNow))
                {
                    needsAcknowledgement = true;
                    acknowledged = false;
                    Status = "Ошибка " + Read.Text(__0) + ". Закройте окно «Фотон недоступен» через OK, " +
                        "затем подтвердите повтор " + (budget.Used + 1) + "/3 в разделе Сеть.";
                    Log.Event("network", "WARN", Status);
                }
                else if (!budget.Active) Cancel("Повторы завершены: " + Read.Text(__0) + ".");
            }
            catch (Exception e) { Cancel("Ошибка планирования повтора."); Log.Error("network", "Plan retry", e); }
        }

        private static void Joined()
        {
            Type photon = Read.Type("Photon.Pun.PhotonNetwork");
            if (Object.Equals(Read.Get(photon, "OfflineMode"), false) && Object.Equals(Read.Get(photon, "InRoom"), true))
                Cancel("Подключение выполнено.");
        }
        private static void Rejected() { Cancel("Photon отклонил вход или создание комнаты. Повторы остановлены."); }
        private static void Cancelled() { Cancel("Отменено пользователем."); }

        internal static void Cancel(string reason)
        {
            budget.Cancel(); manager = null; request = null; expectedRegionDisconnect = false;
            needsAcknowledgement = false; acknowledged = false;
            if (arguments != null) Array.Clear(arguments, 0, arguments.Length);
            arguments = null; menuCallbacks.Clear(); Status = reason;
        }

        internal static void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            if (!enabled) Cancel("Повторы входа выключены.");
            else Status = "Защищённые повторы входа включены. Создание комнат не повторяется автоматически.";
        }

        private static bool NativeModalBlocksInput()
        {
            try
            {
                Type gui = Read.Type("Gui");
                object service = gui == null ? null : Read.Get(gui, "GuiService");
                return Object.Equals(Read.Get(service, "GrabbedCameraControls"), true);
            }
            catch (Exception e)
            {
                // If the native modal state cannot be read, retrying is unsafe.
                Log.Error("network", "Check Photon error modal", e);
                return true;
            }
        }

        internal static void ConfirmErrorDismissed()
        {
            if (!Enabled || !budget.Pending || !needsAcknowledgement) return;
            if (NativeModalBlocksInput())
            {
                Status = "Сначала закройте штатное окно Photon кнопкой OK.";
                return;
            }
            acknowledged = true;
            needsAcknowledgement = false;
            Status = "Ошибка подтверждена. Ожидаем безопасного состояния сети для одного повтора входа.";
            Log.Event("network", "INFO", "User acknowledged native error dialog; JoinRoom retry allowed.");
        }

        private static void CaptureMenuCallbacks()
        {
            Type menu = Read.Type("MainMenuScreen");
            if (menu == null) return;
            foreach (string name in new[] { "OnRoomEntered", "OnRoomCreated", "OnRoomCreationFailed" })
            {
                Delegate callbacks = Read.Get(manager, name) as Delegate;
                if (callbacks == null) continue;
                Delegate[] selected = callbacks.GetInvocationList().Where(d => d.Target != null && d.Target.GetType() == menu).ToArray();
                if (selected.Length > 0) menuCallbacks[name] = selected;
            }
        }
        private static void RestoreMenuCallbacks()
        {
            foreach (var entry in menuCallbacks)
            {
                Delegate current = Read.Get(manager, entry.Key) as Delegate;
                foreach (Delegate callback in entry.Value)
                {
                    if (!Object.Equals(Read.Get(callback.Target, "Visible"), true) ||
                        Object.Equals(Read.Get(callback.Target, "Hiding"), true)) continue;
                    if (current == null || !current.GetInvocationList().Contains(callback)) current = Delegate.Combine(current, callback);
                }
                ObserverHud.SetMember(manager, entry.Key, current);
            }
        }

        internal static void Tick()
        {
            if (!Enabled || !budget.Pending || needsAcknowledgement || !acknowledged || NativeModalBlocksInput()) return;
            try
            {
                Type photon = Read.Type("Photon.Pun.PhotonNetwork");
                object client = Read.Get(photon, "NetworkingClient");
                bool offline = Object.Equals(Read.Get(photon, "OfflineMode"), true);
                string state = Read.Text(Read.Get(client, "State"));
                if (!offline && Object.Equals(Read.Get(photon, "InRoom"), true))
                { Cancel("Подключение выполнено."); return; }
                // The facade sometimes reports ConnectedToMasterServer even while the peer is disconnected.
                bool safe = state == "Disconnected" || state == "PeerCreated" ||
                    (offline && (state == "Joined" || state == "ConnectedToMasterServer"));
                if (!budget.Take(DateTime.UtcNow, safe)) return;
                if (manager == null || request == null || arguments == null) { Cancel("Нет запроса для повтора."); return; }
                if (request.Name != "JoinRoom") { Cancel("Создавать комнату повторно небезопасно."); return; }
                acknowledged = false;
                Status = "Повтор входа " + budget.Used + "/3 (таймаут Photon 10 с).";
                Log.Event("network", "INFO", Status);
                replaying = true;
                try { RestoreMenuCallbacks(); request.Invoke(manager, arguments); }
                finally { replaying = false; }
            }
            catch (Exception e) { Cancel("Не удалось повторить вход."); Log.Error("network", "Replay native JoinRoom request", e); }
        }
    }
}
