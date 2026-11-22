#if !DORSTH_BUNDLE   // ezCrosshair's own window: MONOLITH has its menu instead
// ezCrosshair - a STRAFTAT mod (BepInEx 5). Dorsth was here.
//
// The crosshair menu, built the same way as the STRAFTH menu and in its colors. Pick a
// crosshair from the pictures (hover one for its name), a color from the swatches or as a hex code, the size, and the
// key that opens the menu. Changes show right away, in the preview and on screen.

using System;
using System.Collections.Generic;
using BepInEx;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EzCrosshair
{
    internal sealed class Menu : MonoBehaviour
    {
        public static Menu I;
        public static bool IsOpen { get { return I != null && I._open; } }

        private const float W = 748f, H = 636f, Pad = 28f;
        // the top section: captions on one line, then the preview, the crosshairs and the colors under them, the preview
        // as tall as the crosshairs and colors together
        private const float CapY = 84f, TopY = 108f, ColorCapY = 248f, ColorY = 272f, RowsY = 336f;
        // the crosshair tiles fill the width from GridX to the window's right padding, like the color row and the rows
        // under it; a little shorter than they are wide
        private const float GridX = 272f, Gap = 8f, TileH = 56f;
        private const float TileW = (W - Pad - GridX - (Columns - 1) * Gap) / Columns;
        private const int Columns = 6;

        // the main colors: white (keeps the crosshair's own colors), red, yellow, green, cyan, blue, magenta
        private static readonly Color32[] Swatches =
        {
            new Color32(255, 255, 255, 255), new Color32(255, 0, 0, 255), new Color32(255, 255, 0, 255), new Color32(0, 255, 0, 255),
            new Color32(0, 255, 255, 255), new Color32(0, 0, 255, 255), new Color32(255, 0, 255, 255)
        };

        private bool _open, _pauseBefore, _rebinding;
        private Canvas _canvas;
        private RawImage _preview;
        private TextBox _scaleBox;
        private RectTransform _tip;
        private TextMeshProUGUI _tipText;

        private void Awake() { I = this; }

        private void Update()
        {
            if (_rebinding) { WaitForKey(); return; }
            // the escape that closed the menu is over once the key is up
            if (escapeHeld) _ = SwallowEscape;
            // escape closes the menu, unless it's stopping a text field
            if (_open && KeyDown(KeyCode.Escape) && !Typing() && Time.frameCount - TextBox.LastTypingFrame > 1)
            {
                escapeFrame = Time.frameCount;
                escapeHeld = true;
                Close();
                return;
            }
            if (KeyDown(EzCrosshairPlugin.MenuKey.Value) && !Typing())
            {
                if (_open) Close();
                else Open();
            }
        }

        // The escape press that closed the menu doesn't also reach the game's own menus (the pause menu in a map, the
        // menu panel on the home screen). Only that press: once the key is up, the next escape opens them right away.
        private static int escapeFrame = -10;
        private static bool escapeHeld;

        public static bool SwallowEscape
        {
            get
            {
                if (!escapeHeld) return false;
                if (Time.frameCount - escapeFrame <= 1) return true;
                bool down;
                try { down = UnityInput.Current.GetKey(KeyCode.Escape); } catch { down = Input.GetKey(KeyCode.Escape); }
                if (!down) escapeHeld = false;
                return down;
            }
        }

        // ---- where the window is: moved by its top bar, kept on screen, remembered

        private static Vector2? spot;

        private static Vector2 Spot()
        {
            if (spot.HasValue) return spot.Value;
            var v = Vector2.zero;
            var parts = (EzCrosshairPlugin.MenuSpot.Value ?? "").Split(',');
            if (parts.Length == 2)
            {
                float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v.x);
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v.y);
            }
            spot = v;
            return v;
        }

        private static void SaveSpot(Vector2 p)
        {
            spot = p;
            EzCrosshairPlugin.MenuSpot.Value = Mathf.RoundToInt(p.x).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + Mathf.RoundToInt(p.y).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // the whole window stays on the screen (the canvas is 1080 high, as wide as the screen's shape makes it)
        private static Vector2 ClampSpot(Vector2 p)
        {
            float canvasW = 1080f * Screen.width / Mathf.Max(1f, Screen.height);
            float hw = Mathf.Max(0f, (canvasW - W) / 2f), hh = Mathf.Max(0f, (1080f - H) / 2f);
            return new Vector2(Mathf.Clamp(p.x, -hw, hw), Mathf.Clamp(p.y, -hh, hh));
        }

        private void LateUpdate()
        {
            if (!_open) return;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        public void Open()
        {
            if (!_open)
            {
                _open = true;
                var pm = PauseManager.Instance;
                _pauseBefore = pm != null && pm.pause;
                // the game ignores your mouse and keys while it thinks it's paused
                if (pm != null) pm.pause = true;
                Ui.Prepare();
                Ui.EnsureEventSystem();
                // STRAFTH's menu colors, if it's installed (picked up again each time the menu opens)
                Ui.LoadColors();
            }
            Build();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _rebinding = false;
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = null;
            Blur.DrawMenu(null);
            var pm = PauseManager.Instance;
            if (pm != null) pm.pause = _pauseBefore;
        }


        private void Rebuild() { if (_open) Build(); }

        private void Build()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = Ui.MakeCanvas("ezCrosshair Menu", 32001);
            Blur.DrawMenu(_canvas);
            var root = (RectTransform)_canvas.transform;

            // an invisible layer over the game that catches clicks (the game isn't dimmed), then the window
            var dim = Ui.Panel(root, new Color(0f, 0f, 0f, 0f));
            Ui.Fill(dim.rectTransform);
            dim.raycastTarget = true;
            // see-through and blurred everywhere, like the STRAFTH menu
            var win = Ui.Window(root);
            var w = win.rectTransform;
            w.anchorMin = w.anchorMax = new Vector2(0.5f, 0.5f);
            w.sizeDelta = new Vector2(W, H);
            w.anchoredPosition = ClampSpot(Spot());
            win.raycastTarget = true;
            // the top bar moves the window
            var dragBar = Ui.Panel(w, new Color(0f, 0f, 0f, 0f));
            dragBar.raycastTarget = true;
            Ui.At(dragBar.rectTransform, 0f, 0f, W, 72f);
            var drag = dragBar.gameObject.AddComponent<WindowDrag>();
            drag.Window = w;
            drag.Clamp = ClampSpot;
            drag.Moving = p => spot = p;
            drag.Moved = SaveSpot;

            // header: the name, close on the right
            var title = Ui.Label(w, "ezCrosshair", 26f, Ui.Txt);
            Ui.At(title.rectTransform, Pad, 16f, 400f, 40f);
            var close = Ui.Btn(w, "x", 20f, Close);
            Ui.At((RectTransform)close.transform, W - 64f, 16f, 40f, 40f);
            Ui.Center(close);

            Preview(w);
            Pictures(w);
            Colors(w);

            float y = RowsY;
            Row(w, ref y, "scale", ScaleControl);
            Row(w, ref y, "show while scoped", r => Switch(r, EzCrosshairPlugin.ShowScoped.Value, v => EzCrosshairPlugin.ShowScoped.Value = v));
            Row(w, ref y, "use this crosshair", r => Switch(r, EzCrosshairPlugin.On.Value, v => EzCrosshairPlugin.On.Value = v));
            Row(w, ref y, "hit color", HitControl);
            Row(w, ref y, "menu key", r =>
            {
                var b = Ui.Btn(r, _rebinding ? "press a key" : KeyLabel(EzCrosshairPlugin.MenuKey.Value), 20f, () => { _rebinding = true; Rebuild(); }, _rebinding);
                Ui.At((RectTransform)b.transform, W - Pad * 2f - 220f, 4f, 220f, 40f);
                Ui.Center(b);
            });

            // last on the window: makes it solid in the menu's picture (see Blur.AlphaOnly)
            var seal = Ui.Panel(w, Color.white, Ui.Round);
            seal.material = Blur.AlphaOnly();
            Ui.Fill(seal.rectTransform);

            // the name of the crosshair under the cursor, next to it
            var tip = Ui.Panel(root, Ui.A(Ui.Mix(0.16f), 0.95f), Ui.RoundSmall);
            Ui.Edge(tip, Ui.Tint(0.3f));
            _tip = tip.rectTransform;
            _tip.anchorMin = _tip.anchorMax = Vector2.zero;
            _tip.pivot = new Vector2(0f, 1f);
            _tipText = Ui.Label(_tip, "", 16f, Ui.Txt, TextAlignmentOptions.Center);
            _tipText.enableWordWrapping = false;
            Ui.Fill(_tipText.rectTransform);
            _tip.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------- parts

        // The crosshair as it looks in the game, at its real size, on the same backdrop as the rows.
        private void Preview(RectTransform w)
        {
            var cap = Ui.Label(w, "preview", 14f, Ui.Dim);
            Ui.At(cap.rectTransform, Pad, CapY, 220f, 20f);
            // the same backdrop as the rows under it
            var box = Ui.Panel(w, Ui.Tint(0.06f), Ui.RoundSmall);
            Ui.At(box.rectTransform, Pad, TopY, GridX - Pad - 24f, ColorY + 40f - TopY);
            Texture2D tex = global::EzCrosshair.Pictures.At(EzCrosshairPlugin.Pick.Value, EzCrosshairPlugin.Scale.Value);
            if (tex == null) return;
            var pic = Ui.Node("preview", box.transform).gameObject.AddComponent<RawImage>();
            pic.texture = tex;
            pic.color = EzCrosshairPlugin.Tint.Value;
            pic.raycastTarget = false;
            var rt = pic.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            _preview = pic;
            SizePreview();
        }

        // the same number of screen pixels the crosshair covers in the game (the canvas is scaled to a 1080 high
        // screen, and snaps to whole pixels, drawn without smoothing like the crosshair itself)
        private void SizePreview()
        {
            if (_preview == null) return;
            _preview.texture = global::EzCrosshair.Pictures.At(EzCrosshairPlugin.Pick.Value, EzCrosshairPlugin.Scale.Value);
            if (_preview.texture == null) return;
            Vector2Int px = new Vector2Int(_preview.texture.width, _preview.texture.height);
            float factor = Mathf.Max(0.01f, Screen.height / 1080f);
            _preview.rectTransform.sizeDelta = new Vector2(px.x, px.y) / factor;
        }

        // scale: a slider from 0 to 2, and the number, which you can type in
        private void ScaleControl(RectTransform r)
        {
            float right = W - Pad * 2f;
            var slider = Ui.MakeSlider(r, 0f, 2f, EzCrosshairPlugin.Scale.Value, v =>
            {
                v = Mathf.Round(v * 20f) / 20f;
                if (Mathf.Abs(v - EzCrosshairPlugin.Scale.Value) < 0.001f) return;
                EzCrosshairPlugin.Scale.Value = v;
                if (_scaleBox != null && !_scaleBox.isFocused) _scaleBox.SetTextWithoutNotify(v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                SizePreview();
            });
            Ui.At((RectTransform)slider.transform, right - 360f, 12f, 240f, 24f);
            string shown = EzCrosshairPlugin.Scale.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            TextBox box = null;
            box = TextBox.Make(r, right - 100f, 4f, 92f, 40f, new TextBox.Options
            {
                Text = shown,
                Limit = 4,
                Filter = c => char.IsDigit(c) || c == '.' ? c : c == ',' ? '.' : '\0',
                Left = text =>
                {
                    float v;
                    if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                    {
                        v = Mathf.Clamp(v, 0f, 2f);
                        if (Mathf.Abs(v - EzCrosshairPlugin.Scale.Value) > 0.0001f) { EzCrosshairPlugin.Scale.Value = v; Rebuild(); return; }
                    }
                    if (box != null) box.SetTextWithoutNotify(shown);
                },
            });
            _scaleBox = box;
        }

        // Every crosshair as a picture: click one to use it, hover it for its name.
        private void Pictures(RectTransform w)
        {
            var cap = Ui.Label(w, "crosshairs", 14f, Ui.Dim);
            Ui.At(cap.rectTransform, GridX, CapY, 300f, 20f);
            string[] names = EzCrosshairPlugin.Names;
            Color tint = EzCrosshairPlugin.Tint.Value;
            // all at one size, so they compare like they do in the game: as big as the biggest one fits, in whole pixels
            int biggest = 1;
            foreach (string n in names)
            {
                Texture2D t = global::EzCrosshair.Pictures.Get(n);
                if (t != null) { Vector2 own = global::EzCrosshair.Pictures.Native(t); biggest = Mathf.Max(biggest, Mathf.CeilToInt(Mathf.Max(own.x, own.y))); }
            }
            int k = Mathf.Max(1, Mathf.FloorToInt((TileH - 12f) / biggest));
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                bool picked = name == EzCrosshairPlugin.Pick.Value;
                float x = GridX + (i % Columns) * (TileW + Gap), y = TopY + (i / Columns) * (TileH + Gap);
                if (picked)
                {
                    var hl = Ui.Accent(w);
                    Ui.At(hl.rectTransform, x, y, TileW, TileH);
                }
                var b = Ui.Btn(w, null, 0f, () => { EzCrosshairPlugin.Pick.Value = name; Rebuild(); });
                Ui.At((RectTransform)b.transform, x, y, TileW, TileH);
                b.gameObject.AddComponent<Hover>().Text = name;
                Texture2D tex = global::EzCrosshair.Pictures.Get(name);
                if (tex == null) continue;
                var pic = Ui.Node("picture", b.transform).gameObject.AddComponent<RawImage>();
                pic.texture = tex;
                pic.color = tint;
                pic.raycastTarget = false;
                var rt = pic.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = global::EzCrosshair.Pictures.Native(tex) * k;
            }
        }

        // A few colors to click, and a hex code for any other.
        private void Colors(RectTransform w)
        {
            var cap = Ui.Label(w, "color", 14f, Ui.Dim);
            Ui.At(cap.rectTransform, GridX, ColorCapY, 300f, 20f);
            Color32 now = EzCrosshairPlugin.Tint.Value;
            float x = GridX, y = ColorY;
            foreach (Color32 c in Swatches)
            {
                Color32 col = c;
                bool picked = now.r == c.r && now.g == c.g && now.b == c.b;
                if (picked)
                {
                    var ring = Ui.Panel(w, Ui.Txt, Ui.RoundSmallRing);
                    Ui.At(ring.rectTransform, x - 4f, y - 4f, 48f, 48f);
                }
                var sw = Ui.Panel(w, col, Ui.RoundSmall);
                sw.raycastTarget = true;
                Ui.At(sw.rectTransform, x, y, 40f, 40f);
                var b = sw.gameObject.AddComponent<Button>();
                b.targetGraphic = null;
                b.navigation = new Navigation { mode = Navigation.Mode.None };
                b.onClick.AddListener(() => { EzCrosshairPlugin.Tint.Value = (Color)col; Rebuild(); });
                x += 48f;
            }
            // the color as a hex code: type or paste one, it's used once it has 6 digits
            string good = ColorUtility.ToHtmlStringRGB(EzCrosshairPlugin.Tint.Value).ToLowerInvariant();
            TextBox hex = null;
            hex = TextBox.Make(w, x + 8f, y, W - Pad - (x + 8f), 40f, new TextBox.Options
            {
                Text = good,
                Prefix = "#",
                Limit = 6,
                Key = "crosshair color",
                Filter = c => Uri.IsHexDigit(c) ? char.ToLowerInvariant(c) : '\0',
                Changed = v =>
                {
                    Color c;
                    if (v.Length != 6 || v == good || !ColorUtility.TryParseHtmlString("#" + v, out c)) return;
                    good = v;
                    EzCrosshairPlugin.Tint.Value = c;
                    // built again with the new color; the field keeps its cursor (Key)
                    Rebuild();
                },
                Left = v => { if (v.Length != 6 && hex != null) hex.SetTextWithoutNotify(good); },
            });
        }

        // the hit color: on / off, and its color as a hex code with a swatch of it
        private void HitControl(RectTransform r)
        {
            Switch(r, EzCrosshairPlugin.HitOn.Value, v => EzCrosshairPlugin.HitOn.Value = v);
            float right = W - Pad * 2f;
            var sw = Ui.Panel(r, EzCrosshairPlugin.HitTint.Value, Ui.RoundSmall);
            Ui.At(sw.rectTransform, right - 180f - 8f - 150f - 8f - 40f, 4f, 40f, 40f);
            string good = ColorUtility.ToHtmlStringRGB(EzCrosshairPlugin.HitTint.Value).ToLowerInvariant();
            TextBox hex = null;
            hex = TextBox.Make(r, right - 180f - 8f - 150f, 4f, 150f, 40f, new TextBox.Options
            {
                Text = good,
                Prefix = "#",
                Limit = 6,
                Key = "hit color",
                Filter = c => Uri.IsHexDigit(c) ? char.ToLowerInvariant(c) : '\0',
                Changed = v =>
                {
                    Color c;
                    if (v.Length != 6 || v == good || !ColorUtility.TryParseHtmlString("#" + v, out c)) return;
                    good = v;
                    EzCrosshairPlugin.HitTint.Value = c;
                    Rebuild();
                },
                Left = v => { if (v.Length != 6 && hex != null) hex.SetTextWithoutNotify(good); },
            });
        }

        // one setting on its own darker bar, its control on the right
        private static void Row(RectTransform w, ref float y, string label, Action<RectTransform> control)
        {
            var bar = Ui.Panel(w, Ui.Tint(0.06f), Ui.RoundSmall);
            Ui.At(bar.rectTransform, Pad, y, W - Pad * 2f, 48f);
            var t = Ui.Label(bar.transform, label, 22f, Ui.Txt);
            t.enableWordWrapping = false;
            Ui.At(t.rectTransform, 16f, 0f, 400f, 48f);
            control(bar.rectTransform);
            y += 56f;
        }

        // on / off, the one that's on filled in
        private void Switch(RectTransform r, bool on, Action<bool> set)
        {
            float right = W - Pad * 2f;
            var off = Ui.Btn(r, "off", 18f, () => { set(false); Rebuild(); }, !on);
            Ui.At((RectTransform)off.transform, right - 180f, 4f, 84f, 40f);
            Ui.Center(off);
            var yes = Ui.Btn(r, "on", 18f, () => { set(true); Rebuild(); }, on);
            Ui.At((RectTransform)yes.transform, right - 92f, 4f, 84f, 40f);
            Ui.Center(yes);
        }

        // ---------------------------------------------------------- the name tip

        public void ShowTip(string text, Vector2 at)
        {
            if (_tip == null) return;
            _tipText.text = text;
            float wdt = _tipText.GetPreferredValues(text).x + 28f;
            _tip.sizeDelta = new Vector2(wdt, 32f);
            _tip.gameObject.SetActive(true);
            _tip.SetAsLastSibling();
            MoveTip(at);
        }

        public void MoveTip(Vector2 at)
        {
            if (_tip == null) return;
            float f = Mathf.Max(0.01f, Screen.height / 1080f);
            _tip.anchoredPosition = at / f + new Vector2(16f, -16f);
        }

        public void HideTip() { if (_tip != null) _tip.gameObject.SetActive(false); }

        // ---------------------------------------------------------- keys

        private static KeyCode[] _allKeys;

        private void WaitForKey()
        {
            if (KeyDown(KeyCode.Escape)) { _rebinding = false; Rebuild(); return; }
            if (_allKeys == null) _allKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));
            foreach (KeyCode k in _allKeys)
            {
                if (k == KeyCode.None || k == KeyCode.Escape || (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6) || k >= KeyCode.JoystickButton0) continue;
                if (!KeyDown(k)) continue;
                EzCrosshairPlugin.MenuKey.Value = k;
                _rebinding = false;
                Rebuild();
                return;
            }
        }

        private static bool KeyDown(KeyCode k)
        {
            try { return UnityInput.Current.GetKeyDown(k); }
            catch { return Input.GetKeyDown(k); }
        }

        private static string KeyLabel(KeyCode k)
        {
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return ((int)(k - KeyCode.Alpha0)).ToString();
            if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) return "num " + (int)(k - KeyCode.Keypad0);
            return k.ToString().ToLowerInvariant();
        }

        // typing in chat or a text box: the key is a letter then
        private static bool Typing()
        {
            var es = EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go != null)
            {
                var tmp = go.GetComponent<TMP_InputField>();
                if (tmp != null && tmp.isFocused) return true;
                var ui = go.GetComponent<InputField>();
                if (ui != null && ui.isFocused) return true;
            }
            var pm = PauseManager.Instance;
            return pm != null && pm.chatting && !Overlay.InMainMenu();
        }
    }

    /// <summary>Shows the crosshair's name next to the cursor while it's over its picture.</summary>
    internal sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        public string Text;
        public void OnPointerEnter(PointerEventData e) { if (Menu.I != null) Menu.I.ShowTip(Text, e.position); }
        public void OnPointerMove(PointerEventData e) { if (Menu.I != null) Menu.I.MoveTip(e.position); }
        public void OnPointerExit(PointerEventData e) { if (Menu.I != null) Menu.I.HideTip(); }
    }

    // The menu's raycaster: its clicks come before the game's own menus under it.
    internal sealed class TopRaycaster : GraphicRaycaster
    {
        public override int sortOrderPriority { get { return int.MaxValue; } }
        public override int renderOrderPriority { get { return int.MaxValue; } }
    }

    // The window's top bar: drag it to move the window (never past the edge of the screen), double-click it (without
    // moving it) to put the window back in the middle.
    internal sealed class WindowDrag : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public RectTransform Window;
        public Func<Vector2, Vector2> Clamp;
        public Action<Vector2> Moving, Moved;
        private static bool dragged;
        private static float lastDrag = -10f;

        public void OnPointerDown(PointerEventData e) { dragged = false; }
        public void OnBeginDrag(PointerEventData e) { dragged = true; }

        public void OnDrag(PointerEventData e)
        {
            var c = Window.GetComponentInParent<Canvas>();
            float scale = c != null && c.rootCanvas.scaleFactor > 0f ? c.rootCanvas.scaleFactor : 1f;
            Window.anchoredPosition = Clamp(Window.anchoredPosition + e.delta / scale);
            Moving(Window.anchoredPosition);
        }

        public void OnEndDrag(PointerEventData e)
        {
            lastDrag = Time.unscaledTime;
            Moved(Window.anchoredPosition);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.clickCount < 2 || dragged || Time.unscaledTime - lastDrag < 0.5f) return;
            Window.anchoredPosition = Vector2.zero;
            Moved(Vector2.zero);
        }
    }

    /// <summary>The STRAFTH menu's look: its colors, uneven rounded corners, font and buttons.</summary>
    internal static class Ui
    {
        public static TMP_FontAsset Font;
        public static FontStyles Style = FontStyles.Bold | FontStyles.Italic;
        public static Sprite Round, RoundSmall, RoundRing, RoundSmallRing;
        // The colors, the same system as the STRAFTH menu: the window (glass) is the background color, everything on
        // it the text color: text as is, dimmer text mixed toward the background (Mix, Dim), every fill the text color
        // see-through (Tint). The two colors are STRAFTH's menu colors when it's installed (read each time the menu
        // opens, so a change there shows here too), otherwise STRAFTH's default mint on dark olive.
        public static readonly Color MintTxt = new Color32(0xb5, 0xf7, 0xb7, 0xff), MintBg = new Color32(0x15, 0x16, 0x10, 0xff),
            MintGlass = new Color32(0x0c, 0x0d, 0x09, 0xff);
        public static Color Txt = MintTxt, Bg = MintBg, GlassColor = MintGlass;
        public const float GlassAlpha = 0.66f;
        public static Color Mix(float k) { return Color.Lerp(Bg, Txt, k); }
        public static Color A(Color c, float a) { c.a = a; return c; }
        public static Color Tint(float k) { return A(Txt, k); }
        public static Color Dim { get { return Mix(0.66f); } }

        private const string StrafthConfig = "dorsth.straftat.strafth.cfg";

        // the text on a filled button: the background color, unless the two colors are too close to tell apart
        // (black on black, say), then white or black, whichever reads on the fill
        public static Color OnFill
        {
            get
            {
                if (Mathf.Abs(Txt.grayscale - Bg.grayscale) >= 0.35f) return Bg;
                return Txt.grayscale < 0.5f ? Color.white : Color.black;
            }
        }

        public static void LoadColors()
        {
            string text = "B5F7B7", back = "151610";
            try
            {
                string path = System.IO.Path.Combine(Paths.ConfigPath, StrafthConfig);
                if (System.IO.File.Exists(path))
                {
                    foreach (string line in System.IO.File.ReadAllLines(path))
                    {
                        int eq = line.IndexOf('=');
                        if (eq < 0 || line.TrimStart().StartsWith("#")) continue;
                        string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim().TrimStart('#');
                        if (val.Length != 6) continue;
                        if (key == "Menu color") text = val;
                        else if (key == "Menu background") back = val;
                    }
                }
            }
            catch { }
            Color t, b;
            if (!ColorUtility.TryParseHtmlString("#" + text, out t)) t = MintTxt;
            if (!ColorUtility.TryParseHtmlString("#" + back, out b)) b = MintBg;
            t.a = b.a = 1f;
            Txt = t;
            Bg = b;
            bool mint = Mathf.Abs(b.r - MintBg.r) + Mathf.Abs(b.g - MintBg.g) + Mathf.Abs(b.b - MintBg.b) < 0.01f;
            GlassColor = mint ? MintGlass : new Color(b.r * 0.6f, b.g * 0.6f, b.b * 0.6f, 1f);
        }

        public static void Prepare()
        {
            if (Round == null) Round = RoundSprite(10);
            if (RoundSmall == null) RoundSmall = RoundSprite(5);
            if (RoundRing == null) RoundRing = RoundSprite(10, 1.5f);
            if (RoundSmallRing == null) RoundSmallRing = RoundSprite(5, 1.25f);
            FindFont();
        }

        // the game's Century Gothic, bold italic if it's there
        private static void FindFont()
        {
            if (Font != null) return;
            TMP_FontAsset[] all;
            try { all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>(); } catch { all = new TMP_FontAsset[0]; }
            int best = 0;
            foreach (var f in all)
            {
                if (f == null) continue;
                string n = f.name.ToLowerInvariant();
                int s = 0;
                if (n.Contains("gothic") || n.Contains("century")) s = 2;
                if (n.Contains("gothicbi")) s = 5;
                else if (s > 0 && n.Contains("bold") && n.Contains("italic")) s = 4;
                else if (s > 0 && (n.Contains("bold") || n.Contains("italic"))) s = 3;
                if (s > best) { best = s; Font = f; }
            }
            if (Font != null)
            {
                string n = Font.name.ToLowerInvariant();
                Style = FontStyles.Normal;
                if (!n.Contains("gothicbi"))
                {
                    if (!n.Contains("italic")) Style |= FontStyles.Italic;
                    if (!n.Contains("bold")) Style |= FontStyles.Bold;
                }
            }
            if (Font == null) Font = TMP_Settings.defaultFontAsset;
        }

        // Rounded corners, a little uneven like they were shaped by hand (the same shape as STRAFTH's panels).
        private static readonly float[] CornerW = { 1.14f, 0.86f, 1.21f, 0.93f };
        private static readonly float[] CornerH = { 0.93f, 1.14f, 0.86f, 1.07f };

        private static Sprite RoundSprite(int r, float ring = 0f)
        {
            float tlw = r * CornerW[0], trw = r * CornerW[1], brw = r * CornerW[2], blw = r * CornerW[3];
            float tlh = r * CornerH[0], trh = r * CornerH[1], brh = r * CornerH[2], blh = r * CornerH[3];
            int left = Mathf.CeilToInt(Mathf.Max(tlw, blw)), right = Mathf.CeilToInt(Mathf.Max(trw, brw));
            int top = Mathf.CeilToInt(Mathf.Max(tlh, trh)), bottom = Mathf.CeilToInt(Mathf.Max(blh, brh));
            int w = left + right + 4, h = top + bottom + 4;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float inside;
                    if (fx < tlw && fy > h - tlh) inside = InEllipse(fx - tlw, fy - (h - tlh), tlw, tlh);
                    else if (fx > w - trw && fy > h - trh) inside = InEllipse(fx - (w - trw), fy - (h - trh), trw, trh);
                    else if (fx > w - brw && fy < brh) inside = InEllipse(fx - (w - brw), fy - brh, brw, brh);
                    else if (fx < blw && fy < blh) inside = InEllipse(fx - blw, fy - blh, blw, blh);
                    else inside = Mathf.Min(Mathf.Min(fx, w - fx), Mathf.Min(fy, h - fy));
                    float a = Mathf.Clamp01(inside + 0.5f);
                    if (ring > 0f) a *= Mathf.Clamp01(ring - inside + 0.5f);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            t.SetPixels32(px);
            t.Apply();
            Object.DontDestroyOnLoad(t);
            var s = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(left, bottom, right, top));
            Object.DontDestroyOnLoad(s);
            return s;
        }

        private static float InEllipse(float dx, float dy, float rx, float ry)
        {
            float u = dx / rx, v = dy / ry;
            float f = Mathf.Sqrt(u * u + v * v);
            if (f < 1e-4f) return Mathf.Min(rx, ry);
            float g = Mathf.Sqrt(u * u / (rx * rx) + v * v / (ry * ry)) / f;
            return (1f - f) / g;
        }

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (parent != null) go.layer = parent.gameObject.layer;
            return (RectTransform)go.transform;
        }

        // Places a rect from the parent's top-left corner.
        public static RectTransform At(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static RectTransform Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Panel(Transform parent, Color c, Sprite s = null)
        {
            var img = Node("panel", parent).gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            if (s != null) { img.sprite = s; img.type = Image.Type.Sliced; }
            return img;
        }

        // the window: dark, rounded, a thin mint edge; see-through ones show the game blurred behind them
        public static Image Window(Transform parent)
        {
            // a light tint over the game hides it like fog at the same opacity a dark one lets it through: the lighter
            // the background, the less of it is laid over, so every color looks as see-through (as in STRAFTH)
            float alpha = GlassAlpha * Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(GlassColor.grayscale * 1.6f));
            var bg = Panel(parent, A(GlassColor, alpha), Round);
            Edge(bg, Tint(0.2f), false);
            Blur.Behind(bg);
            return bg;
        }

        public static Slider MakeSlider(Transform parent, float min, float max, float val, UnityAction<float> onChange)
        {
            var root = Node("slider", parent);
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            var track = Panel(root, Tint(0.2f), RoundSmall);
            var trt = track.rectTransform;
            trt.anchorMin = new Vector2(0f, 0.5f);
            trt.anchorMax = new Vector2(1f, 0.5f);
            trt.sizeDelta = new Vector2(0f, 8f);
            var fillArea = Node("fill area", root);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(0f, 8f);
            var fill = Panel(fillArea, A(Txt, 0.55f), RoundSmall);
            fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = Node("handle area", root);
            Fill(handleArea);
            handleArea.offsetMin = new Vector2(9f, 0f);
            handleArea.offsetMax = new Vector2(-9f, 0f);
            var handle = Panel(handleArea, Txt, Round);
            handle.raycastTarget = true;
            var s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = min;
            s.maxValue = max;
            s.value = val;
            s.navigation = new Navigation { mode = Navigation.Mode.None };
            var cb = s.colors;
            cb.normalColor = new Color(1f, 1f, 1f, 0.85f);
            cb.highlightedColor = Color.white;
            cb.pressedColor = Color.white;
            cb.selectedColor = cb.normalColor;
            s.colors = cb;
            handle.CrossFadeColor(cb.normalColor, 0f, true, true);
            var hrt = handle.rectTransform;
            hrt.anchorMin = new Vector2(hrt.anchorMin.x, 0.5f);
            hrt.anchorMax = new Vector2(hrt.anchorMax.x, 0.5f);
            hrt.sizeDelta = new Vector2(18f, 18f);
            if (onChange != null) s.onValueChanged.AddListener(onChange);
            return s;
        }

        public static Image Edge(Image host, Color c, bool small = true)
        {
            var e = Panel(host.transform, c, small ? RoundSmallRing : RoundRing);
            Fill(e.rectTransform);
            return e;
        }

        // the selected one: a mint tint with a brighter mint edge
        public static Image Accent(Transform parent)
        {
            var p = Panel(parent, Tint(0.15f), RoundSmall);
            Edge(p, Tint(0.5f));
            return p;
        }

        private static readonly Dictionary<TMP_FontAsset, Material> CleanMats = new Dictionary<TMP_FontAsset, Material>();

        // the font's material without the outline and shadow the game uses for its own hud
        private static Material CleanMaterial(TMP_FontAsset font)
        {
            if (font == null || font.material == null) return null;
            Material m;
            if (CleanMats.TryGetValue(font, out m) && m != null) return m;
            try
            {
                m = new Material(font.material) { name = font.material.name + " (crosshairs clean)" };
                m.DisableKeyword("OUTLINE_ON");
                m.DisableKeyword("UNDERLAY_ON");
                m.DisableKeyword("UNDERLAY_INNER");
                m.DisableKeyword("GLOW_ON");
                foreach (var p in new[] { "_OutlineWidth", "_OutlineSoftness", "_FaceDilate", "_UnderlayOffsetX", "_UnderlayOffsetY", "_UnderlayDilate", "_UnderlaySoftness", "_GlowPower", "_GlowOuter", "_GlowInner", "_GlowOffset" })
                    if (m.HasProperty(p)) m.SetFloat(p, 0f);
                if (m.HasProperty("_Sharpness")) m.SetFloat("_Sharpness", 0.35f);
                ShaderUtilities.GetShaderPropertyIDs();
                ShaderUtilities.UpdateShaderRatios(m);
                Object.DontDestroyOnLoad(m);
            }
            catch { m = null; }
            CleanMats[font] = m;
            return m;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, float size, Color c, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var t = Node("text", parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (Font != null) t.font = Font;
            var clean = CleanMaterial(t.font);
            if (clean != null) t.fontSharedMaterial = clean;
            t.extraPadding = false;
            if (size <= 18f) t.characterSpacing = 5f;
            t.fontStyle = Style;
            t.text = text;
            t.fontSize = size;
            t.color = c;
            t.alignment = align;
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        public static Button Btn(Transform parent, string text, float size, UnityAction onClick, bool filled = false)
        {
            var img = Panel(parent, filled ? Txt : Tint(0.14f), RoundSmall);
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            var cb = b.colors;
            if (filled)
            {
                cb.normalColor = new Color(1f, 1f, 1f, 0.85f);
                cb.highlightedColor = Color.white;
                cb.pressedColor = new Color(1f, 1f, 1f, 0.65f);
            }
            else
            {
                cb.normalColor = new Color(1f, 1f, 1f, 0f);
                cb.highlightedColor = Color.white;
                cb.pressedColor = new Color(1f, 1f, 1f, 0.6f);
            }
            cb.selectedColor = cb.normalColor;
            cb.fadeDuration = 0.06f;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.targetGraphic = img;
            b.colors = cb;
            img.CrossFadeColor(cb.normalColor, 0f, true, true);
            if (onClick != null) b.onClick.AddListener(onClick);
            if (text != null)
            {
                var t = Label(img.transform, text, size, filled ? OnFill : Txt, filled ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft);
                t.characterSpacing = 0f;
                Fill(t.rectTransform);
                t.margin = filled ? Vector4.zero : new Vector4(16f, 0f, 16f, 0f);
            }
            return b;
        }

        public static void Center(Button b)
        {
            var t = b.GetComponentInChildren<TextMeshProUGUI>();
            if (t == null) return;
            t.alignment = TextAlignmentOptions.Center;
            t.margin = Vector4.zero;
        }

        public static Canvas MakeCanvas(string name, int order)
        {
            var go = new GameObject(name);
            Object.DontDestroyOnLoad(go);
            go.layer = Blur.MenuLayer;
            var c = go.AddComponent<Canvas>();
            // drawn by its own camera at the end of the frame, over a live blur of everything under it
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = Blur.MenuCamera();
            c.planeDistance = 10f;
            c.sortingOrder = order;
            // on whole pixels, so the crosshair pictures stay sharp
            c.pixelPerfect = true;
            var sc = go.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920f, 1080f);
            sc.matchWidthOrHeight = 1f;
            go.AddComponent<TopRaycaster>();
            return c;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || Object.FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("ezCrosshair EventSystem");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }
    }
}
#endif
