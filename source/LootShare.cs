using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace SolastaCoop
{
    // Independent, read-only channel. Never issues a loot/gameplay RPC or opens a native inventory.
    internal static class LootWire
    {
        internal const byte EventCode = 191;
        internal const string Magic = "SolastaCoopView.loot/1";
        internal const int Hello = 0, Frame = 1, Close = 2;
        internal static bool Valid(object[] data)
        {
            if (data == null || data.Length < 3 || !System.Object.Equals(data[0], Magic) || !(data[1] is int)) return false;
            int kind = (int)data[1];
            if (kind == Hello) return data.Length == 3 && data[2] is bool;
            if (kind == Close) return data.Length == 3 && data[2] is int && (int)data[2] >= 0;
            return kind == Frame && data.Length == 4 && data[2] is int && (int)data[2] >= 0 &&
                data[3] is byte[] && TravelWire.Jpeg((byte[])data[3]);
        }
    }

    internal sealed class LootShare : IDisposable
    {
        private sealed class Incoming
        {
            internal int Actor;
            internal object[] Data;
        }
        private sealed class Peer
        {
            internal DateTime LastSeen;
            internal bool Accepts;
        }
        private readonly object sync = new object();
        private readonly Queue<Incoming> inbound = new Queue<Incoming>();
        private readonly Dictionary<int, Peer> peers = new Dictionary<int, Peer>();
        private object client, room;
        private Type photon, optionType, sendType;
        private EventInfo eventInfo;
        private Delegate handler;
        private MethodInfo raise;
        private DateTime nextHello, nextDetect, nextFrame, lastFrame;
        private bool online, sharing, autoDetected, manualSharing, viewShown = true;
        private int frameSequence, lastSequence = -1, sourceActor;
        private Texture2D image;
        private Rect window = new Rect(70, 85, 900, 550);
        private GameObject shield;
        private RectTransform shieldArea;
        internal bool Enabled = true;
        internal bool AutoDetect = true;
        internal bool ShouldCapture { get; private set; }
        internal static bool Capturing { get; private set; }
        internal string Status { get; private set; } = "Ожидание открытого лута. F11: вручную поделиться окном.";
        internal string Diagnostics { get { return "Loot view: enabled=" + Enabled + "; auto=" + AutoDetect +
            "; detected=" + autoDetected + "; manual=" + manualSharing + "; peers=" + peers.Count +
            "; sharing=" + sharing + "; sourceActor=" + sourceActor + "; frame=" + lastSequence; } }

        private static int Actor(object player)
        {
            object value = Read.Get(player, "ActorNumber");
            return value is int ? (int)value : 0;
        }
        private bool IsMember(int actor)
        {
            return actor > 0 && Read.Items(Read.Get(Read.Get(room, "Players"), "Values")).Any(p => Actor(p) == actor);
        }
        private bool Online()
        {
            return System.Object.Equals(Read.Get(photon, "InRoom"), true) && !System.Object.Equals(Read.Get(photon, "OfflineMode"), true);
        }
        private void Attach(object next)
        {
            if (ReferenceEquals(next, client)) return;
            Detach(); client = next;
            if (next == null) return;
            eventInfo = next.GetType().GetEvent("EventReceived");
            if (eventInfo == null) throw new MissingMemberException("Photon event receiver");
            var parameters = eventInfo.EventHandlerType.GetMethod("Invoke").GetParameters();
            if (parameters.Length != 1) throw new MissingMemberException("Photon event signature");
            var callback = GetType().GetMethod("Receive", BindingFlags.NonPublic | BindingFlags.Instance)
                .MakeGenericMethod(parameters[0].ParameterType);
            handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, this, callback);
            eventInfo.AddEventHandler(next, handler);
            raise = photon.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "RaiseEvent" && m.GetParameters().Length == 4);
            optionType = raise.GetParameters()[2].ParameterType;
            sendType = raise.GetParameters()[3].ParameterType;
            Log.Write("Loot mirror attached: view-only, no gameplay actions.");
        }
        private void Receive<T>(T evt)
        {
            try
            {
                if (!Enabled || !System.Object.Equals(Read.Get(evt, "Code"), LootWire.EventCode)) return;
                object[] data = Read.Get(evt, "CustomData") as object[];
                object sender = Read.Get(evt, "Sender");
                if (!(sender is int) || !LootWire.Valid(data)) return;
                lock (sync)
                {
                    if (inbound.Count >= 24) inbound.Dequeue();
                    inbound.Enqueue(new Incoming { Actor = (int)sender, Data = data });
                }
            }
            catch (Exception e) { Log.Error("network", "Receive loot mirror", e); }
        }
        private void Send(object[] data, int[] targets = null, bool reliable = false)
        {
            if (!online || raise == null) return;
            if (targets != null && targets.Length == 0) return;
            object options = Activator.CreateInstance(optionType), send = Activator.CreateInstance(sendType);
            if (targets != null) ObserverHud.SetMember(options, "TargetActors", targets);
            ObserverHud.SetMember(send, "Reliability", reliable);
            raise.Invoke(null, new object[] { LootWire.EventCode, data, options, send });
        }
        private void Detach()
        {
            if (eventInfo != null && client != null && handler != null)
                try { eventInfo.RemoveEventHandler(client, handler); }
                catch (Exception e) { Log.Error("network", "Detach loot mirror", e); }
            client = null; eventInfo = null; handler = null; raise = null;
        }
        private void Clear(bool destroyRoom)
        {
            lock (sync) inbound.Clear();
            peers.Clear();
            if (image != null) UnityEngine.Object.Destroy(image);
            image = null; lastSequence = -1; sourceActor = 0; lastFrame = DateTime.MinValue;
            sharing = false; ShouldCapture = false; manualSharing = false; autoDetected = false;
            if (shield != null) shield.SetActive(false);
            if (destroyRoom) room = null;
        }
        internal void SetEnabled(bool enabled)
        {
            if (enabled == Enabled) return;
            if (!enabled && online && sharing) Send(new object[] { LootWire.Magic, LootWire.Close, ++frameSequence }, null, true);
            Enabled = enabled;
            if (!enabled) { Clear(false); Detach(); Status = "Общий просмотр лута выключен."; }
            else nextHello = nextDetect = DateTime.MinValue;
        }
        internal void ToggleManual()
        {
            manualSharing = !manualSharing;
            Log.Write("Loot mirror manual share=" + manualSharing);
        }
        internal void TogglePreview()
        {
            viewShown = !viewShown;
            UpdateShield();
        }
        // Detect actual visible inventory/loot panels, not world-space chest objects.
        private static bool DetectLoot()
        {
            var gui = Read.Get(Read.Type("Gui"), "GuiService");
            if (gui == null) return false;
            foreach (string typeName in new[] { "GameLocationScreenLoot", "GameLocationLootScreen",
                "GameLocationScreenContainer", "LootScreen", "LootPanel", "LootContainerPanel",
                "ContainerLootPanel", "GameLocationInventoryLootPanel" })
            {
                Type type = Read.Type(typeName);
                if (type != null && TravelShare.Visible(Read.Call(gui, "GetScreen", type))) return true;
            }
            // Explicitly look for nested loot sections in the standard inventory screen.
            foreach (string screenName in new[] { "GameLocationScreenInventory", "GameInventoryScreen" })
            {
                Type inventoryType = Read.Type(screenName);
                object screen = inventoryType == null ? null : Read.Call(gui, "GetScreen", inventoryType);
                if (!TravelShare.Visible(screen)) continue;
                foreach (string panelName in new[] { "LootPanel", "LootingPanel", "ContainerPanel", "ContainerLootPanel", "LootSection" })
                {
                    object subPanel = Read.Get(screen, panelName);
                    if (TravelShare.Visible(subPanel)) return true;
                }
            }
            // Some game builds embed the loot view as a subpanel of another screen.
            // Screen component names are intentionally filtered to avoid ordinary inventory.
            foreach (MonoBehaviour behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (behaviour == null || !behaviour.isActiveAndEnabled || !behaviour.gameObject.activeInHierarchy) continue;
                string name = behaviour.GetType().Name;
                if (!(name.IndexOf("Loot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      name.IndexOf("Container", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                if (!(name.IndexOf("Panel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      name.IndexOf("Screen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      name.IndexOf("Modal", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                Canvas canvas = behaviour.GetComponentInParent<Canvas>();
                if (canvas == null || !canvas.isActiveAndEnabled || !canvas.gameObject.activeInHierarchy) continue;
                object visible = Read.Get(behaviour, "Visible");
                if (visible is bool && !(bool)visible) continue;
                object hiding = Read.Get(behaviour, "Hiding");
                if (hiding is bool && (bool)hiding) continue;
                if (!(visible is bool))
                {
                    CanvasGroup group = behaviour.GetComponent<CanvasGroup>();
                    if (group != null && group.alpha <= 0.01f) continue;
                }
                return true;
            }
            return false;
        }
        internal void Tick()
        {
            ShouldCapture = false;
            if (!Enabled) return;
            try
            {
                photon = Read.Type("Photon.Pun.PhotonNetwork");
                online = Online();
                object nextRoom = Read.Get(photon, "CurrentRoom");
                if (!online) { if (room != null) Clear(true); Detach(); return; }
                if (!ReferenceEquals(nextRoom, room)) { Clear(false); room = nextRoom; nextHello = nextDetect = DateTime.MinValue; }
                Attach(Read.Get(photon, "NetworkingClient"));
                DateTime now = DateTime.UtcNow;
                Incoming[] messages;
                lock (sync) { messages = inbound.ToArray(); inbound.Clear(); }
                foreach (Incoming packet in messages) Accept(packet, now);
                foreach (int actor in peers.Where(p => !IsMember(p.Key) || now - p.Value.LastSeen > TimeSpan.FromSeconds(12))
                    .Select(p => p.Key).ToArray()) peers.Remove(actor);
                if (now >= nextHello)
                {
                    Send(new object[] { LootWire.Magic, LootWire.Hello, viewShown });
                    nextHello = now.AddSeconds(3);
                }
                if (now >= nextDetect)
                {
                    bool previous = autoDetected;
                    autoDetected = AutoDetect && DetectLoot();
                    if (previous != autoDetected) Log.Write("Loot mirror detector=" + autoDetected);
                    nextDetect = now.AddSeconds(0.75);
                }
                bool sendFrames = (manualSharing || autoDetected) && peers.Values.Any(p => p.Accepts);
                if (sharing && !sendFrames) Send(new object[] { LootWire.Magic, LootWire.Close, ++frameSequence }, null, true);
                sharing = sendFrames;
                object pending = Read.Get(Read.Get(client, "LoadBalancingPeer"), "QueuedOutgoingCommands");
                bool notCongested = !(pending is int) || (int)pending < 8;
                if (sharing && notCongested && !Capturing && !TravelShare.Capturing && now >= nextFrame)
                {
                    ShouldCapture = true; nextFrame = now.AddSeconds(1.2);
                }
                if (sourceActor > 0 && (now - lastFrame > TimeSpan.FromSeconds(4) || !IsMember(sourceActor)))
                    ClearReceived();
                UpdateShield();
                Status = sharing ? "Показываем лут второму игроку (только просмотр)." :
                    sourceActor > 0 ? "Просмотр лута игрока #" + sourceActor + " (только просмотр). F12 скрывает окно." :
                    "Ожидание лута. F11 вручную показывает текущее окно, F12 скрывает просмотр.";
            }
            catch (Exception e)
            {
                Log.Error("network", "Loot mirror disabled", e);
                SetEnabled(false);
                Status = "Просмотр отключён: " + e.GetBaseException().Message;
            }
        }
        private void Accept(Incoming packet, DateTime now)
        {
            if (!IsMember(packet.Actor)) return;
            int kind = (int)packet.Data[1];
            Peer peer;
            if (!peers.TryGetValue(packet.Actor, out peer))
            {
                if (kind != LootWire.Hello || peers.Count >= 4) return;
                peers[packet.Actor] = peer = new Peer();
            }
            peer.LastSeen = now;
            if (kind == LootWire.Hello) { peer.Accepts = (bool)packet.Data[2]; return; }
            if (kind == LootWire.Close)
            {
                if (sourceActor == packet.Actor && (int)packet.Data[2] > lastSequence) ClearReceived();
                return;
            }
            if (kind != LootWire.Frame || (manualSharing || autoDetected) || !viewShown) return;
            if (sourceActor != 0 && sourceActor != packet.Actor) return;
            if ((int)packet.Data[2] <= lastSequence) return;
            if (image == null) image = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!ImageConversion.LoadImage(image, (byte[])packet.Data[3], false)) return;
            sourceActor = packet.Actor; lastSequence = (int)packet.Data[2]; lastFrame = now;
        }
        private void ClearReceived()
        {
            if (image != null) UnityEngine.Object.Destroy(image);
            image = null; sourceActor = 0; lastSequence = -1; lastFrame = DateTime.MinValue;
            if (shield != null) shield.SetActive(false);
        }
        internal IEnumerator Capture()
        {
            Capturing = true;
            yield return new WaitForEndOfFrame();
            try { if (Enabled && sharing && Online()) CaptureFrame(); }
            catch (Exception e) { Log.Error("network", "Capture loot UI", e); }
            finally { Capturing = false; }
        }
        private void CaptureFrame()
        {
            Texture2D full = null, scaled = null;
            RenderTexture target = null, original = RenderTexture.active;
            try
            {
                full = ScreenCapture.CaptureScreenshotAsTexture();
                if (full == null) return;
                float ratio = Math.Min(1f, Math.Min(1280f / full.width, 720f / full.height));
                int w = Math.Max(16, (int)(full.width * ratio));
                int h = Math.Max(16, (int)(full.height * ratio));
                target = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(full, target); RenderTexture.active = target;
                scaled = new Texture2D(w, h, TextureFormat.RGB24, false);
                scaled.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                scaled.Apply(false, false);
                byte[] jpeg = ImageConversion.EncodeToJPG(scaled, 65);
                if (jpeg.Length > TravelWire.MaxFrameBytes) jpeg = ImageConversion.EncodeToJPG(scaled, 40);
                if (!TravelWire.Jpeg(jpeg))
                {
                    Log.Event("network", "WARN", "Loot frame exceeds size limit; skipped.");
                    return;
                }
                int[] targets = peers.Where(p => p.Value.Accepts).Select(p => p.Key).ToArray();
                Send(new object[] { LootWire.Magic, LootWire.Frame, ++frameSequence, jpeg }, targets);
            }
            finally
            {
                RenderTexture.active = original;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (full != null) UnityEngine.Object.Destroy(full);
                if (scaled != null) UnityEngine.Object.Destroy(scaled);
            }
        }
        private void UpdateShield()
        {
            bool show = Enabled && online && viewShown && sourceActor > 0 && image != null &&
                !(manualSharing || autoDetected) && DateTime.UtcNow - lastFrame < TimeSpan.FromSeconds(4);
            if (!show) { if (shield != null) shield.SetActive(false); return; }
            if (shield == null)
            {
                shield = new GameObject("CoopLootPreviewInputShield", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                UnityEngine.Object.DontDestroyOnLoad(shield);
                Canvas canvas = shield.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32761;
                GameObject area = new GameObject("PreviewArea", typeof(RectTransform), typeof(Image));
                area.transform.SetParent(shield.transform, false);
                Image blocker = area.GetComponent<Image>();
                blocker.color = Color.clear; blocker.raycastTarget = true;
                shieldArea = area.GetComponent<RectTransform>();
                shieldArea.anchorMin = shieldArea.anchorMax = new Vector2(0, 1);
                shieldArea.pivot = new Vector2(0, 1);
            }
            shield.SetActive(true);
            shieldArea.anchoredPosition = new Vector2(window.x, -window.y);
            shieldArea.sizeDelta = new Vector2(window.width, window.height);
        }
        internal void Draw()
        {
            if (!Enabled || Capturing || TravelShare.Capturing || !online || !viewShown ||
                image == null || sourceActor == 0 || (manualSharing || autoDetected) ||
                DateTime.UtcNow - lastFrame > TimeSpan.FromSeconds(4)) return;
            window.width = Math.Min(950, Screen.width - 20);
            window.height = Math.Min(600, Screen.height - 20);
            window.x = Mathf.Clamp(window.x, 0, Screen.width - window.width);
            window.y = Mathf.Clamp(window.y, 0, Screen.height - window.height);
            window = GUI.Window(487203, window, DrawWindow, "Лут союзника: только просмотр | F12");
            UpdateShield();
        }
        private void DrawWindow(int id)
        {
            if (GUI.Button(new Rect(window.width - 100, 2, 86, 20), "Скрыть")) TogglePreview();
            float width = window.width - 16, height = window.height - 48;
            float ratio = Math.Min(width / image.width, height / image.height);
            GUI.DrawTexture(new Rect(8 + (width - image.width * ratio) / 2, 25,
                image.width * ratio, image.height * ratio), image, ScaleMode.StretchToFill);
            GUI.Label(new Rect(8, window.height - 21, window.width - 16, 20),
                "Содержимое у открывшего игрока. Нажатия здесь не перемещают предметы.");
            GUI.DragWindow(new Rect(0, 0, window.width - 110, 23));
            if (new Rect(0, 0, window.width, window.height).Contains(Event.current.mousePosition) &&
                (Event.current.type == EventType.MouseDown || Event.current.type == EventType.ScrollWheel)) Event.current.Use();
        }
        public void Dispose()
        {
            if (online && sharing) try { Send(new object[] { LootWire.Magic, LootWire.Close, ++frameSequence }, null, true); }
                catch (Exception e) { Log.Error("network", "Close loot mirror", e); }
            Enabled = false; Clear(true); Detach(); Capturing = false;
            if (shield != null) UnityEngine.Object.Destroy(shield);
            shield = null; shieldArea = null;
        }
    }
}
