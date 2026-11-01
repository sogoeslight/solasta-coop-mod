using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace SolastaCoop
{
    internal sealed class TravelShare : IDisposable
    {
        private sealed class Peer
        {
            internal DateTime Seen, PointerAt;
            internal bool WantsFrames, PointerVisible;
            internal float X, Y;
            internal int Sequence;
        }
        private sealed class Incoming { internal int Sender; internal object[] Data; }
        private readonly object sync = new object();
        private readonly Queue<Incoming> incoming = new Queue<Incoming>();
        private readonly Dictionary<int, Peer> peers = new Dictionary<int, Peer>();
        private object client, room;
        private EventInfo eventInfo;
        private Delegate handler;
        private Type photon;
        private MethodInfo raise;
        private Type optionsType, sendType;
        private DateTime nextHello, nextPointer, nextFrame, frameAt;
        private bool host, online, localTravel, wasSharing, previewShown = true;
        private int master, sequence, receivedSequence = -1, pointerSequence;
        private Texture2D image;
        private GameObject inputShield;
        private RectTransform shieldArea;
        private Rect preview = new Rect(16, 16, 1000, 620), imageRect;
        internal static bool Capturing { get; private set; }
        internal bool Enabled = true;
        internal string Status = "Travel preview enabled. All players need the updated mod.";
        internal bool ShouldCapture { get; private set; }
        internal string DiagnosticSummary { get { return Status + "; enabled=" + Enabled + "; host=" + host +
            "; map=" + localTravel + "; peers=" + peers.Count + "; lastFrame=" + receivedSequence; } }

        private static object Screen(string name)
        {
            Type type = Read.Type(name);
            object service = Read.Get(Read.Type("Gui"), "GuiService");
            return type == null || service == null ? null : Read.Call(service, "GetScreen", type);
        }
        internal static bool Visible(object screen)
        {
            return screen != null && System.Object.Equals(Read.Get(screen, "Visible"), true) && !System.Object.Equals(Read.Get(screen, "Hiding"), true);
        }
        private bool InTravelUi()
        {
            return Visible(Screen("GameCampaignScreen")) || Visible(Screen("TravelPreparationModal")) || Visible(Screen("TravelSettingsModal"));
        }
        private int Actor(object player)
        {
            object value = Read.Get(player, "ActorNumber"); return value is int ? (int)value : 0;
        }
        private bool Member(int actor)
        {
            return Read.Items(Read.Get(Read.Get(room, "Players"), "Values")).Any(p => Actor(p) == actor);
        }
        private bool Online()
        {
            return System.Object.Equals(Read.Get(photon, "InRoom"), true) && System.Object.Equals(Read.Get(photon, "OfflineMode"), false);
        }
        private void Attach(object next)
        {
            if (ReferenceEquals(next, client)) return;
            Detach(); client = next;
            if (next == null) return;
            eventInfo = next.GetType().GetEvent("EventReceived");
            if (eventInfo == null) throw new MissingMemberException("Photon EventReceived");
            var parameters = eventInfo.EventHandlerType.GetMethod("Invoke").GetParameters();
            if (parameters.Length != 1) throw new MissingMemberException("Photon EventReceived signature");
            MethodInfo callback = GetType().GetMethod("OnEvent", BindingFlags.Instance | BindingFlags.NonPublic).MakeGenericMethod(parameters[0].ParameterType);
            handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, this, callback);
            eventInfo.AddEventHandler(next, handler);
            raise = photon.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == "RaiseEvent" && m.GetParameters().Length == 4);
            optionsType = raise.GetParameters()[2].ParameterType; sendType = raise.GetParameters()[3].ParameterType;
            Log.Write("Travel display channel attached; game UI frames only, no desktop capture or control commands.");
        }
        private void OnEvent<T>(T evt)
        {
            try
            {
                if (!Enabled || !System.Object.Equals(Read.Get(evt, "Code"), TravelWire.EventCode)) return;
                object[] data = Read.Get(evt, "CustomData") as object[];
                object actor = Read.Get(evt, "Sender");
                if (!(actor is int) || !TravelWire.Valid(data)) return;
                lock (sync)
                {
                    if (incoming.Count == 32) incoming.Dequeue();
                    incoming.Enqueue(new Incoming { Sender = (int)actor, Data = data });
                }
            }
            catch (Exception e) { Log.Error("network", "Receive travel display packet", e); }
        }
        private void Send(object[] data, int[] targets = null, bool reliable = false)
        {
            if (raise == null || !Online()) return;
            object options = Activator.CreateInstance(optionsType), send = Activator.CreateInstance(sendType);
            if (targets != null)
            {
                if (targets.Length == 0) return;
                ObserverHud.SetMember(options, "TargetActors", targets);
            }
            ObserverHud.SetMember(send, "Reliability", reliable);
            // ReceiverGroup.Others is the SDK default. Frames are uncached and never sent as game RPCs.
            raise.Invoke(null, new object[] { TravelWire.EventCode, data, options, send });
        }
        private void ClearRoom()
        {
            peers.Clear(); lock (sync) incoming.Clear();
            if (image != null) UnityEngine.Object.Destroy(image);
            image = null; receivedSequence = -1; frameAt = DateTime.MinValue;
            wasSharing = false; ShouldCapture = false;
            if (inputShield != null) inputShield.SetActive(false);
        }
        internal void SetEnabled(bool enabled)
        {
            if (Enabled == enabled) return;
            if (!enabled && host && online) Send(new object[] { TravelWire.Magic, TravelWire.End, ++sequence }, null, true);
            Enabled = enabled; nextHello = DateTime.MinValue;
            if (!enabled) { ClearRoom(); Detach(); Status = "Travel preview disabled."; }
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
                if (!online) { if (room != null) { ClearRoom(); room = null; } Detach(); return; }
                if (!ReferenceEquals(nextRoom, room)) { ClearRoom(); room = nextRoom; nextHello = DateTime.MinValue; }
                Attach(Read.Get(photon, "NetworkingClient"));
                int nextMaster = Actor(Read.Get(photon, "MasterClient"));
                if (master != nextMaster) { ClearRoom(); master = nextMaster; nextHello = DateTime.MinValue; }
                host = System.Object.Equals(Read.Get(photon, "IsMasterClient"), true);
                localTravel = InTravelUi();
                DateTime now = DateTime.UtcNow;
                Incoming[] received;
                lock (sync) { received = incoming.ToArray(); incoming.Clear(); }
                foreach (Incoming packet in received) Accept(packet, now);
                foreach (int key in peers.Where(p => now - p.Value.Seen > TimeSpan.FromSeconds(10) || !Member(p.Key)).Select(p => p.Key).ToArray()) peers.Remove(key);
                if (now >= nextHello)
                {
                    Send(new object[] { TravelWire.Magic, TravelWire.Hello, previewShown });
                    nextHello = now.AddSeconds(3);
                }
                bool viewers = peers.Values.Any(p => p.WantsFrames);
                bool sharing = host && localTravel && viewers;
                if (wasSharing && !sharing) Send(new object[] { TravelWire.Magic, TravelWire.End, ++sequence }, null, true);
                wasSharing = sharing;
                object queued = Read.Get(Read.Get(client, "LoadBalancingPeer"), "QueuedOutgoingCommands");
                bool writable = !(queued is int) || (int)queued < 8;
                if (sharing && writable && !Capturing && now >= nextFrame)
                { ShouldCapture = true; nextFrame = now.AddSeconds(1); }
                if (now >= nextPointer && ((host && localTravel) || (!host && image != null && now - frameAt < TimeSpan.FromSeconds(5))))
                {
                    Vector3 mouse = Input.mousePosition;
                    float x = 0, y = 0; bool shown = false;
                    if (host)
                    {
                        x = mouse.x / Math.Max(1, UnityEngine.Screen.width);
                        y = 1f - mouse.y / Math.Max(1, UnityEngine.Screen.height);
                        shown = TravelWire.Unit(x) && TravelWire.Unit(y);
                    }
                    else if (previewShown)
                    {
                        var point = new Vector2(mouse.x, UnityEngine.Screen.height - mouse.y);
                        shown = imageRect.width > 0 && imageRect.height > 0 && imageRect.Contains(point);
                        if (shown) { x = (point.x - imageRect.x) / imageRect.width; y = (point.y - imageRect.y) / imageRect.height; }
                    }
                    Send(new object[] { TravelWire.Magic, TravelWire.Pointer, ++pointerSequence,
                        Mathf.Clamp01(x), Mathf.Clamp01(y), shown }, peers.Keys.ToArray());
                    nextPointer = now.AddSeconds(0.2);
                }
                UpdateInputShield();
                Status = sharing ? "Host shares the travel screen at up to 1 FPS and cursors at up to 5 updates/s." :
                    host ? "Waiting for the travel map and clients with the updated mod." :
                    image != null && now - frameAt < TimeSpan.FromSeconds(5) ? "Host travel screen received. Press F6 to show or hide the preview." :
                    "Waiting for the host travel screen. All players need the updated mod.";
            }
            catch (Exception e)
            {
                Log.Error("network", "Travel display suspended", e); SetEnabled(false);
                Status = "Preview suspended: " + e.GetBaseException().Message;
            }
        }
        private void Accept(Incoming packet, DateTime now)
        {
            int sender = packet.Sender; object[] data = packet.Data;
            bool member = Member(sender);
            if (!member) return;
            int kind = (int)data[1];
            Peer peer;
            if (!peers.TryGetValue(sender, out peer))
            {
                if (kind != TravelWire.Hello || peers.Count >= 8) return;
                peers[sender] = peer = new Peer();
            }
            peer.Seen = now;
            if (kind == TravelWire.Hello) { peer.WantsFrames = (bool)data[2]; return; }
            if (kind == TravelWire.Pointer)
            {
                int order = (int)data[2]; if (order <= peer.Sequence) return;
                peer.Sequence = order; peer.X = (float)data[3]; peer.Y = (float)data[4];
                peer.PointerVisible = (bool)data[5]; peer.PointerAt = now; return;
            }
            if (host || !TravelWire.HostFrame(sender, master, member) || (int)data[2] <= receivedSequence) return;
            receivedSequence = (int)data[2];
            if (kind == TravelWire.End)
            {
                if (image != null) UnityEngine.Object.Destroy(image);
                image = null; return;
            }
            if (kind == TravelWire.Frame)
            {
                if (image == null) image = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (ImageConversion.LoadImage(image, (byte[])data[3], false)) frameAt = now;
            }
        }
        internal IEnumerator Capture()
        {
            Capturing = true;
            yield return new WaitForEndOfFrame();
            try { if (Enabled && host && localTravel && Online()) CaptureFrame(); }
            catch (Exception e) { Log.Error("network", "Capture travel game UI", e); }
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
                int w = Math.Max(16, (int)(full.width * ratio)), h = Math.Max(16, (int)(full.height * ratio));
                target = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(full, target); RenderTexture.active = target;
                scaled = new Texture2D(w, h, TextureFormat.RGB24, false);
                scaled.ReadPixels(new Rect(0, 0, w, h), 0, 0); scaled.Apply(false, false);
                byte[] jpeg = ImageConversion.EncodeToJPG(scaled, 65);
                if (jpeg.Length > TravelWire.MaxFrameBytes) jpeg = ImageConversion.EncodeToJPG(scaled, 40);
                if (!TravelWire.Jpeg(jpeg)) { Log.Event("network", "WARN", "Travel frame skipped: size or dimensions exceed limit."); return; }
                int[] viewers = peers.Where(p => p.Value.WantsFrames).Select(p => p.Key).ToArray();
                Send(new object[] { TravelWire.Magic, TravelWire.Frame, ++sequence, jpeg }, viewers);
            }
            finally
            {
                RenderTexture.active = original;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (full != null) UnityEngine.Object.Destroy(full);
                if (scaled != null) UnityEngine.Object.Destroy(scaled);
            }
        }
        internal void TogglePreview()
        {
            previewShown = !previewShown; nextHello = DateTime.MinValue;
            UpdateInputShield();
        }
        private void UpdateInputShield()
        {
            bool shown = Enabled && online && !host && previewShown && image != null && DateTime.UtcNow - frameAt <= TimeSpan.FromSeconds(5);
            if (!shown) { if (inputShield != null) inputShield.SetActive(false); return; }
            if (inputShield == null)
            {
                // IMGUI windows do not intercept Unity UI raycasts. A transparent overlay blocks native buttons underneath.
                inputShield = new GameObject("CoopTravelPreviewInputShield", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                UnityEngine.Object.DontDestroyOnLoad(inputShield);
                Canvas canvas = inputShield.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
                var area = new GameObject("PreviewArea", typeof(RectTransform), typeof(Image));
                area.transform.SetParent(inputShield.transform, false);
                Image blocker = area.GetComponent<Image>(); blocker.color = Color.clear; blocker.raycastTarget = true;
                shieldArea = area.GetComponent<RectTransform>();
                shieldArea.anchorMin = shieldArea.anchorMax = new Vector2(0, 1);
                shieldArea.pivot = new Vector2(0, 1);
            }
            inputShield.SetActive(true);
            shieldArea.anchoredPosition = new Vector2(preview.x, -preview.y);
            shieldArea.sizeDelta = new Vector2(preview.width, preview.height);
        }
        internal void Draw()
        {
            if (!Enabled || Capturing || !online) return;
            if (host && localTravel) DrawPointers(new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height));
            if (host || !previewShown || image == null || DateTime.UtcNow - frameAt > TimeSpan.FromSeconds(5)) return;
            // Near fullscreen at every resolution, keeping a small safe border.
            // The image below is letterboxed to preserve the host's aspect ratio.
            float marginX = Mathf.Max(8f, UnityEngine.Screen.width * 0.015f);
            float marginY = Mathf.Max(8f, UnityEngine.Screen.height * 0.025f);
            preview = new Rect(marginX, marginY,
                Mathf.Max(160f, UnityEngine.Screen.width - 2f * marginX),
                Mathf.Max(120f, UnityEngine.Screen.height - 2f * marginY));
            preview = GUI.Window(487202, preview, DrawPreview, "Travel: host screen (read-only) | F6");
            UpdateInputShield();
        }
        private void DrawPreview(int id)
        {
            if (GUI.Button(new Rect(preview.width - 90, 2, 65, 20), "Hide")) TogglePreview();
            float width = preview.width - 16, height = preview.height - 45;
            float ratio = Math.Min(width / image.width, height / image.height);
            Rect local = new Rect(8 + (width - image.width * ratio) / 2, 25, image.width * ratio, image.height * ratio);
            GUI.DrawTexture(local, image, ScaleMode.StretchToFill);
            imageRect = new Rect(preview.x + local.x, preview.y + local.y, local.width, local.height);
            DrawPointers(local);
            GUI.Label(new Rect(8, preview.height - 21, preview.width - 16, 20), "The host makes all selections. Clicks here are not sent. Player cursors indicate where they point.");
            GUI.DragWindow(new Rect(0, 0, preview.width - 100, 23));
            if (new Rect(0, 0, preview.width, preview.height).Contains(Event.current.mousePosition) &&
                (Event.current.type == EventType.MouseDown || Event.current.type == EventType.ScrollWheel)) Event.current.Use();
        }
        private void DrawPointers(Rect area)
        {
            Color before = GUI.color;
            try
            {
                foreach (var item in peers)
                {
                    Peer p = item.Value;
                    if (!p.PointerVisible || DateTime.UtcNow - p.PointerAt > TimeSpan.FromSeconds(1.5)) continue;
                    GUI.color = item.Key == master ? Color.yellow : Color.cyan;
                    float x = area.x + area.width * p.X, y = area.y + area.height * p.Y;
                    GUI.DrawTexture(new Rect(x - 3, y - 3, 7, 7), Texture2D.whiteTexture);
                    GUI.Label(new Rect(x + 7, y, 90, 22), item.Key == master ? "Host" : "Player #" + item.Key);
                }
            }
            finally { GUI.color = before; }
        }
        private void Detach()
        {
            if (client != null && eventInfo != null && handler != null)
                try { eventInfo.RemoveEventHandler(client, handler); } catch (Exception e) { Log.Error("network", "Detach travel channel", e); }
            client = null; eventInfo = null; handler = null; raise = null;
        }
        public void Dispose()
        {
            Enabled = false; ClearRoom(); Detach(); room = null; Capturing = false;
            if (inputShield != null) UnityEngine.Object.Destroy(inputShield);
            inputShield = null; shieldArea = null;
        }
    }
}
