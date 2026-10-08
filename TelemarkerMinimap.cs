using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Splatform;
using UnityEngine;
using UnityEngine.UI;

namespace DrakesTelemarker
{
    /// <summary>
    /// Minimap pins labeled Mark N. Right-click / Ctrl+left-click recall uses the same cheat gate as console
    /// (<see cref="DrakesTelemarkerPlugin.CanUseTelemarkerCheats"/>), including Server Devcommands admin handling.
    /// </summary>
    internal static class TelemarkerMinimap
    {
        private static MethodInfo? ScreenToWorldPointMethod;
        private static MethodInfo? GetClosestPinMethod;
        private static MethodInfo? CreateMapNamePinMethod;

        private static readonly Dictionary<int, Minimap.PinData> TrackedBySlot = new Dictionary<int, Minimap.PinData>();
        private static readonly Sprite?[] SlotSpriteCache = new Sprite[DrakesTelemarkerPlugin.SlotCount + 1];
        private static DrakesTelemarkerPlugin? _plugin;
        private static Harmony? _harmony;
        private static bool _loggedCreateMapNamePinFailure;


        internal static void Init(DrakesTelemarkerPlugin plugin)
        {
            _plugin = plugin;
            ResolveMinimapReflection();

            _harmony = new Harmony(DrakesTelemarkerPlugin.GUID + ".Minimap");

            MethodInfo? loadMap = FindParameterlessInstanceMethod(typeof(Minimap), "LoadMapData");
            if (loadMap != null)
            {
                _harmony.Patch(loadMap,
                    postfix: new HarmonyMethod(typeof(TelemarkerMinimap), nameof(AfterMinimap_LoadMapData)));
            }
            else
            {
                Debug.LogWarning(
                    $"[{DrakesTelemarkerPlugin.ModName}] Minimap.LoadMapData() not found — pins may not refresh when the map reloads.");
            }

            MethodInfo? mapRight = AccessTools.Method(typeof(Minimap), "OnMapRightClick",
                new[] { typeof(UIInputHandler) });
            if (mapRight != null && ScreenToWorldPointMethod != null)
            {
                _harmony.Patch(mapRight,
                    prefix: new HarmonyMethod(typeof(TelemarkerMinimap), nameof(OnMapRightClick_Prefix)));
            }

            MethodInfo? mapLeftDown = AccessTools.Method(typeof(Minimap), "OnMapLeftDown",
                new[] { typeof(UIInputHandler) });
            if (mapLeftDown != null && ScreenToWorldPointMethod != null)
            {
                _harmony.Patch(mapLeftDown,
                    prefix: new HarmonyMethod(typeof(TelemarkerMinimap), nameof(OnMapLeftDown_Prefix)));
            }

            if (mapRight == null && mapLeftDown == null)
            {
                Debug.LogWarning($"[{DrakesTelemarkerPlugin.ModName}] Could not attach map click hooks — use telemark recall from console.");
            }

            MethodInfo? updatePins = AccessTools.Method(typeof(Minimap), "UpdatePins");
            if (updatePins != null && CreateMapNamePinMethod != null)
            {
                var updatePinsPostfix = new HarmonyMethod(typeof(TelemarkerMinimap), nameof(After_UpdatePins_Postfix))
                {
                    priority = Priority.Last
                };
                _harmony.Patch(updatePins, postfix: updatePinsPostfix);
            }

            plugin.StartCoroutine(DelayedSyncRoutine());
        }

        /// <summary>
        /// Harmony's DeclaredMethod(Type, string, Type[] parameters, Type[] generics) treats the last array as
        /// generic type args — not parameter types. Resolve methods explicitly to avoid TypeInitializationException.
        /// </summary>
        private static void ResolveMinimapReflection()
        {
            ScreenToWorldPointMethod =
                AccessTools.Method(typeof(Minimap), "ScreenToWorldPoint", new[] { typeof(Vector3) });
            GetClosestPinMethod = AccessTools.Method(typeof(Minimap), "GetClosestPin",
                new[] { typeof(Vector3), typeof(float), typeof(bool) });
            CreateMapNamePinMethod = AccessTools.Method(typeof(Minimap), "CreateMapNamePin",
                new[] { typeof(Minimap.PinData), typeof(RectTransform) });
        }

        private static MethodInfo? FindParameterlessInstanceMethod(Type type, string name)
        {
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (MethodInfo m in type.GetMethods(bf))
            {
                if (m.Name == name && m.GetParameters().Length == 0)
                    return m;
            }

            return null;
        }

        private static void AfterMinimap_LoadMapData() => SyncPins();

        private static IEnumerator DelayedSyncRoutine()
        {
            yield return null;
            for (int i = 0; i < 30 && Minimap.instance == null; i++)
                yield return null;
            SyncPins();
        }

        internal static void RequestSync()
        {
            if (_plugin == null)
                return;
            _plugin.StartCoroutine(SyncNextFrame());
        }

        private static IEnumerator SyncNextFrame()
        {
            yield return null;
            SyncPins();
        }

        internal static void SyncPins()
        {
            if (_plugin == null || Minimap.instance == null)
                return;

            Minimap mm = Minimap.instance;
            foreach (Minimap.PinData p in TrackedBySlot.Values)
            {
                if (p != null)
                    mm.RemovePin(p);
            }

            TrackedBySlot.Clear();
            _loggedCreateMapNamePinFailure = false;

            string path = DrakesTelemarkerPlugin.GetMarksFilePath();
            string worldKey = DrakesTelemarkerPlugin.SanitizeWorldKey(DrakesTelemarkerPlugin.GetWorldKey());
            TelemarkerMarksDocument doc = TelemarkerMarksDocument.Load(path);
            long owner = ZNet.GetUID();
            PlatformUserID author = default;

            for (int slot = 1; slot <= DrakesTelemarkerPlugin.SlotCount; slot++)
            {
                if (!doc.TryGetSlot(worldKey, slot, out Vector3 pos, out _))
                    continue;

                string label = $"Mark {slot}";
                Minimap.PinData? pin = mm.AddPin(pos, Minimap.PinType.Icon3, label, false, false, owner, author);
                if (pin == null)
                    continue;

                pin.m_icon = GetOrCreateSlotSprite(slot);
                pin.m_animate = false;
                pin.m_doubleSize = true;
                TrackedBySlot[slot] = pin;
            }
        }

        private static RectTransform ResolvePinNameRoot(Minimap mm)
        {
            if (mm.m_mapLarge.gameObject.activeInHierarchy &&
                mm.m_mapLarge.gameObject.activeSelf)
                return mm.m_pinNameRootLarge;
            return mm.m_pinNameRootSmall;
        }

        private static void After_UpdatePins_Postfix(Minimap __instance)
        {
            if (__instance == null || TrackedBySlot.Count == 0 || CreateMapNamePinMethod == null)
                return;

            RectTransform root = ResolvePinNameRoot(__instance);
            foreach (Minimap.PinData pin in TrackedBySlot.Values)
            {
                if (pin == null || pin.m_uiElement == null)
                    continue;
                if (pin.m_NamePinData != null &&
                    pin.m_NamePinData.PinNameGameObject != null)
                    continue;

                try
                {
                    CreateMapNamePinMethod.Invoke(__instance, new object[] { pin, root });
                }
                catch (Exception ex)
                {
                    if (!_loggedCreateMapNamePinFailure)
                    {
                        _loggedCreateMapNamePinFailure = true;
                        Debug.LogWarning(
                            $"[{DrakesTelemarkerPlugin.ModName}] CreateMapNamePin failed (further errors suppressed): {ex.Message}");
                    }

                    return;
                }
            }
        }

        /// <returns><c>false</c> skips vanilla (we handled recall).</returns>
        private static bool OnMapRightClick_Prefix(Minimap __instance, UIInputHandler handler)
        {
            _ = handler;
            return HandleMapRecallInput(__instance, viaPhrase: "right-click map pin");
        }

        /// <returns><c>false</c> skips vanilla when Ctrl is held and we recall.</returns>
        private static bool OnMapLeftDown_Prefix(Minimap __instance, UIInputHandler handler)
        {
            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
                return true;

            _ = handler;
            return HandleMapRecallInput(__instance, viaPhrase: "Ctrl+click map pin");
        }

        /// <returns>
        /// <c>false</c> if we consumed this map click (successful recall). Vanilla otherwise runs (<c>true</c>).
        /// </returns>
        private static bool HandleMapRecallInput(Minimap minimapInstance, string viaPhrase)
        {
            if (_plugin == null || minimapInstance == null || TrackedBySlot.Count == 0)
                return true;

            if (ScreenToWorldPointMethod == null)
                return true;

            if (!DrakesTelemarkerPlugin.CanUseTelemarkerCheats())
                return true;

            if (!TryGetTrackedSlotUnderCursor(minimapInstance, out int slot))
                return true;

            Player? player = Player.m_localPlayer;
            if (player == null)
                return true;

            if (!_plugin.TryRecallSlot(slot, out Vector3 recallPos, out string err))
            {
                DrakesTelemarkerPlugin.Notify(string.IsNullOrEmpty(err) ? $"Mark {slot} is empty." : err);
                return true;
            }

            player.transform.position = recallPos;
            Physics.SyncTransforms();
            DrakesTelemarkerPlugin.Notify($"Recalled mark {slot} ({viaPhrase}) -> {DrakesTelemarkerPlugin.FormatVec(recallPos)}.");
            return false;
        }

        private static Camera? CanvasUiCamera(RectTransform rt)
        {
            Canvas canvas = rt.GetComponentInParent<Canvas>();
            if (canvas == null)
                return null;

            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        private static float ScreenPivotSlackPixels(RectTransform rt)
        {
            float wPx = Mathf.Abs(rt.rect.width * rt.lossyScale.x);
            float hPx = Mathf.Abs(rt.rect.height * rt.lossyScale.y);
            float shortest = Mathf.Max(24f, Mathf.Min(wPx, hPx));
            return Mathf.Clamp(shortest * 0.65f + 32f, 40f, 140f);
        }

        /// <summary>Prefer rect containment; enlarge hit area slightly using distance to pivot.</summary>
        private static bool TryLooseRectHit(RectTransform rt, Vector2 screen)
        {
            Camera? cam = CanvasUiCamera(rt);
            if (RectTransformUtility.RectangleContainsScreenPoint(rt, screen, cam))
                return true;

            Vector3 sp = RectTransformUtility.WorldToScreenPoint(cam,
                rt.TransformPoint(rt.rect.center));
            float slack = ScreenPivotSlackPixels(rt);
            Vector2 pivot = new Vector2(sp.x, sp.y);
            return (screen - pivot).sqrMagnitude <= slack * slack;
        }

        /// <summary>Resolve which telemark pin the pointer is treating as “closest” among UI rects and worldXZ.</summary>
        private static bool TryGetTrackedSlotUnderCursor(Minimap minimap, out int slot)
        {
            slot = 0;
            Vector3 mouseLegacy = Input.mousePosition;
            Vector2 mouseScreen = mouseLegacy;

            int bestUi = 0;
            float bestSq = float.MaxValue;
            foreach (KeyValuePair<int, Minimap.PinData> kv in TrackedBySlot)
            {
                Minimap.PinData? pin = kv.Value;
                if (pin == null)
                    continue;

                RectTransform?[] candidates =
                [
                    pin.m_iconElement?.rectTransform,
                    pin.m_uiElement,
                    pin.m_NamePinData?.PinNameRectTransform
                ];

                foreach (RectTransform? cand in candidates)
                {
                    if (cand == null)
                        continue;

                    if (!TryLooseRectHit(cand, mouseScreen))
                        continue;

                    Camera? cam = CanvasUiCamera(cand);
                    Vector3 sp = RectTransformUtility.WorldToScreenPoint(cam,
                        cand.TransformPoint(cand.rect.center));
                    float sq = (new Vector2(sp.x, sp.y) - mouseScreen).sqrMagnitude;
                    if (sq >= bestSq)
                        continue;

                    bestSq = sq;
                    bestUi = kv.Key;
                }
            }

            if (bestUi != 0)
            {
                slot = bestUi;
                return true;
            }

            MethodInfo? swRef = ScreenToWorldPointMethod;
            if (swRef == null)
                return false;

            Vector3 wp;
            try
            {
                object? rw = swRef.Invoke(minimap, new object[] { mouseLegacy });
                if (rw is not Vector3 ww)
                    return false;
                wp = ww;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{DrakesTelemarkerPlugin.ModName}] ScreenToWorldPoint reflex failed: {ex.Message}");
                return false;
            }

            MethodInfo? gcp = GetClosestPinMethod;
            if (gcp != null)
            {
                try
                {
                    float probe = Mathf.Max(minimap.m_removeRadius * 48f, 640f);
                    foreach (bool publicOnlyFlag in new[] { false, true })
                    {
                        object? o = gcp.Invoke(minimap, new object[] { wp, probe, publicOnlyFlag });
                        if (o is not Minimap.PinData hit)
                            continue;

                        foreach (KeyValuePair<int, Minimap.PinData> kv in TrackedBySlot)
                        {
                            if (ReferenceEquals(kv.Value, hit))
                            {
                                slot = kv.Key;
                                return true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[{DrakesTelemarkerPlugin.ModName}] GetClosestPin reflex failed: {ex.Message}");
                }
            }

            float threshWorld = Mathf.Max(minimap.m_removeRadius * 40f, 450f);
            float threshSq = threshWorld * threshWorld;
            float bestDxzSq = float.PositiveInfinity;
            int pick = 0;
            foreach (KeyValuePair<int, Minimap.PinData> kv in TrackedBySlot)
            {
                if (kv.Value == null)
                    continue;
                Vector3 p = kv.Value.m_pos;
                float dx = wp.x - p.x;
                float dz = wp.z - p.z;
                float dxzSq = dx * dx + dz * dz;
                if (dxzSq < bestDxzSq)
                {
                    bestDxzSq = dxzSq;
                    pick = kv.Key;
                }
            }

            if (pick == 0 || bestDxzSq > threshSq)
                return false;

            slot = pick;
            return true;
        }

        private static Sprite GetOrCreateSlotSprite(int slot)
        {
            if (slot < 1 || slot > DrakesTelemarkerPlugin.SlotCount)
                slot = Mathf.Clamp(slot, 1, DrakesTelemarkerPlugin.SlotCount);

            Sprite? cached = SlotSpriteCache[slot];
            if (cached != null)
                return cached;

            Texture2D tex = TelemarkerPinTexture.Build(slot);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;

            Sprite created = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.12f),
                Mathf.Max(tex.width / 120f * 96f, 48f));

            SlotSpriteCache[slot] = created;
            return created;
        }
    }

    internal static class TelemarkerPinTexture
    {
        private const int Res = 128;

        /// <summary>Glyphs are 5×5, top row first (<c>Glyphs[row][col]</c>).</summary>
        private static readonly string[][] DigitRows =
        {
            new[] { "01110", "10001", "10001", "10001", "01110" }, // 0
            new[] { "00100", "01100", "00100", "00100", "01110" }, // 1
            new[] { "01110", "10001", "00110", "01000", "11111" }, // 2
            new[] { "11110", "00001", "01110", "00001", "11110" }, // 3
            new[] { "10001", "10001", "11111", "00001", "00001" }, // 4
            new[] { "11111", "10000", "11110", "00001", "11110" }, // 5
            new[] { "01110", "10000", "11110", "10001", "01110" }, // 6
            new[] { "11111", "00001", "00010", "00100", "01000" }, // 7
            new[] { "01110", "10001", "01110", "10001", "01110" }, // 8
            new[] { "01110", "10001", "01111", "00001", "01110" } // 9
        };

        public static Texture2D Build(int slotNumber)
        {
            var tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false);
            tex.name = $"TelemarkerPin_{slotNumber}";
            Color32[] pix = new Color32[Res * Res];

            const float cx = 0.5f;
            const float cyHead = 0.74f;
            const float headRx = 0.19f;
            const float headRy = 0.24f;
            const float holeRx = 0.1f;
            const float holeRy = 0.11f;

            Vector2 tip = new Vector2(0.5f, 0.06f);
            Vector2 shoulderL = new Vector2(cx - headRx * 0.78f, 0.52f);
            Vector2 shoulderR = new Vector2(cx + headRx * 0.78f, 0.52f);

            int holePx = Mathf.RoundToInt(cx * (Res - 1));
            int holePy = Mathf.RoundToInt(cyHead * (Res - 1));
            int holeRadPx = Mathf.RoundToInt(Res * 0.09f);

            for (int py = 0; py < Res; py++)
            {
                float uy = py / (Res - 1f);
                for (int px = 0; px < Res; px++)
                {
                    float ux = px / (Res - 1f);
                    bool inHead = InsideEllipse(ux, uy, cx, cyHead, headRx, headRy);
                    bool inTail = InsideTriangle(new Vector2(ux, uy), tip, shoulderL, shoulderR);
                    bool hull = inHead || inTail;
                    bool inHole = InsideEllipse(ux, uy, cx, cyHead, holeRx, holeRy);

                    if (!hull)
                    {
                        pix[py * Res + px] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    if (inHole)
                    {
                        pix[py * Res + px] = new Color32(32, 38, 48, 255);
                        continue;
                    }

                    byte shade = ux < cx ? (byte)255 : (byte)228;
                    bool edge = OutlineSample(ux, uy, cx, cyHead, headRx, headRy, tip, shoulderL, shoulderR);
                    byte brighten = shade >= 247 ? shade : (byte)(shade + 8);
                    pix[py * Res + px] =
                        edge ? new Color32(200, 200, 210, 255) : new Color32(shade, shade, brighten, 255);
                }
            }

            BlitDigits(pix, holePx, holePy, holeRadPx, slotNumber);

            tex.SetPixels32(pix);
            tex.Apply(false, false);
            return tex;
        }

        private static bool OutlineSample(float ux, float uy, float cx, float cyHead, float headRx, float headRy,
            Vector2 tip, Vector2 shoulderL, Vector2 shoulderR)
        {
            const float s = 1f / (Res - 1);
            bool center = InsidePin(ux, uy, cx, cyHead, headRx, headRy, tip, shoulderL, shoulderR);
            if (!center)
                return false;
            foreach (float dx in new[] { -s, 0f, s })
            foreach (float dy in new[] { -s, 0f, s })
            {
                if (Mathf.Abs(dx) < 0.0001f && Mathf.Abs(dy) < 0.0001f)
                    continue;
                if (!InsidePin(ux + dx, uy + dy, cx, cyHead, headRx, headRy, tip, shoulderL, shoulderR))
                    return true;
            }

            return false;
        }

        private static bool InsidePin(float ux, float uy, float cx, float cyHead, float headRx, float headRy,
            Vector2 tip, Vector2 shoulderL, Vector2 shoulderR) =>
            InsideEllipse(ux, uy, cx, cyHead, headRx, headRy) ||
            InsideTriangle(new Vector2(ux, uy), tip, shoulderL, shoulderR);

        private static bool InsideEllipse(float ux, float uy, float cx, float cy, float halfW, float halfH)
        {
            float dx = (ux - cx) / halfW;
            float dy = (uy - cy) / halfH;
            return dx * dx + dy * dy <= 1f;
        }

        private static bool InsideTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float Sign(Vector2 p1, Vector2 p2, Vector2 p3) =>
                (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);
            bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
            bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(hasNeg && hasPos);
        }

        private static void BlitDigits(Color32[] pix, int holeCx, int holeCy, int holeR, int value)
        {
            string label = Mathf.Clamp(value, 1, DrakesTelemarkerPlugin.SlotCount)
                .ToString(CultureInfo.InvariantCulture);

            float scale = label.Length >= 2 ? 1.35f : 1f;
            int gw = Mathf.Max(8, Mathf.RoundToInt(holeR * scale * 0.55f));
            int gh = Mathf.Max(10, Mathf.RoundToInt(holeR * scale * 0.62f));

            int totalW = gw * label.Length + Mathf.Max(0, label.Length - 1);
            int left = holeCx - totalW / 2;
            int oy = holeCy - gh / 2;

            foreach (char ch in label)
            {
                DrawGlyph(pix, left, oy, gw, gh, ch);
                left += gw + 1;
            }
        }

        private static void DrawGlyph(Color32[] pix, int ox, int oy, int gw, int gh, char digit)
        {
            if (!char.IsDigit(digit))
                return;
            int idx = digit - '0';
            string[] rows = DigitRows[idx];
            int rowPx = Mathf.Max(1, gh / 5);

            for (int row = 0; row < 5 && row < rows.Length; row++)
            {
                string slice = rows[row];
                int colPx = Mathf.Max(1, gw / 5);
                for (int col = 0; col < 5 && col < slice.Length; col++)
                {
                    if (slice[col] != '1')
                        continue;

                    int x0 = ox + col * colPx;
                    int py0 = oy + gh - (row + 1) * rowPx;

                    for (int py = py0; py < py0 + rowPx && py < Res; py++)
                    {
                        if (py < 0)
                            continue;
                        for (int px = x0; px < x0 + colPx && px < Res; px++)
                        {
                            if (px < 0)
                                continue;
                            pix[py * Res + px] = new Color32(246, 248, 255, 255);
                        }
                    }
                }
            }
        }
    }
}
