// MONOLITH - its own menu. Dorsth was here.
//
// Look: a text-mode screen in the manner of Far Manager. One fixed-width font, two framed panels, a dark palette, the
// cursor as a bar, a key bar along the bottom. Chosen because it is about the cheapest thing a menu can be: a few
// rectangles and one line of text per row, drawn only while the menu is open (the script that draws is switched off
// when it closes; what stays is one key check a frame).
//
// Structure: the STRAFTH menu's. Categories in a list on the left, a page of rows on the right; a row is a setting
// or a link to a page below it; the page's path is in the frame's title; back goes one page up; a search reads
// every setting. What is on which page is in Layout.cs.
//
// Keys: up/down move, left/right change a value (shift: ten steps), enter opens, flips or edits, del puts the default
// back, backspace goes back, tab switches panel, / searches, escape leaves an edit, the search, then the menu.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Monolith
{
    /// <summary>Always on: one key check a frame. Everything else lives in BundleMenu, which is off while the menu is closed.</summary>
    internal sealed class MenuKeyWatch : MonoBehaviour
    {
        private void Update()
        {
            BundleMenu m = BundleMenu.I;
            if ((object)m == null) return;
            if (BundleMenu.EscHeld) BundleMenu.EscTick();
            if (BundleMenu.Opened)
            {
                m.Hold();
                // the menu's own key closes it (not while a key is being picked or something is being typed)
                if (BundleMenu.KeyDown(BundleMenu.Key.Value) && !m.Busy) m.Close();
                return;
            }
            if (!BundleMenu.KeyDown(BundleMenu.Key.Value) || BundleMenu.GameTyping()) return;
            m.Open();
        }
    }

    internal sealed partial class BundleMenu : MonoBehaviour
    {
        internal static BundleMenu I;
        internal static ConfigEntry<KeyCode> Key;
        /// <summary>Open or not, kept here so nothing has to ask the engine.</summary>
        internal static bool Opened;
        internal static bool IsOpen { get { return Opened; } }
        internal bool Busy { get { return _capture || _editRow != null || _searching || _bindRow != null || _pop != null; } }

        /// <summary>Settings that have no use in this menu (a mod's own menu key, where its window was).</summary>
        internal static readonly HashSet<ConfigEntryBase> Hidden = new HashSet<ConfigEntryBase>();

        internal static bool KeyDown(KeyCode k)
        {
            if (k == KeyCode.None) return false;
            try { return UnityInput.Current.GetKeyDown(k); } catch { return Input.GetKeyDown(k); }
        }

        // One row failing (often a setting of another mod) must not take the menu down. It is written to the log,
        // once for each different failure: the menu is drawn every frame.
        private static readonly HashSet<string> Warned = new HashSet<string>();
        internal static void Warn(string what, Exception e)
        {
            if (Warned.Add(what + ": " + e.Message)) BundlePlugin.Log.LogWarning("Menu, " + what + ": " + e.Message);
        }

        // A value typed in the menu goes into the setting. Text the setting does not take is dropped, as when typing letters into a number.
        private static void SetTyped(ConfigEntryBase entry, string typed)
        {
            try { entry.SetSerializedValue(entry.SettingType == typeof(Color) ? typed.TrimStart('#') : typed); }
            catch (Exception) { }
        }

        private List<MenuPage> _cats;
        private int _cat;
        private MenuPage _page;
        private bool _inNav = true;
        private readonly List<MenuRow> _vis = new List<MenuRow>();

        // an edit in progress
        private MenuRow _editRow;
        private string _edit;
        // the page panel itself while a dialog page takes _pageR
        private Rect _behindR;
        // an open dialog's whole box, its frame and the ground around it; _pageR is where its rows go
        private Rect _dlgR;
        // a dialog's width in cells: its rows and the two samples side by side fit, with the page around it
        private const int DialogCols = 48;
        // where a dialog was dragged by its top line, from the middle of the page; back in the middle when one opens
        private Vector2 _dlgMoved; private MenuPage _dlgFor; private bool _dlgDrag; private Vector2 _dlgGrab;
        // the value a field opened with, not typed over yet: grey, and the first character typed replaces it, as in Far
        private bool _editFresh;
        private bool _capture;
        // a key being set by pressing it (read from the input system: it knows mouse buttons and the wheel too)
        private MenuRow _bindRow;
        private int _bindEnd = -10;
        // a row that wants E twice, and the line a row's action gave back
        private MenuRow _armed;
        private bool _armedByClick, _clicking;
        private string _say;
        private float _sayUntil;
        private void Say(string s) { _say = s; _sayUntil = Time.unscaledTime + 8f; }
        // the search
        private bool _searching;
        private string _query = "";
        private MenuPage _searchPage;

        private bool _pauseBefore;
        private int _openFrame = -10, _modeFrame = -10;
        private bool _bindEnterUp;
        private GameObject _block;
        private static int _escFrame = -10;
        internal static bool EscHeld;

        /// <summary>The escape that closed the menu is over once the key is up.</summary>
        internal static void EscTick()
        {
            if (Time.frameCount - _escFrame <= 1) return;
            bool down; try { down = UnityInput.Current.GetKey(KeyCode.Escape); } catch { down = Input.GetKey(KeyCode.Escape); }
            if (!down) EscHeld = false;
        }

        private void Awake()
        {
            I = this; enabled = false; useGUILayout = false;
            // a map or the main menu loading while it is open: shut, and the new scene's pause state is left alone
            SceneManager.activeSceneChanged += (from, to) => { if (Opened) Shut(false); };
        }

        /// <summary>What the info box says: the row under the mouse first, then the category under it, then the cursor's.</summary>
        private string InfoText(int over, int overCat, bool pageOn, int ci)
        {
            if (over >= 0 && over < _vis.Count) return Describe(_vis[over]);
            if (overCat >= 0) return Lang.T(_cats[overCat].Info);
            if (!pageOn) return Lang.T(_cats[_cat].Info);
            if (ci >= 0) return Describe(_vis[ci]);
            if (_searching && _query.Length > 0) return Lang.T("Nothing found.");
            return "";
        }

        // ---- open and close

        internal void Open()
        {
            if (Opened) return;
            if (_cats == null)
            {
                try { _cats = MenuLayout.Build(); }
                catch (Exception e) { BundlePlugin.Log.LogError("Menu: " + e); _cats = new List<MenuPage>(); }
            }
            if (_cats.Count == 0) return;
            _cat = Mathf.Clamp(_cat, 0, _cats.Count - 1);
            if (_page == null) _page = _cats[_cat];
            PauseManager pm = PauseManager.Instance;
            _pauseBefore = pm != null && pm.pause;
            // the game ignores your mouse and keys while it thinks it is paused
            if (pm != null) pm.pause = true;
            _editRow = null; _capture = false; _searching = false;
            _openFrame = Time.frameCount;
            Block(true);
            Opened = true; enabled = true; MenuHooks.HoldOtherChatPrefixes();
        }

        internal void Close() { Shut(true); }

        private void Shut(bool restorePause)
        {
            if (!Opened) return;
            if (_pop != null) PopClose(_pop.Palette);   // a color picked stays when the menu shuts
            _dragRow = null;
            Opened = false; enabled = false;
            _editRow = null; _capture = false; _searching = false; _searchPage = null; _bindRow = null;
            Block(false);
            if (!restorePause) return;
            PauseManager pm = PauseManager.Instance;
            if (pm != null) pm.pause = _pauseBefore;
        }

        // An unseen sheet over the whole screen while the menu is open, so a click in the menu does not also press a
        // button of the game that happens to be under it (the main menu's). Made the first time the menu opens.
        private void Block(bool on)
        {
            try
            {
                if (_block == null)
                {
                    if (!on) return;
                    _block = new GameObject("MonolithMenuBlock", typeof(Canvas), typeof(GraphicRaycaster));
                    _block.hideFlags = HideFlags.HideAndDontSave;
                    DontDestroyOnLoad(_block);
                    Canvas c = _block.GetComponent<Canvas>();
                    c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 32000;
                    var sheet = new GameObject("Sheet", typeof(Image));
                    sheet.transform.SetParent(_block.transform, false);
                    Image img = sheet.GetComponent<Image>();
                    img.color = new Color(0f, 0f, 0f, 0f); img.raycastTarget = true;
                    RectTransform rt = img.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                }
                if (_block.activeSelf != on) _block.SetActive(on);
            }
            catch (Exception e) { Warn("the click shield", e); }
            Keyboard(on);
        }

        // The sheet stops the mouse, not the keyboard. The game's own windows are steered with the same keys as this menu
        // (Enter and Space press the button or switch that is picked there, the arrows and W A S D move to the next one or
        // move a slider), so every key pressed in here was also pressing things in a game window that happened to be open
        // or was left with something picked. While the menu is open the game's windows get no steering keys and nothing in
        // them is picked (a text box of the game that was being typed in is left). Put back as it was on closing.
        private UnityEngine.EventSystems.EventSystem _keysOf; private bool _keysWere;

        private void Keyboard(bool menuOpen)
        {
            try
            {
                if (menuOpen)
                {
                    UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
                    if (es == null || _keysOf != null) return;
                    _keysOf = es; _keysWere = es.sendNavigationEvents;
                    es.sendNavigationEvents = false;
                    es.SetSelectedGameObject(null);
                }
                else if (_keysOf != null)
                {
                    _keysOf.sendNavigationEvents = _keysWere;
                    _keysOf = null;
                }
            }
            catch { _keysOf = null; }
        }

        /// <summary>Every frame while open: the cursor stays free.</summary>
        internal void Hold()
        {
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        /// <summary>The escape press that closed the menu does not also open the game's own menu.</summary>
        internal static bool SwallowEscape { get { return EscHeld; } }

        internal static bool GameTyping()
        {
            EventSystem es = EventSystem.current;
            GameObject go = es != null ? es.currentSelectedGameObject : null;
            if (go != null)
            {
                TMP_InputField tmp = go.GetComponent<TMP_InputField>();
                if (tmp != null && tmp.isFocused) return true;
                InputField ui = go.GetComponent<InputField>();
                if (ui != null && ui.isFocused) return true;
            }
            PauseManager pm = PauseManager.Instance;
            return pm != null && pm.chatting && SceneManager.GetActiveScene().name != "MainMenu";
        }

        // ---- which rows of a page show right now, and where the cursor is among them

        private static bool Shows(MenuRow r)
        {
            if (r.Entry != null && Hidden.Contains(r.Entry)) return false;
            if (r.If == null) return true;
            try { return r.If(); } catch { return true; }
        }

        private int Lay(MenuPage p)
        {
            _vis.Clear();
            if (p.Parent != null && !p.Dialog) _vis.Add(UpRow);
            foreach (MenuRow r in p.Rows) if (Shows(r)) _vis.Add(r);
            // a heading with nothing under it
            for (int i = _vis.Count - 1; i >= 0; i--)
                if (_vis[i].Kind == RowKind.Header && (i == _vis.Count - 1 || _vis[i + 1].Kind == RowKind.Header)) _vis.RemoveAt(i);
            // a dialog opens straight on its first part: its title already says what it is, as Far's dialogs do
            if (p.Dialog && _vis.Count > 0 && _vis[0].Kind == RowKind.Header) _vis.RemoveAt(0);
            int ci = p.Cur != null ? _vis.IndexOf(p.Cur) : -1;
            if (ci < 0 && _vis.Count > 0)
            {
                // the row the cursor was on is gone (a switch hid it): the nearest row that can be picked
                int from = Mathf.Clamp(p.Cur == null && p.Last == 0 && p.Parent != null ? 1 : p.Last, 0, _vis.Count - 1);
                for (int d = 0; d < _vis.Count && ci < 0; d++)
                {
                    if (from - d >= 0 && _vis[from - d].Kind != RowKind.Header) ci = from - d;
                    else if (from + d < _vis.Count && _vis[from + d].Kind != RowKind.Header) ci = from + d;
                }
                p.Cur = ci >= 0 ? _vis[ci] : null;
            }
            if (ci >= 0) p.Last = ci;
            return ci;
        }

        private MenuPage Current() { return _searching ? SearchPage() : _page; }

        private static readonly MenuRow UpRow = new MenuRow { Kind = RowKind.Up, Label = ".." };
        private MenuRow _dragRow; private Vector2 _lastMouse;
        // a long page gives its scroll bar a cell
        private int ColsFor(MenuPage page) { return Mathf.RoundToInt((_pageR.width - 2 * _cw) / _cw) - 1 - (page.Side ? page.SideCols + 1 : 0) - (Scrolls ? 2 : 0); }

        // ---- drawing

        private bool _byMouse;
        private static Font _font;
        private static GUIStyle _st;
        private static Texture2D _white, _dots;
        private static int _fontFor;
        private static bool _fontSaid;
        // one cell of the text grid in pixels, and the window and its two panels
        private float _cw, _ch;
        private Rect _win, _navR, _pageR;
        // where the first category and the first row are and how far apart they sit: set when it is drawn, used by the mouse
        private float _navY0, _navStep, _rowY0, _rowStep;
        private int _visRows = PanelRows - 2;
        // The menu in cells. Across: 80. Down: a margin, the two panels, a gap, the info box, a gap, the key bar.
        private const int Cols = 80, RowsHigh = 30, NavCols = 19;
        private const int PanelRows = RowsHigh - 4, KeyBarRows = 1;
        private const float TopMargin = 0.75f, SideMargin = 0.5f, BoxGap = 0.5f, InfoRows = 3.6f;
        private const float WindowRows = TopMargin + PanelRows + BoxGap + InfoRows + BoxGap + KeyBarRows;
        // on the left, so the crosshair and your weapon stay in view while you change them: this share of the screen is left of it
        private const float ScreenLeft = 0.03f;

        // the STRAFTAT theme for Far (straftat-lobby, STRAFCOLORS), measured off Far with it: panel glass, gunmetal-sage
        // frames (lighter on dialogs), the active title on a mint badge, yellow column titles and keys. The panels are the
        // theme's dark glass and dialogs (the preview and pop-ups) its dialog ground, as Far shows them with the theme
        private static readonly Color Back = new Color32(0x09, 0x0A, 0x05, 0xFF), Frame = new Color32(0x8F, 0xA5, 0x9B, 0xFF), Bar = new Color32(0x34, 0x45, 0x32, 0xFF), MenuBar = new Color32(0x2B, 0x39, 0x29, 0xFF),
            FrameDim = new Color32(0x6F, 0x85, 0x7B, 0xFF), Panel = new Color32(0x13, 0x18, 0x0F, 0xFF),
            BarDim = new Color32(0x13, 0x18, 0x10, 0xFF), Shadow = new Color(0f, 0f, 0f, 0.72f), Hover = new Color32(0x1A, 0x22, 0x17, 0xFF), KeyBar = new Color32(0x17, 0x1C, 0x13, 0xFF),
            Well = new Color32(0x07, 0x08, 0x03, 0xFF), Badge = new Color32(0xB4, 0xF6, 0xB7, 0xFF);
        private const string Txt = "#ADB3AC", Dim = "#909790", Hot = "#F0FC9D", Mint = "#B4F6B7", Val = "#FCFEF9", Head = "#8FA59B", Faint = "#5F605D", Ink = "#070803", Unchanged = "#8F8F8E";

        private void Style()
        {
            int size = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 60f), 11, 30);
            if (_st != null && _fontFor == size && _font != null) return;
            _fontFor = size;
            // Cascadia Mono first: the font of Windows Terminal, which Far runs in; Consolas where it is missing
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont(new[] { "Cascadia Mono", "Consolas", "Lucida Console", "Menlo", "Monaco", "Courier New", "DejaVu Sans Mono", "Liberation Mono" }, size);
            if (_white == null) { _white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave }; _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            _st = new GUIStyle { font = _font, fontSize = size, richText = true, wordWrap = false, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            _st.normal.textColor = Color.white;
            // the font leaves more room above a capital than under it: one pixel up (at 1080p) puts a row's text in the middle of its bar
            _st.contentOffset = new Vector2(0f, -Mathf.Max(1f, Mathf.Round(size / 18f)));
            _cw = _st.CalcSize(new GUIContent("MMMMMMMMMMMMMMMMMMMM")).x / 20f;
            _ch = Mathf.Ceil(size * 1.32f);
            float thin = _st.CalcSize(new GUIContent("iiiiiiiiiiiiiiiiiiii")).x / 20f;
            if (!_fontSaid)
            {
                _fontSaid = true;
                if (Mathf.Abs(thin - _cw) > 0.5f) BundlePlugin.Log.LogWarning("Menu font " + string.Join(", ", _font.fontNames) + " is not fixed-width (no such font was found on this PC): the columns will not line up.");
            }
        }

        private void Fill(Rect r, Color c) { Color o = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = o; }

        // how thick a frame line is
        private float Thin { get { return Mathf.Max(1f, Mathf.Round(_ch / 14f)); } }

        // a double frame, as the panels of a text-mode file manager have, drawn as lines (no gaps between rows);
        // a box inside a panel has one line
        private void Box(Rect r, string title, bool active) { Box(r, title, active, Back); }

        private void Box(Rect r, string title, bool active, Color under, int lines = 2)
        {
            // a panel's frame is the darker sage, a dialog's and its groups' the lighter one
            bool panel = lines == 2 && under == Back;
            Color c = panel ? FrameDim : Frame;
            float t = Thin, g = lines == 2 ? t * 2f : 0f;
            for (int k = 0; k < lines; k++)
            {
                float o = k * g; Rect q = new Rect(r.x + o, r.y + o, r.width - 2 * o, r.height - 2 * o);
                Fill(new Rect(q.x, q.y, q.width, t), c); Fill(new Rect(q.x, q.yMax - t, q.width, t), c);
                Fill(new Rect(q.x, q.y, t, q.height), c); Fill(new Rect(q.xMax - t, q.y, t, q.height), c);
            }
            if (string.IsNullOrEmpty(title)) return;
            float w = (title.Length + 2) * _cw, x = Mathf.Round(r.x + (r.width - w) * 0.5f);
            // the title of the panel that has the keys sits on a mint badge, as in the theme; a dialog's is bright. Titles are bold, as Far's
            bool badge = active && panel;
            // a badge is a whole cell; a plain title only hides the frame under it, so the ground above stays
            if (badge) Fill(new Rect(x, r.y - _ch * 0.5f + g, w, _ch), Badge); else Fill(new Rect(x, r.y, w, _ch * 0.5f + g), under);
            GUI.Label(new Rect(x + _cw, r.y - _ch * 0.5f + g * 0.5f, w, _ch), "<b><color=" + (badge ? Ink : panel ? Head : lines == 2 ? Val : Txt) + ">" + Esc(title) + "</color></b>", _st);
        }

        // text being typed, as Far shows it: bright once typed in, grey while it is still the value the field opened
        // with, and a blinking mint cursor after it. Far's cursor in insert mode is a bar along the bottom of the cell,
        // 15% of its height (CursorSize1 in its config.cpp); ▁, an eighth of the cell, is the nearest character
        private static string Typing(string text, bool fresh)
        {
            string cursor = Time.unscaledTime % 1f < 0.5f ? "<color=" + Mint + ">▁</color>" : " ";
            return "<color=" + (fresh ? Unchanged : Val) + ">" + Esc(text) + "</color>" + cursor;
        }

        private void Line(float x, float y, string rich) { GUI.Label(new Rect(x, y, _win.xMax - x, _ch), rich, _st); }

        // a word in the middle of a panel's bottom line, as Far puts its totals there
        private void Footer(Rect r, string text)
        {
            float g = Thin * 2f, w = (text.Length + 2) * _cw, x = Mathf.Round(r.x + (r.width - w) * 0.5f);
            Fill(new Rect(x, r.yMax - _ch * 0.5f - g, w, _ch * 0.5f + g), Back);   // over the frame only: below it is the ground
            GUI.Label(new Rect(x + _cw, r.yMax - _ch * 0.5f - g * 0.5f - Thin, w, _ch), "<color=" + Dim + ">" + Esc(text) + "</color>", _st);
        }

        // A long page has a scroll bar inside its right frame, as Far draws one beside text in a panel: ▲ on the first
        // row, ▼ on the last, a dotted track between them and the part shown as a solid block on it.
        private void ScrollBar(int top, int shown, int count) { ScrollBar(BarX(_pageR.xMax), _rowY0, top, shown, count, Back); }

        // where a scroll bar goes, in a frame whose outer edge is at right: the same gap (Gap) between the frame's inner
        // line and the bar, and between the bar and the rows. Gap is also what a pop-up has above its first row and
        // below its last, so its rows have the same room on all four sides.
        private const float BarW = 0.8f;
        private float Gap { get { return Mathf.Round(_ch * 0.5f - Thin * 3f); } }
        private float BarX(float right) { return Mathf.Round(right - Thin * 3f - Gap - _cw * BarW); }
        private bool Scrolls { get { return _vis.Count > _visRows; } }

        // a page's scroll bar, held with the mouse: the cursor goes to the row at that height of the bar
        private bool _barHeld;
        private Rect PageBar(MenuPage page) { return Scrolls ? new Rect(BarX(_pageR.xMax), _rowY0, _cw * BarW, _visRows * _ch) : new Rect(); }
        private void BarTo(MenuPage page, int ci, float y)
        {
            Rect b = PageBar(page);
            float f = Mathf.Clamp01((y - b.y - _ch) / Mathf.Max(1f, b.height - 2f * _ch));
            Move(page, ci, Mathf.RoundToInt(f * (_vis.Count - 1)) - Mathf.Max(ci, 0));
        }

        // a scroll bar one cell wide at x, over the rows from y0 down (a page's, or a list's in a pop-up)
        private void ScrollBar(float x, float y0, int top, int shown, int count, Color under)
        {
            float h = shown * _ch, w = Mathf.Round(_cw * BarW), ax = Mathf.Round(x + (w - _cw) * 0.5f);
            Fill(new Rect(x, y0, w, h), under);
            GUI.Label(new Rect(ax, y0, _cw * 2f, _ch), "<color=" + Head + ">▲</color>", _st);
            GUI.Label(new Rect(ax, y0 + h - _ch, _cw * 2f, _ch), "<color=" + Head + ">▼</color>", _st);
            float ty = y0 + _ch, th = h - 2f * _ch;
            if (th <= 0f) return;
            // the track: dots, as Far's light shade character
            if (_dots == null)
            {
                _dots = new Texture2D(3, 3, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
                for (int i = 0; i < 9; i++) _dots.SetPixel(i % 3, i / 3, i == 4 ? Color.white : Color.clear);
                _dots.Apply();
            }
            // the dots centered across the bar (each 3 pixels holds one, on its middle pixel)
            float first = Mathf.Floor((w - (Mathf.Floor((w - 1f) / 3f) * 3f + 1f)) / 2f);
            Color was = GUI.color; GUI.color = FrameDim;
            GUI.DrawTextureWithTexCoords(new Rect(x, ty, w, th), _dots, new Rect((1f - first) / 3f, 0f, w / 3f, th / 3f));
            GUI.color = was;
            float bh = Mathf.Max(_ch * 0.5f, th * shown / count), by = ty + (th - bh) * top / Mathf.Max(1, count - shown);
            Fill(new Rect(x, Mathf.Round(by), w, Mathf.Round(bh)), Frame);
        }

        // A section heading as Far heads a column: its name alone, in the middle, in the color of a column title.
        // Beside a preview it is in the middle of the rows.
        // a part of a dialog starts at a line across it joined to its frame, its name in the middle of the line, as
        // modern Far splits its dialogs (╟──── Markers ────╢)
        private void Divider(float y, string title)
        {
            Rect f = DialogFrame(); float t = Thin, lineY = Mathf.Round(y + _ch * 0.5f - t * 0.5f);
            Fill(new Rect(f.x + 2f * t, lineY, f.width - 4f * t, t), Frame);
            title = " " + Fit(title, (int)(f.width / _cw) - 6) + " ";
            float w = title.Length * _cw, x = Mathf.Round(f.x + (f.width - w) * 0.5f);
            Fill(new Rect(x, y, w, _ch), Panel);
            GUI.Label(new Rect(x, y, w + 2f, _ch), "<color=" + Txt + ">" + Esc(title) + "</color>", _st);
        }

        private void Heading(Rect row, string title, Rect side)
        {
            bool beside = side.width > 0f && row.y + _ch * 0.5f >= side.y && row.y <= side.yMax;
            float x0 = _pageR.x + _cw, x1 = beside ? side.x : _pageR.xMax - _cw;
            title = Fit(title, (int)((x1 - x0) / _cw) - 2);
            float w = title.Length * _cw, x = Mathf.Round((x0 + x1 - w) * 0.5f);
            GUI.Label(new Rect(x, row.y, w + 2f, _ch), "<b><color=" + Hot + ">" + Esc(title) + "</color></b>", _st);
        }

        // long enough for a join message with color tags in it
        private const int EditMax = 200;

        private static string Esc(string s) { return s == null ? "" : s.Replace('<', '‹').Replace('>', '›'); }
        private static string Fit(string s, int n) { if (n <= 0) return ""; return s.Length <= n ? s : (n == 1 ? "…" : s.Substring(0, n - 1) + "…"); }
        private static string Pad(string s, int n) { s = Fit(s, n); return s.Length >= n ? s : s + new string(' ', n - s.Length); }

        private void OnGUI()
        {
            Style();
            Event ev = Event.current;
            MenuPage page = Current(); int ci = Lay(page);
            float w = Cols * _cw;
            float h = Mathf.Round(WindowRows * _ch);
            float left = Mathf.Min(Screen.width * ScreenLeft, Mathf.Max(0f, Screen.width - w));
            _win = new Rect(Mathf.Round(left), Mathf.Round((Screen.height - h) * 0.5f), w, h);
            _navR = new Rect(_win.x + _cw * SideMargin, _win.y + _ch * TopMargin, NavCols * _cw, PanelRows * _ch);
            // a whole cell between the panels: the page is 59 cells wide, every column of it on the same grid as the rest
            float pageX = _navR.xMax + _cw;
            _pageR = new Rect(pageX, _navR.y, _win.xMax - _cw * SideMargin - pageX, _navR.height);
            // the first row of a panel is one cell under its frame
            _navY0 = _navR.y + _ch;
            _navStep = _ch;
            _rowY0 = _pageR.y + _ch;
            _rowStep = _ch;
            _visRows = PanelRows - 2;
            _behindR = _pageR;
            if (page != null && page.Dialog && !_searching)
            {
                // a box over the menu, its rows one under the other and the samples under them
                // the frame takes a row above the rows and one under them; samples take their rows and a line more
                float samples = page.Draw != null ? page.SideRows() : 0f;
                int rows = Mathf.Min(_vis.Count, PanelRows - 4 - Mathf.CeilToInt(samples));
                float dh = Mathf.Min((rows + 2 + samples) * _ch, _behindR.height - 2f * _ch), dw = Mathf.Min(DialogCols * _cw, _behindR.width - 4f * _cw);
                if (_dlgFor != page) { _dlgFor = page; _dlgMoved = Vector2.zero; _dlgDrag = false; }
                // where it was dragged to, anywhere on the screen as long as it stays whole on it
                float mx = Mathf.Round(_dlgMoved.x), my = Mathf.Round(_dlgMoved.y);
                // in the middle of the whole menu window, as Far centers a dialog on the whole screen, on the page's grid
                float x = _behindR.x + Mathf.Round((_win.center.x - dw * 0.5f - _behindR.x) / _cw) * _cw + mx;
                float y = _behindR.y + Mathf.Round((_win.center.y - dh * 0.5f - _behindR.y) / _ch) * _ch + my;
                // on whole pixels, so its text does not shimmer while it is dragged
                _dlgR = new Rect(Mathf.Round(Mathf.Clamp(x, 0f, Screen.width - dw)), Mathf.Round(Mathf.Clamp(y, 0f, Screen.height - dh)), dw, dh);
                // the rows a cell further in than on a page: Far keeps a cell and a half between a dialog's frame and
                // what is in it, so the cursor bar and the pop-ups stay inside the frame
                _pageR = new Rect(_dlgR.x + _cw, _dlgR.y, _dlgR.width - 2f * _cw, _dlgR.height);
                _rowY0 = _pageR.y + _ch;
                _visRows = rows;
            }

            // the mouse in use: the page shows no cursor, only the row under the mouse; a key brings the cursor back
            bool wasMouse = _byMouse;
            if (ev.type == EventType.MouseDown || ev.type == EventType.ScrollWheel) _byMouse = true;
            else if (ev.type == EventType.KeyDown && ev.keyCode != KeyCode.None) _byMouse = false;
            if (_bindRow != null) PollBind();
            bool binding = _bindRow != null || Time.frameCount - _bindEnd <= 1;
            // Reset under the page works in every state: it ends whatever is being edited, then resets
            if (ev.type == EventType.MouseDown && ev.button == 0 && _resetRow != null && _resetRect.Contains(ev.mousePosition))
            {
                MenuRow r = _resetRow;
                if (_pop != null) PopClose(false);
                if (_bindRow != null) { _bindRow = null; _bindEnd = Time.frameCount; }
                _editRow = null; _capture = false; _dragRow = null;
                Default(r);
                ev.Use(); return;
            }
            if (_pop != null)
            {
                // a pop-up is open: everything goes to it
                if (ev.type == EventType.KeyDown) PopKeys(ev);
                else if (ev.type == EventType.ScrollWheel || ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag || ev.type == EventType.MouseUp) { PopMouse(ev); ev.Use(); }
            }
            else if (ev.type == EventType.KeyDown)
            {
                if (binding) ev.Use();
                else
                {
                    // the first key after the mouse acts on the row under the mouse, the one that was shaded, not on a hidden cursor
                    int at = wasMouse && !_inNav && ev.keyCode != KeyCode.None ? RowAt(_lastMouse, page) : -1;
                    if (at >= 0) { if (page.Cur != _vis[at]) { _armed = null; _say = null; } page.Cur = _vis[at]; page.Last = at; ci = at; }
                    Keys(ev, page, ci);
                }
                if (!Opened) return;
            }
            else if (ev.type == EventType.ScrollWheel) { if (!binding) Wheel(page, ci, ev.delta.y > 0 ? 3 : -3, ev.mousePosition); ev.Use(); }
            else if (ev.type == EventType.MouseDown && ev.button == 0 && !binding && page.Dialog && !_searching && DialogGrip().Contains(ev.mousePosition)) { _dlgDrag = true; _dlgGrab = ev.mousePosition - _dlgMoved; ev.Use(); }
            else if (ev.type == EventType.MouseDown && ev.button == 0 && !binding && PageBar(page).Contains(ev.mousePosition)) { _barHeld = true; BarTo(page, ci, ev.mousePosition.y); ev.Use(); }
            else if (ev.type == EventType.MouseDown) { if (!binding) Click(ev.mousePosition, page, ev.button); ev.Use(); if (!Opened) return; }
            else if (ev.type == EventType.MouseDrag && _dlgDrag) { _dlgMoved = ev.mousePosition - _dlgGrab; ev.Use(); }
            else if (ev.type == EventType.MouseDrag) { if (_barHeld) BarTo(page, ci, ev.mousePosition.y); else if (_dragRow != null) Slide(_dragRow, ev.mousePosition.x, page); ev.Use(); }
            else if (ev.type == EventType.MouseUp) { _dragRow = null; _barHeld = false; _dlgDrag = false; }
            if (ev.type != EventType.Repaint) return;
            page = Current(); ci = Lay(page);
            // As in Far Manager, the mouse moves the cursor only with a click, never by passing over a row.
            // The pop-up lists do follow it, as Far's menus do.
            Vector2 mouse = ev.mousePosition; bool mouseMoved = mouse != _lastMouse; _lastMouse = mouse;
            // what the mouse is over is described in the box below, while the bar stays where it is
            int over = _pop == null && _editRow == null && _dragRow == null ? RowAt(mouse, page) : -1, overCat = -1;
            if (_pop == null && _editRow == null && _dragRow == null && _navR.Contains(mouse)) { int i = Mathf.FloorToInt((mouse.y - _navY0) / _navStep); if (i >= 0 && i < _cats.Count) overCat = i; }
            bool pageOn = !_inNav || _searching;
            // the cursor's row is kept in view
            if (ci >= 0 && ci < page.Top) page.Top = ci;
            if (ci >= page.Top + _visRows) page.Top = ci - _visRows + 1;
            page.Top = Mathf.Clamp(page.Top, 0, Mathf.Max(0, _vis.Count - _visRows));
            // a heading just above the first row shown is shown with it
            if (page.Top == 1 && _vis.Count > 0 && _vis[0].Kind == RowKind.Header) page.Top = 0;

            // no ground of its own: the game shows between and around the panels
            Fill(_navR, Back); Fill(_behindR, Back);
            // the box under the panels and the key bar: drawn first while a dialog is open, so it goes over them
            bool dlgOpen = page.Dialog && !_searching;
            void Foot()
            {
                // what the row under the cursor is
                Rect info = new Rect(_navR.x, _navR.yMax + _ch * BoxGap, _win.width - 2 * _cw * SideMargin, _ch * InfoRows);
                Fill(info, Back);
                Box(info, null, false);
                bool said = _say != null && Time.unscaledTime < _sayUntil;
                MenuRow told = said ? null : over >= 0 && over < _vis.Count ? _vis[over] : overCat >= 0 || !pageOn ? null : ci >= 0 && ci < _vis.Count ? _vis[ci] : null;
                string text = said ? _say : InfoText(over, overCat, pageOn, ci);
                // the mouse went from a row down to this box (to click Reset): the box keeps telling about that row
                if (!said && info.Contains(mouse) && _told != null && _vis.Contains(_told)) { told = _told; text = Describe(told); }
                else _told = told;
                // a changed setting: "Reset" at the end of the box, where its default is read (R does the same)
                _resetRow = told != null && Changed(told) ? told : null;
                string reset = Lang.T("Reset");
                string[] lines = Wrap(_resetRow != null ? text + " " + reset : text, (int)(info.width / _cw) - 4, 3);
                int last = lines.Length - 1;
                bool link = _resetRow != null && last >= 0 && lines[last].EndsWith(reset, StringComparison.Ordinal);
                if (!link) _resetRow = null;
                for (int i = 0; i < lines.Length; i++)
                {
                    float y = info.y + _ch * 0.3f + i * _ch;
                    string l = link && i == last ? lines[i].Substring(0, lines[i].Length - reset.Length) : lines[i];
                    Line(info.x + _cw * 1.5f, y, "<color=" + (said ? Mint : Txt) + ">" + Esc(l) + "</color>");
                    if (link && i == last)
                    {
                        _resetRect = new Rect(info.x + _cw * (1.5f + l.Length) - _cw * 0.5f, y, (reset.Length + 1) * _cw, _ch);
                        if (_resetRect.Contains(mouse)) Fill(_resetRect, Hover);
                        Line(info.x + _cw * (1.5f + l.Length), y, "<color=" + Hot + ">" + reset + "</color>");
                    }
                }

                // the key bar
                Rect kb = new Rect(_win.x, _win.yMax - _ch, _win.width, _ch);
                Fill(kb, KeyBar);
                string keys = _pop != null ? (_pop.Palette ? K("WASD", "move") : K("WS", "move")) + K("E", "take") + K("Q", "cancel") + (_pop.Palette ? K("R", "reset") + K("0-9", "hex value") : K("A-Z", "jump to a name"))
                    : _bindRow != null ? K("Key", "press one") + K("Enter", "none") + K("Esc", "cancel")
                    : _capture ? K("Key", "press one") + K("Esc", "cancel")
                    : _editRow != null ? K("Enter", "ok") + K("Backspace", "delete") + K("Esc", "cancel")
                    : _searching ? K("type", "to search") + K("Enter", "open") + K("Esc", "leave search")
                    : _inNav ? K("WS", "move") + K("E", "open") + K("Tab", "switch panel") + K("/", "search") + K("Esc", "close")
                    : K("WS", "move") + K("AD", "set") + K("E", "open") + K("Q", "back") + K("/", "search") + K("Esc", "close")
                        // last, so the keys before it stay put when it comes and goes
                        + ((over >= 0 && over < _vis.Count ? Changed(_vis[over]) : pageOn && ci >= 0 && ci < _vis.Count && Changed(_vis[ci])) ? K("R", "reset") : "");
                Line(kb.x + _cw * 2f, kb.y, keys);   // on the same column as the text of the panels and of the box above it
            }
            if (dlgOpen) Foot();
            // the categories
            Box(_navR, "MONOLITH", !pageOn);
            for (int i = 0; i < _cats.Count; i++)
            {
                // the settings of other mods are faint, as Far shows hidden files
                Rect r = new Rect(_navR.x + _cw, _navR.y + (i + 1) * _ch, _navR.width - 2 * _cw, _ch);
                bool cur = i == _cat;
                if (cur) Fill(r, pageOn ? BarDim : Bar); else if (i == overCat) Fill(r, Hover);
                Line(r.x + _cw * 0.5f, r.y, "<color=" + (cur ? Val : _cats[i].Outside ? Faint : Txt) + ">" + Pad(Lang.T(_cats[i].Path[0]), NavCols - 3) + "</color>");
            }
            Footer(_navR, BundlePlugin.Version);
            bool dialog = page.Dialog && !_searching;
            Rect pageR = _pageR; _pageR = _behindR;
            string title = _searching ? Lang.T("search: ") + _query + "_" : PathText(dialog ? page.Parent : page);
            Box(_pageR, Fit(title, (int)(_pageR.width / _cw) - 24), pageOn);   // room on the right for the clock
            // the clock on the top frame, as Far shows it: over the corner, its last letter on the frame's outer edge
            string clock = DateTime.Now.ToShortTimeString();
            float cx = _pageR.xMax - (clock.Length + 1) * _cw;
            Fill(new Rect(cx, _pageR.y, (clock.Length + 1) * _cw, _ch * 0.5f + Thin * 2f), Back);   // over the frame only: above it is the ground
            GUI.Label(new Rect(cx + _cw, _pageR.y - _ch * 0.5f + Thin, (clock.Length + 1) * _cw, _ch), "<b><color=" + Hot + ">" + Esc(clock) + "</color></b>", _st);
            if (dialog)
            {
                Behind(page.Parent);
                _pageR = pageR;
                Fill(ShadowOf(_dlgR), Shadow);
                Fill(_dlgR, Panel);
                Box(DialogFrame(), Lang.T(page.Path[page.Path.Length - 1]), false, Panel);
            }
            _pageR = pageR;
            int side = page.Side ? page.SideCols + 1 : 0;
            int cols = ColsFor(page), vis = _visRows;
            for (int i = 0; i < vis && page.Top + i < _vis.Count; i++)
            {
                int idx = page.Top + i; MenuRow row = _vis[idx];
                Rect r = new Rect(_pageR.x + _cw, _pageR.y + (i + 1) * _ch, Scrolls ? BarX(_pageR.xMax) - Gap - _pageR.x - _cw : _pageR.width - 2 * _cw - side * _cw, _ch);
                bool cur = idx == ci && pageOn && !_byMouse;
                // stays lit under an open pop-up, as a Far combobox keeps its focus while its list is open
                if (cur) Fill(r, Bar); else if (idx == over && row.Kind != RowKind.Header) Fill(r, Hover);
                if (row.Kind == RowKind.Header && dialog) Divider(r.y, Lang.T(row.Label));
                else if (row.Kind == RowKind.Header) Heading(r, Lang.T(row.Label), page.Side ? SideRect(page) : new Rect());
                else Line(r.x + _cw * 0.5f, r.y, RowText(row, cols, cur));
                Knob(row, r.y, cols, cur);
            }
            if (_vis.Count > vis) ScrollBar(page.Top, vis, _vis.Count);

            // the preview: a Far dialog over the page beside the rows, with its shadow (as Far gives dialogs and pop-up
            // menus), its own lighter ground and a double frame, the samples in named groups inside it
            if (page.Dialog && page.Draw != null && !_searching)
            {
                // the samples under the rows, split from them by a line joined to the frame, as in Far's Color dialog
                float t = Thin, lineY = Mathf.Round(_pageR.y + (_visRows + 1) * _ch + _ch * 0.5f - t * 0.5f);
                Rect frame = DialogFrame();
                Fill(new Rect(frame.x + 2f * t, lineY, frame.width - 4f * t, t), Frame);
                page.Draw(new Rect(frame.x, lineY, frame.width, frame.yMax - lineY));
            }
            else if (page.Draw != null)
            {
                Rect sr = SideRect(page);
                // its shadow stops at the page's frame, as a pop-up's does
                float pt = Thin * 3f;
                Clipped(ShadowOf(sr), new Rect(_pageR.x + pt, _pageR.y + pt, _pageR.width - 2f * pt, _pageR.height - 2f * pt), Shadow);
                Fill(sr, Panel);
                var frame = new Rect(sr.x + _cw * 0.5f, sr.y + _ch * 0.5f, sr.width - _cw, sr.height - _ch);
                Box(frame, Lang.T("Preview"), false, Panel);
                page.Draw(frame);
            }

            // under an open dialog, so the dialog can be dragged over them
            if (!dlgOpen) Foot();
            if (_pop != null) PopDraw(mouse, mouseMoved);
        }

        // while a key is being set: the first thing pressed is it. Escape leaves it as it was; Enter (or Backspace) sets "none".
        private void PollBind()
        {
            if (Time.frameCount == _modeFrame) return;   // the press that started this is not the key
            string got = Monolith.Keys.Pressed();
            bool none = got == "<Keyboard>/backspace" || got == "<Keyboard>/enter" || got == "<Keyboard>/numpadEnter";
            // an Enter held when this started has to be let go before an Enter can mean "no key"
            if (!_bindEnterUp) { if (Monolith.Keys.EnterHeld()) { if (none) return; } else _bindEnterUp = true; }
            if (got == null) return;
            MenuRow row = _bindRow;
            _bindRow = null; _bindEnd = Time.frameCount;
            if (got == "<Keyboard>/escape") return;
            try { row.Entry.BoxedValue = none ? Monolith.Keys.NoKey(row.Entry) : Monolith.Keys.Taken(row.Entry, got); } catch (Exception e) { Warn("setting a keybind", e); }
        }

        // the page a dialog was opened from, as it was: its rows under the dialog, the one that opened it still lit
        private void Behind(MenuPage parent)
        {
            int cols = Mathf.RoundToInt((_behindR.width - 2 * _cw) / _cw) - 1, i = 0;
            foreach (MenuRow row in parent.Rows)
            {
                if (!Shows(row) || i >= PanelRows - 2) continue;
                Rect r = new Rect(_behindR.x + _cw, _behindR.y + (i + 1) * _ch, _behindR.width - 2 * _cw, _ch);
                if (row == parent.Cur) Fill(r, Bar);
                if (row.Kind == RowKind.Header) Heading(r, Lang.T(row.Label), new Rect());
                else Line(r.x + _cw * 0.5f, r.y, RowText(row, cols, row == parent.Cur));
                i++;
            }
        }

        // where a dialog is held to drag it: its top line, as Far's dialogs move by their frame
        private Rect DialogGrip() { return new Rect(_dlgR.x, _dlgR.y, _dlgR.width, _ch); }

        // Far's shadow: two cells to the right and one row down, over which what is under it stays readable but dark
        private Rect ShadowOf(Rect r) { return new Rect(r.x + 2f * _cw, r.y + _ch, r.width, r.height); }

        // a dialog's double frame: half a cell in from its filled ground, as Far draws one
        private Rect DialogFrame() { return new Rect(_dlgR.x + _cw * 0.5f, _dlgR.y + _ch * 0.5f, _dlgR.width - _cw, _dlgR.height - _ch); }

        private Rect SideRect(MenuPage page)
        {
            // the dialog with its half cell of ground around the frame: the title on the frame comes level with the first row
            // a cell of room on its left, and a cell and a half between it and the page frame on its right
            return new Rect(_pageR.xMax - (page.SideCols + 1.5f) * _cw, _pageR.y + _ch - Thin, (page.SideCols - 0.5f) * _cw, page.SideRows() * _ch);
        }

        private static string K(string key, string what) { return "<color=" + Hot + ">" + Lang.T(key) + "</color><color=" + Txt + "> " + Lang.T(what) + "</color>  "; }

        // "HUD \ Hitmarkers", each part in the menu's language (a profile's own name stays as it is)
        private static string PathText(MenuPage page)
        {
            if (!Lang.Ru) return string.Join(" \\ ", page.Path);
            var parts = new string[page.Path.Length];
            for (int i = 0; i < parts.Length; i++) parts[i] = Lang.T(page.Path[i]);
            return string.Join(" \\ ", parts);
        }

        private string RowText(MenuRow row, int cols, bool cur)
        {
            string name = cur ? Val : Txt;
            switch (row.Kind)
            {
                case RowKind.Up:
                {
                    string to = _page != null && _page.Parent != null ? Lang.T(_page.Parent.Path[_page.Parent.Path.Length - 1]) : "";
                    string back = Fit(Lang.T("back to ") + to, cols - 3);
                    return "<color=" + name + ">" + Pad("..", cols - back.Length) + "</color><color=" + Dim + ">" + Esc(back) + "</color>";
                }
                case RowKind.Link:
                {
                    string info = Fit(Side(row), 24);
                    return "<color=" + name + ">" + Esc(Pad(Lang.T(row.Label), cols - info.Length - 2)) + "</color><color=" + Dim + ">" + Esc(info) + "</color><color=" + Hot + "> ►</color>";
                }
                case RowKind.Action:
                {
                    string act = Lang.T(row.Label);
                    if (row == _editRow) return "<color=" + Mint + ">" + Esc(act) + ": </color>" + Typing(Fit(_edit, cols - act.Length - 4), false);
                    if (row == _armed) return "<color=" + Hot + ">" + Esc(Pad(act + Lang.T(_armedByClick ? ": click again" : ": E again"), cols)) + "</color>";
                    if (row.Take == null && row.Dyn != null)   // a line to read (credits): the text, its value on the right, nothing to press
                    {
                        string v = ""; try { v = row.Dyn() ?? ""; } catch (Exception e) { Warn("a row's value", e); }
                        return "<color=" + Mint + ">" + Esc(Pad(act, cols - v.Length)) + "</color><color=" + Dim + ">" + Esc(v) + "</color>";
                    }
                    if (row.Take == null) return "<color=" + Dim + ">" + Esc(Fit(act, cols)) + "</color>";   // a line of text a mod put on its page
                    if (row.Dyn != null)
                    {
                        string b = ""; try { b = row.Dyn() ?? ""; } catch (Exception e) { Warn("a row's value", e); }
                        return "<color=" + Mint + ">" + Esc(Pad(act, cols - b.Length)) + "</color><color=" + Dim + ">" + Esc(b) + "</color>";
                    }
                    // done on the spot: no ►, which means a page opens
                    return "<color=" + Mint + ">" + Esc(act) + "</color>";
                }
                case RowKind.Group:
                {
                    int on = 0; foreach (ConfigEntryBase e in row.Parts) if ((bool)e.BoxedValue) on++;
                    string v = on == row.Parts.Length ? "[x]" : on == 0 ? "[ ]" : "[-]";
                    return "<color=" + name + ">" + Esc(Pad(Lang.T(row.Label), cols - 4)) + "</color> <color=" + Val + ">" + v + "</color>";
                }
                default:
                {
                    int used; string value = ValueText(row, cols, out used);
                    string side = row == _editRow || row == _bindRow ? "" : Fit(Side(row), 16), label = Lang.T(row.Label);
                    if (side.Length > 0 && label.Length > cols - used - side.Length - 2) side = "";   // the name comes first
                    if (side.Length > 0) used += side.Length + 1;
                    return "<color=" + name + ">" + Esc(Pad(label, cols - used - 1)) + "</color> "
                        + (side.Length > 0 ? "<color=" + Dim + ">" + Esc(side) + "</color> " : "") + value;
                }
            }
        }

        private const int BarCells = 10, NumberCells = 6;
        private static readonly string BarGap = new string(' ', BarCells + 1);
        private static readonly Color LineRest = new Color32(0x6F, 0x85, 0x7B, 0xFF), KnobCur = new Color32(0xF0, 0xFC, 0x9D, 0xFF), KnobRest = new Color32(0xAD, 0xB3, 0xAC, 0xFF), Tick = new Color32(0x8F, 0xA5, 0x9B, 0xFF);

        // what a setting is set to, drawn as what it is: a switch, a key, a color, one of a list (▼ opens the list),
        // or a number with a bar that shows where it sits between its lowest and highest
        private string ValueText(MenuRow row, int cols, out int used)
        {
            ConfigEntryBase e = row.Entry; Type t = e.SettingType;
            string v;
            int room = Mathf.Clamp(cols - Lang.T(row.Label).Length - 3, 4, cols / 2);   // cells a list or text value may take
            if (row == _bindRow) { v = Lang.T("press a key…"); used = v.Length; return "<color=" + Hot + ">" + v + "</color>"; }
            if (row == _editRow && _capture) { v = Lang.T("press a key…"); used = v.Length; return "<color=" + Hot + ">" + Esc(v) + "</color>"; }
            if (row == _editRow) { v = Fit(_edit, room + 1); used = v.Length + 1; return Typing(v, _editFresh); }
            if (row.Bind) { v = Lang.T(Monolith.Keys.Name(e.GetSerializedValue(), row.Empty)); used = v.Length; return "<color=" + Val + ">" + Esc(v) + "</color>"; }
            if (t == typeof(Color))
            {
                Color c = (Color)e.BoxedValue;
                v = "#" + e.GetSerializedValue(); used = v.Length + 4;
                return "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">■</color> <color=" + Val + ">" + v + " ▼</color>";
            }
            if (t != typeof(bool) && IsPick(row))
            {
                v = Fit(LabelOf(row, e.BoxedValue), room); used = v.Length + 2;
                // the ▼ in the value's color: yellow is for keys, titles and headings
                return "<color=" + Val + ">" + Esc(v) + " ▼</color>";
            }
            if (Range(e, out _, out _))
            {
                // the slider itself is drawn over these cells (Knob)
                v = Show(e); if (v.Length > NumberCells) v = v.Substring(0, NumberCells);
                used = BarCells + 1 + NumberCells;
                return BarGap + "<color=" + Val + ">" + v.PadLeft(NumberCells) + "</color>";
            }
            v = Fit(row.Empty != null && e.GetSerializedValue().Length == 0 ? Lang.T(row.Empty) : Show(e), room + 2); used = v.Length;
            return "<color=" + Val + ">" + Esc(v) + "</color>";
        }

        // Some mods keep a key as text ("L", "Semicolon"): a text box made you type the name. A text setting called a key,
        // whose value is a key name or empty, asks for a key press like a real key setting, and gets the name written the
        // way it already was: the game's key names (KeyCode) or the input system's (Key: "Digit1", "Enter").
        private static bool TextKey(ConfigEntryBase e)
        {
            if (e == null || e.SettingType != typeof(string) || e.Definition == null) return false;
            string name = e.Definition.Key ?? "";
            if (!name.EndsWith("Key", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("Keybind", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("Hotkey", StringComparison.OrdinalIgnoreCase)) return false;
            string v = (e.BoxedValue as string ?? "").Trim();
            return v.Length == 0 || IsName<KeyCode>(v) || IsName<UnityEngine.InputSystem.Key>(v);
        }

        private static bool IsName<T>(string v) where T : struct { return Enum.TryParse(v, true, out T _) && !int.TryParse(v, out _); }

        private static string KeyName(ConfigEntryBase e, KeyCode k)
        {
            if (k == KeyCode.None) return "";
            string v = (e.BoxedValue as string ?? "").Trim();
            bool input = v.Length > 0 && !IsName<KeyCode>(v) && IsName<UnityEngine.InputSystem.Key>(v);
            if (input)
            {
                // the input system's name of the key just pressed
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null) foreach (var kc in kb.allKeys) if (kc != null && kc.wasPressedThisFrame) return kc.keyCode.ToString();
            }
            return k.ToString();
        }

        private static bool IsPick(MenuRow row)
        {
            if (row.Options != null) return true;
            ConfigEntryBase e = row.Entry; Type t = e.SettingType;
            if (t.IsEnum && t != typeof(KeyCode)) return true;
            AcceptableValueBase av = e.Description != null ? e.Description.AcceptableValues : null;
            return av != null && av.GetType().GetProperty("AcceptableValues") != null;
        }

        private static readonly Dictionary<ConfigEntryBase, Vector2> Ranges = new Dictionary<ConfigEntryBase, Vector2>();

        // the lowest and highest value of a number that has them; false for anything else
        private static bool Range(ConfigEntryBase e, out float a, out float b)
        {
            a = b = 0f;
            if (e.SettingType != typeof(float) && e.SettingType != typeof(int)) return false;
            Vector2 v;
            if (!Ranges.TryGetValue(e, out v))
            {
                v = new Vector2(float.NaN, float.NaN);
                try
                {
                    AcceptableValueBase av = e.Description != null ? e.Description.AcceptableValues : null;
                    PropertyInfo min = av != null ? av.GetType().GetProperty("MinValue") : null, max = av != null ? av.GetType().GetProperty("MaxValue") : null;
                    if (min != null && max != null) v = new Vector2(Convert.ToSingle(min.GetValue(av, null)), Convert.ToSingle(max.GetValue(av, null)));
                }
                catch (Exception ex) { Warn("a setting's range", ex); }
                Ranges[e] = v;
            }
            if (float.IsNaN(v.x) || v.y <= v.x) return false;
            a = v.x; b = v.y; return true;
        }

        // A number with a lowest and highest value is a slider: a thin line, a knob where the value is, a tick where the
        // default is. The knob is one cell wide and its middle is the value, so it travels BarCells - 1 cells.
        private bool IsSlider(MenuRow row, out float a, out float b)
        {
            a = b = 0f;
            if (row == null || row.Kind != RowKind.Entry || row.Bind || row == _editRow || row == _bindRow) return false;
            return row.Entry.SettingType != typeof(Color) && !IsPick(row) && Range(row.Entry, out a, out b);
        }

        private float SliderX(int cols) { return _pageR.x + _cw * 1.5f + (cols - BarCells - 1 - NumberCells) * _cw; }

        private void Knob(MenuRow row, float y, int cols, bool cur)
        {
            float a, b;
            if (!IsSlider(row, out a, out b)) return;
            float x = SliderX(cols), travel = (BarCells - 1) * _cw, t = Mathf.Max(2f, Mathf.Round(_ch / 12f)), mid = Mathf.Round(y + _ch * 0.5f);
            float f = Mathf.Clamp01((Convert.ToSingle(row.Entry.BoxedValue) - a) / (b - a)), df = Mathf.Clamp01((Convert.ToSingle(row.Entry.DefaultValue) - a) / (b - a));
            float kx = Mathf.Round(x + f * travel);
            Fill(new Rect(x, mid - t * 0.5f, BarCells * _cw, t), LineRest);
            Fill(new Rect(x, mid - t * 0.5f, kx - x + _cw * 0.5f, t), Color.white);
            Fill(new Rect(Mathf.Round(x + _cw * 0.5f + df * travel - t * 0.5f), mid + _ch * 0.2f, t, Mathf.Round(_ch * 0.2f)), Tick);
            Fill(new Rect(kx, Mathf.Round(y + _ch * 0.17f), _cw, Mathf.Round(_ch * 0.66f)), cur ? KnobCur : KnobRest);
        }

        // the mouse on a slider: the knob goes where the mouse is, in whole steps, and onto the default when it is close to it
        private void Slide(MenuRow row, float mouseX, MenuPage page)
        {
            float a, b;
            if (!IsSlider(row, out a, out b)) return;
            float travel = (BarCells - 1) * _cw;
            float f = Mathf.Clamp01((mouseX - SliderX(ColsFor(page)) - _cw * 0.5f) / travel);
            float df = Mathf.Clamp01((Convert.ToSingle(row.Entry.DefaultValue) - a) / (b - a));
            try
            {
                if (Mathf.Abs(f - df) * travel <= _cw * 0.4f) { row.Entry.BoxedValue = row.Entry.DefaultValue; return; }
                float step = Nice((b - a) / 100f), v = a + Mathf.Round(f * (b - a) / step) * step;
                if (row.Entry.SettingType == typeof(int)) row.Entry.BoxedValue = Mathf.Clamp(Mathf.RoundToInt(v), (int)a, (int)b);
                else row.Entry.BoxedValue = Mathf.Round(Mathf.Clamp(v, a, b) * 1000f) / 1000f;
            }
            catch (Exception e) { Warn("dragging a slider", e); }
        }

        private bool OnBar(MenuRow row, float mouseX, MenuPage page)
        {
            if (!IsSlider(row, out _, out _)) return false;
            float x0 = SliderX(ColsFor(page));
            return mouseX >= x0 - _cw * 0.5f && mouseX <= x0 + (BarCells + 0.5f) * _cw;
        }

        // which row of the page the mouse is on (a row that can be picked), or -1
        private int RowAt(Vector2 m, MenuPage page)
        {
            if (!_pageR.Contains(m) || m.y < _rowY0) return -1;
            if (page.Side && m.x >= SideRect(page).x - 10f) return -1;
            int i = page.Top + Mathf.FloorToInt((m.y - _rowY0) / _rowStep);
            if (i < 0 || i >= _vis.Count || i >= page.Top + _visRows || _vis[i].Kind == RowKind.Header) return -1;
            return i;
        }

        private static string Side(MenuRow row)
        {
            if (row.Dyn == null) return row.Kind == RowKind.Entry && row.Info != null ? Lang.T(row.Info) : "";
            try { return Lang.T(row.Dyn()) ?? ""; } catch { return ""; }
        }

        private static string Show(ConfigEntryBase e)
        {
            object v = e.BoxedValue; Type t = e.SettingType;
            if (t == typeof(bool)) return (bool)v ? "[x]" : "[ ]";
            if (t == typeof(float)) return ((float)v).ToString("0.###", CultureInfo.InvariantCulture);
            // keys bare: [x] in brackets is a switch
            if (t == typeof(KeyCode)) return v.ToString();
            if (t == typeof(KeyboardShortcut)) return e.GetSerializedValue();
            if (t == typeof(Color)) return "#" + e.GetSerializedValue();
            string s = e.GetSerializedValue();
            return t == typeof(string) && s.Length == 0 ? Lang.T("(empty)") : s;
        }

        private static MenuRow _hovered;

        private static string Describe(MenuRow row)
        {
            // a row a mod made with Mod Menu's API: told the cursor is on it, as Mod Menu tells it; the mod may then
            // put a text in the info panel, which is this line
            if (row != _hovered)
            {
                try { if (_hovered != null && _hovered.Unhover != null) _hovered.Unhover(); if (row.Hover != null) row.Hover(); } catch (Exception e) { Warn("a mod's hover", e); }
                _hovered = row;
            }
            if (row.Says != null)
            {
                string said = null;
                try { said = row.Says(); } catch (Exception e) { Warn("a mod's info text", e); }
                if (!string.IsNullOrEmpty(said)) return said;
            }
            switch (row.Kind)
            {
                case RowKind.Up: return Lang.T("Back one page.");
                case RowKind.Link: return row.To != null ? Lang.T(row.To.Info) ?? "" : "";
                case RowKind.Action: case RowKind.Group: return Lang.T(row.Info) ?? "";
                case RowKind.Entry:
                {
                    ConfigEntryBase e = row.Entry;
                    string d = row.Tip != null ? Lang.T(row.Tip) : Short(e.Description != null ? e.Description.Description ?? "" : "");
                    string def = Lang.T("Default: ") + (row.Bind && e.SettingType == typeof(string) ? Lang.T(Monolith.Keys.Name((string)e.DefaultValue, row.Empty)) : ShowDefault(e)) + ".";
                    return d.Length == 0 ? def : d + " " + def;
                }
                default: return "";
            }
        }

        // a mod's own description, cut to its first sentences: what fits in about two lines
        private static string Short(string d)
        {
            d = d.Replace('\n', ' ').Trim();
            if (d.Length <= 150) return d;
            int end = -1;
            for (int i = d.IndexOf(". ", StringComparison.Ordinal); i >= 0 && i < 150; i = d.IndexOf(". ", i + 1, StringComparison.Ordinal)) end = i;
            return end > 0 ? d.Substring(0, end + 1) : d.Substring(0, 147) + "…";
        }

        private static string ShowDefault(ConfigEntryBase e)
        {
            object d = e.DefaultValue;
            if (d is bool) return Lang.T((bool)d ? "on" : "off");
            if (d is float) return ((float)d).ToString("0.###", CultureInfo.InvariantCulture);
            if (d is Color) return "#" + ColorUtility.ToHtmlStringRGBA((Color)d);
            if (d is Lang.Language) return LabelOf(null, d);
            string s = d != null ? d.ToString() : "";
            return s.Length == 0 ? Lang.T("empty") : Lang.T(s);
        }

        private static string[] Wrap(string s, int width, int max)
        {
            var lines = new List<string>(); if (string.IsNullOrEmpty(s) || width < 8) return lines.ToArray();
            var sb = new StringBuilder();
            foreach (string part in s.Split(' '))
            {
                string word = part.Length > width ? Fit(part, width) : part;   // a path with no space in it
                if (sb.Length + word.Length + 1 > width)
                {
                    lines.Add(sb.ToString()); sb.Length = 0;
                    if (lines.Count == max) { lines[max - 1] = Fit(lines[max - 1] + " " + word, width - 1) + "…"; return lines.ToArray(); }
                }
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(word);
            }
            if (sb.Length > 0) lines.Add(sb.ToString());
            return lines.ToArray();
        }

        // ---- the search: every setting of every mod (also the ones no page has a row for) whose name or section holds what you typed

        // every row of every page, by its setting: the search shows a setting under the name its page gives it
        private Dictionary<ConfigEntryBase, MenuRow> _rowOf; private Dictionary<MenuRow, MenuPage> _pageOf;

        private void Index(MenuPage page)
        {
            foreach (MenuRow r in page.Rows)
            {
                if (r.Kind == RowKind.Entry && r.Entry != null && !_rowOf.ContainsKey(r.Entry)) { _rowOf[r.Entry] = r; _pageOf[r] = page; }
                else if (r.Kind == RowKind.Link && r.To != null && r.To.Parent == page) Index(r.To);
            }
        }

        private static bool Has(string text, string q) { return text != null && text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0; }

        private MenuPage SearchPage()
        {
            if (_searchPage != null) return _searchPage;
            var p = new MenuPage(null, "search");
            string q = _query.Trim();
            if (q.Length == 0) return _searchPage = p;
            if (_rowOf == null)
            {
                _rowOf = new Dictionary<ConfigEntryBase, MenuRow>(); _pageOf = new Dictionary<MenuRow, MenuPage>();
                foreach (MenuPage cat in _cats) Index(cat);
            }
            // the settings that have a row: found by the row's name (in either language), its page or the setting's own name
            foreach (var kv in _rowOf)
            {
                MenuRow r = kv.Value; MenuPage on = _pageOf[r];
                string label = r.Label.Trim(), where = on.Path[on.Path.Length - 1];
                if (!Has(label, q) && !Has(Lang.T(label), q) && !Has(where, q) && !Has(Lang.T(where), q) && !Has(kv.Key.Definition.Key, q)) continue;
                p.Rows.Add(new MenuRow { Kind = RowKind.Entry, Label = label, Info = where, Entry = r.Entry, Tip = r.Tip, Options = r.Options, Empty = r.Empty, Bind = r.Bind, Fixed = r.Fixed });
                if (p.Rows.Count >= 200) return _searchPage = p;
            }
            // and the ones no page has, by their own name
            foreach (string mod in MenuLayout.Mods)
            {
                ConfigFile f = MenuLayout.File(mod);
                if (f == null) continue;
                foreach (ConfigDefinition def in f.Keys)
                {
                    ConfigEntryBase e = f[def];
                    if (e == null || def.Section == "Internal" || Hidden.Contains(e) || _rowOf.ContainsKey(e)) continue;
                    string d = e.Description != null ? e.Description.Description : null;
                    if (d != null && d.IndexOf("Not used since", StringComparison.Ordinal) >= 0) continue;
                    if (!Has(def.Section + " " + def.Key, q)) continue;
                    p.Rows.Add(new MenuRow { Kind = RowKind.Entry, Label = def.Key, Info = def.Section, Entry = e });
                    if (p.Rows.Count >= 200) return _searchPage = p;
                }
            }
            return _searchPage = p;
        }

        // ---- input

        private void Move(MenuPage p, int ci, int d)
        {
            int n = _vis.Count; if (n == 0) return;
            int i = Mathf.Max(ci, 0), last = ci, step = d > 0 ? 1 : -1, left = Mathf.Abs(d);
            while (left > 0)
            {
                i += step;
                if (i < 0 || i >= n) break;
                if (_vis[i].Kind == RowKind.Header) continue;
                last = i; left--;
            }
            if (last >= 0) { p.Cur = _vis[last]; p.Last = last; }
            _armed = null; _say = null;
        }

        // the wheel acts on the panel under the mouse: over the categories it changes category, over the page it scrolls the
        // rows (the cursor, hidden while the mouse is in use, kept on a row in view)
        private void Wheel(MenuPage page, int ci, int d, Vector2 m)
        {
            if (_navR.Contains(m) && !_searching) { SetCat(_cat + (d > 0 ? 1 : -1)); return; }
            int n = _vis.Count; if (n == 0) return;
            page.Top = Mathf.Clamp(page.Top + d, 0, Mathf.Max(0, n - _visRows));
            int c = Mathf.Clamp(Mathf.Max(ci, 0), page.Top, Mathf.Min(n - 1, page.Top + _visRows - 1));
            if (_vis[c].Kind == RowKind.Header) c = c + 1 < n && c + 1 < page.Top + _visRows ? c + 1 : c - 1;
            if (c >= 0) { page.Cur = _vis[c]; page.Last = c; }
            _armed = null; _say = null;
        }

        private void SetCat(int i)
        {
            i = Mathf.Clamp(i, 0, _cats.Count - 1);
            _cat = i; _page = _cats[i];
        }

        private void GoBack()
        {
            if (_page != null && _page.Parent != null) _page = _page.Parent; else _inNav = true;
        }

        // R resets the row under the mouse when there is one, else the row the cursor is on
        // the row under the mouse as last drawn: a key press does not always carry where the mouse is, and then R reset the
        // row the keyboard cursor was on instead of the one you pointed at
        private MenuRow ResetTarget(Vector2 mouse, MenuPage page, MenuRow cursor)
        {
            int at = RowAt(mouse, page);
            return at >= 0 ? _vis[at] : cursor;
        }

        /// <summary>A setting that is not at its default (for a group: one of its parts is not).</summary>
        private static bool Changed(MenuRow row)
        {
            try
            {
                if (row.Kind == RowKind.Entry && row.Entry != null) return !AtDefault(row.Entry);
                if (row.Kind == RowKind.Group) foreach (ConfigEntryBase e in row.Parts) if (!AtDefault(e)) return true;
            }
            catch (Exception e) { Warn("reading a default", e); }
            return false;
        }

        private static bool AtDefault(ConfigEntryBase e)
        {
            return e.GetSerializedValue() == TomlTypeConverter.ConvertToString(e.DefaultValue, e.SettingType);
        }

        private static void Default(MenuRow row)
        {
            try
            {
                if (row.Kind == RowKind.Entry) row.Entry.BoxedValue = row.Entry.DefaultValue;
                else if (row.Kind == RowKind.Group) foreach (ConfigEntryBase e in row.Parts) e.BoxedValue = e.DefaultValue;
            }
            catch (Exception e) { Warn("resetting a setting", e); }
        }

        private void Keys(Event ev, MenuPage page, int ci)
        {
            KeyCode k = ev.keyCode; char ch = ev.character;
            MenuRow row = ci >= 0 ? _vis[ci] : null;
            // a key comes as two events, the key and then the character it types: the character of the key that just
            // started or ended an edit is not typed into anything
            if (k == KeyCode.None && Time.frameCount == _modeFrame) { ev.Use(); return; }
            // the escape that closed a menu opened from here (and brought this one back) is not for this one too
            if (k == KeyCode.Escape && Time.frameCount - _openFrame <= 1) { ev.Use(); return; }
            // a key being picked
            if (_capture)
            {
                if (k == KeyCode.None) { ev.Use(); return; }
                if (k == KeyCode.Escape) { _editRow = null; _capture = false; }
                else
                {
                    KeyCode set = k == KeyCode.Backspace ? KeyCode.None : k;
                    try
                    {
                        if (_editRow.Entry == Key && set == KeyCode.None) { }   // the menu's own key is never "none": there would be no way back in
                        else if (_editRow.Entry.SettingType == typeof(KeyCode)) _editRow.Entry.BoxedValue = set;
                        else if (_editRow.Entry.SettingType == typeof(string)) _editRow.Entry.BoxedValue = KeyName(_editRow.Entry, set);
                        else _editRow.Entry.BoxedValue = new KeyboardShortcut(set);
                    }
                    catch (Exception e) { Warn("setting a key", e); }
                    _editRow = null; _capture = false;
                }
                _modeFrame = Time.frameCount;
                ev.Use(); return;
            }
            // a value being typed
            if (_editRow != null)
            {
                if (k == KeyCode.Escape) { _editRow = null; _modeFrame = Time.frameCount; }
                else if (k == KeyCode.Return || k == KeyCode.KeypadEnter)
                {
                    MenuRow done = _editRow;
                    _editRow = null; _modeFrame = Time.frameCount;
                    if (done.Take != null) Took(done, _edit);
                    else SetTyped(done.Entry, _edit);
                }
                else if (k == KeyCode.Backspace) { _editFresh = false; if (_edit.Length > 0) _edit = _edit.Substring(0, _edit.Length - 1); }
                else if (ch >= ' ' && ch != 127) { if (_editFresh) { _edit = ""; _editFresh = false; } if (_edit.Length < EditMax) _edit += ch; }
                ev.Use(); return;
            }
            if (_searching)
            {
                if (k == KeyCode.Escape) { _searching = false; _searchPage = null; }
                else if (k == KeyCode.UpArrow) Move(page, ci, -1);
                else if (k == KeyCode.DownArrow) Move(page, ci, 1);
                else if (k == KeyCode.LeftArrow || k == KeyCode.RightArrow) { if (row != null) Change(row, k == KeyCode.RightArrow ? 1 : -1, ev.shift); }
                else if (k == KeyCode.Return || k == KeyCode.KeypadEnter) { if (row != null) Activate(row); }
                else if (k == KeyCode.Delete) { MenuRow r = ResetTarget(_lastMouse, page, row); if (r != null) Default(r); }
                else if (k == KeyCode.Backspace) { if (_query.Length > 0) { _query = _query.Substring(0, _query.Length - 1); _searchPage = null; } }
                else if (_query.Length == 0 && (ch == '/' || ch == '.') && Time.frameCount - _modeFrame <= 1) { }   // the character of the key that opened the search
                else if (ch >= ' ' && ch != 127 && _query.Length < 40) { _query += ch; _searchPage = null; }
                ev.Use(); return;
            }
            // Esc closes a dialog, as in Far, and only the menu itself when no dialog is open; held until let go either
            // way, so the game never sees it
            if (k == KeyCode.Escape) { EscHeld = true; _escFrame = Time.frameCount; if (page.Dialog) GoBack(); else Close(); ev.Use(); return; }
            // the menu's own key is read by the key watcher (once per press, not once per key repeat)
            if (k == Key.Value && k != KeyCode.None) { ev.Use(); return; }
            if (k == KeyCode.Tab) { _inNav = !_inNav; ev.Use(); return; }
            if (k == KeyCode.W) k = KeyCode.UpArrow; else if (k == KeyCode.S) k = KeyCode.DownArrow; else if (k == KeyCode.A) k = KeyCode.LeftArrow; else if (k == KeyCode.D) k = KeyCode.RightArrow;
            else if (k == KeyCode.R) k = KeyCode.Delete;
            // Q back and E open or take: they sit either side of W, so one hand does it all (Backspace and Enter are not them)
            else if (k == KeyCode.Backspace || k == KeyCode.Return || k == KeyCode.KeypadEnter) { ev.Use(); return; }
            // the search starts on the character, so it is the same key on every keyboard layout
            if (ch == '/' || k == KeyCode.Slash) { _searching = true; _query = ""; _searchPage = null; _modeFrame = Time.frameCount; ev.Use(); return; }
            // reset: the row under the mouse, wherever the keys are; without one, the cursor's row on the page
            if (k == KeyCode.Delete)
            {
                int at = RowAt(_lastMouse, page);
                MenuRow r = at >= 0 ? _vis[at] : _inNav ? null : row;
                if (r != null) Default(r);
                ev.Use(); return;
            }
            if (_inNav)
            {
                if (k == KeyCode.UpArrow) SetCat(_cat - 1);
                else if (k == KeyCode.DownArrow) SetCat(_cat + 1);
                else if (k == KeyCode.RightArrow || k == KeyCode.E || k == KeyCode.Space) _inNav = false;
                else return;
                ev.Use(); return;
            }
            if (k == KeyCode.UpArrow) Move(page, ci, -1);
            else if (k == KeyCode.DownArrow) Move(page, ci, 1);
            else if (k == KeyCode.PageUp) Move(page, ci, -Mathf.Max(1, _visRows - 1));
            else if (k == KeyCode.PageDown) Move(page, ci, Mathf.Max(1, _visRows - 1));
            else if (k == KeyCode.Q) GoBack();
            else if (k == KeyCode.LeftArrow) { if (row == null || row.Kind == RowKind.Up) GoBack(); else Change(row, -1, ev.shift); }
            else if (k == KeyCode.RightArrow) { if (row != null) { if (row.Kind == RowKind.Link) Activate(row); else Change(row, 1, ev.shift); } }
            else if (k == KeyCode.E || k == KeyCode.Space) { if (row != null) Activate(row); }
            else return;
            ev.Use();
        }

        private MenuRow _resetRow, _told;
        private Rect _resetRect;

        private void Click(Vector2 m, MenuPage page, int button)
        {
            // a click away from a value being typed keeps it, if it is one the setting takes; an action's text is dropped
            if (_editRow != null)
            {
                if (!_capture && _editRow.Take == null && _edit.Length > 0)
                    SetTyped(_editRow.Entry, _edit);
                _editRow = null; _capture = false;
            }
            if (_navR.Contains(m))
            {
                int i = Mathf.FloorToInt((m.y - _navY0) / _navStep);
                if (button == 0 && i >= 0 && i < _cats.Count) { _searching = false; _searchPage = null; SetCat(i); _inNav = true; }   // a click in a panel makes it the active one, as in Far
                return;
            }
            int at = RowAt(m, page);
            if (at < 0) return;
            MenuRow row = _vis[at];
            // One click moves the cursor to the row and does what E does, as in a game's own settings. The row under
            // the mouse is shaded and described in the box below, so it can be read before it is clicked.
            _inNav = false;
            if (page.Cur != row) { _armed = null; _say = null; }
            page.Cur = row; page.Last = at;
            // On a number's bar: the value goes to the mouse, and follows it while held.
            if (button != 0) return;
            if (OnBar(row, m.x, page)) { _dragRow = row; Slide(row, m.x, page); return; }
            _clicking = true;
            try { Activate(row); } finally { _clicking = false; }
        }

        private void Activate(MenuRow row)
        {
            switch (row.Kind)
            {
                case RowKind.Up: GoBack(); return;
                case RowKind.Link:
                    _searching = false; _searchPage = null; _page = row.To;
                    // a page opens on its first setting, never on the ".." it was left by
                    if (_page.Cur == UpRow) { _page.Cur = null; _page.Last = 0; }
                    return;
                case RowKind.Action:
                {
                    if (row.Take != null)
                    {
                        if (row.Ask != null) { _editRow = row; _capture = false; _edit = ""; _editFresh = false; _modeFrame = Time.frameCount; return; }
                        if (row.Confirm && _armed != row) { _armed = row; _armedByClick = _clicking; return; }
                        Took(row, null);
                        return;
                    }
                    return;
                }
                case RowKind.Group:
                {
                    bool all = true; foreach (ConfigEntryBase p in row.Parts) if (!(bool)p.BoxedValue) all = false;
                    Change(row, all ? -1 : 1, false);
                    return;
                }
                case RowKind.Entry: break;
                default: return;
            }
            ConfigEntryBase e = row.Entry; Type t = e.SettingType;
            // MONOLITH's own keys are text (an input path); another mod's KeyCode or KeyboardShortcut is set by the key capture below
            if (row.Bind && t == typeof(string)) { _bindRow = row; _bindEnterUp = false; _modeFrame = Time.frameCount; return; }
            if (t == typeof(bool)) { e.BoxedValue = !(bool)e.BoxedValue; return; }
            if (t == typeof(KeyCode) || t == typeof(KeyboardShortcut) || TextKey(e)) { _editRow = row; _capture = true; _modeFrame = Time.frameCount; return; }
            // one of several, or a color: a list or a palette opens over the page
            if (t == typeof(Color) || IsPick(row))
            {
                MenuPage page = Current(); int ci = _vis.IndexOf(row);
                if (t == typeof(Color)) OpenPalette(row, page, ci); else OpenList(row, page, ci);
                _modeFrame = Time.frameCount;
                return;
            }
            _editRow = row; _capture = false; _modeFrame = Time.frameCount;
            _edit = t == typeof(float) ? ((float)e.BoxedValue).ToString("0.###", CultureInfo.InvariantCulture) : e.GetSerializedValue();
            _editFresh = true;
        }

        // a row's own action: its answer is shown, and a page that it took away is left
        private void Took(MenuRow row, string typed)
        {
            _armed = null;
            try { Say(row.Take(typed)); } catch (Exception ex) { Say(Lang.T("Did not work: ") + ex.Message); }
            while (_page != null && _page.Parent != null)
            {
                bool there = false;
                foreach (MenuRow r in _page.Parent.Rows) if (r.To == _page) there = true;
                if (there) break;
                _page = _page.Parent;
            }
        }

        // white, red, yellow, green, cyan, blue, magenta (the row ezCrosshair's own window had)
        private static readonly Color32[] MainColors =
        {
            new Color32(255, 255, 255, 255), new Color32(255, 0, 0, 255), new Color32(255, 255, 0, 255), new Color32(0, 255, 0, 255),
            new Color32(0, 255, 255, 255), new Color32(0, 0, 255, 255), new Color32(255, 0, 255, 255)
        };

        private static object[] Choices(MenuRow row)
        {
            ConfigEntryBase e = row.Entry;
            if (row.Options != null)
            {
                // empty first, then what the game has; a value that is not among them stays a choice
                var pick = row.Fixed ? new List<object>() : new List<object> { "" };
                string[] o = null; try { o = row.Options(); } catch (Exception ex) { Warn("a row's choices", ex); }
                if (o != null) foreach (string s in o) if (!string.IsNullOrEmpty(s) && !pick.Contains(s)) pick.Add(s);
                object now = e.BoxedValue;
                if (!pick.Contains(now)) pick.Add(now);
                return pick.ToArray();
            }
            Type t = e.SettingType;
            if (t == typeof(Color))
            {
                var colors = new List<object>();
                foreach (Color32 c in MainColors) colors.Add((Color)c);
                if (!colors.Contains(e.BoxedValue)) colors.Insert(0, e.BoxedValue);
                return colors.ToArray();
            }
            if (t.IsEnum && t != typeof(KeyCode)) { Array a = Enum.GetValues(t); var o = new object[a.Length]; a.CopyTo(o, 0); return o; }
            AcceptableValueBase av = e.Description != null ? e.Description.AcceptableValues : null;
            if (av == null) return null;
            PropertyInfo p = av.GetType().GetProperty("AcceptableValues");
            if (p == null) return null;
            Array vals = p.GetValue(av, null) as Array;
            if (vals == null) return null;
            var list = new object[vals.Length]; vals.CopyTo(list, 0); return list;
        }

        // a step that reads well: 1, 2 or 5 times a power of ten, the nearest to what was asked
        private static float Nice(float raw)
        {
            if (raw <= 0f) return 0.01f;
            float pow = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(raw))), m = raw / pow;
            return (m < 1.5f ? 1f : m < 3.5f ? 2f : m < 7.5f ? 5f : 10f) * pow;
        }

        /// <summary>Left or right on a row: off or on, the next choice, or a step of a number. False when the row has none of these.</summary>
        private static bool Change(MenuRow row, int dir, bool big)
        {
            try
            {
                if (row.Kind == RowKind.Group)
                {
                    foreach (ConfigEntryBase p in row.Parts) p.BoxedValue = dir > 0;
                    return true;
                }
                if (row.Kind != RowKind.Entry) return false;
                ConfigEntryBase e = row.Entry; Type t = e.SettingType;
                if (t == typeof(bool)) { e.BoxedValue = dir > 0; return true; }
                object[] c = Choices(row);
                if (c != null && c.Length > 0)
                {
                    int i = Array.IndexOf(c, e.BoxedValue);
                    e.BoxedValue = c[((i < 0 ? 0 : i) + dir + c.Length) % c.Length];
                    return true;
                }
                if (t != typeof(float) && t != typeof(int)) return false;
                AcceptableValueBase av = e.Description != null ? e.Description.AcceptableValues : null;
                PropertyInfo min = av != null ? av.GetType().GetProperty("MinValue") : null, max = av != null ? av.GetType().GetProperty("MaxValue") : null;
                bool ranged = min != null && max != null;
                if (t == typeof(int))
                {
                    int v = (int)e.BoxedValue, a = ranged ? (int)min.GetValue(av, null) : int.MinValue, b = ranged ? (int)max.GetValue(av, null) : int.MaxValue;
                    int step = ranged && b - a > 1000 ? Mathf.Max(1, Mathf.RoundToInt(Nice((b - a) / 300f))) : 1;
                    e.BoxedValue = Mathf.Clamp(v + dir * step * (big ? 10 : 1), a, b);
                }
                else
                {
                    float v = (float)e.BoxedValue, a = ranged ? (float)min.GetValue(av, null) : float.MinValue, b = ranged ? (float)max.GetValue(av, null) : float.MaxValue;
                    float step = ranged ? Nice((b - a) / 100f) : Nice(Mathf.Max(Mathf.Abs(v), 0.2f) / 20f);
                    v = Mathf.Clamp(v + dir * step * (big ? 10f : 1f), a, b);
                    e.BoxedValue = Mathf.Round(v * 1000f) / 1000f;
                }
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>While the menu is open the game's pause script waits (it would lock the cursor again every frame), and the
    /// escape that closes the menu does not reach the game's own menus.</summary>
    internal static class MenuHooks
    {
        internal static void Install(Harmony h)
        {
            Type me = typeof(MenuHooks);
            h.Patch(AccessTools.Method(typeof(PauseManager), "Update"), new HarmonyMethod(me, nameof(PausePrefix)));
            h.Patch(AccessTools.Method(typeof(PauseManager), "Menu"), new HarmonyMethod(me, nameof(EscapePrefix)));
            MethodInfo tween = AccessTools.Method(typeof(WindowPositionTween), "Update");
            if (tween != null) h.Patch(tween, new HarmonyMethod(me, nameof(EscapePrefix)));
            // the match chat opens on Enter and then takes the keyboard every frame: it waits while the menu is open
            // (Enter is the menu's own key), the way the STRAFTH menu does it
            MethodInfo chat = AccessTools.Method(typeof(MatchChat), "Update");
            if (chat != null) h.Patch(chat, new HarmonyMethod(me, nameof(ChatPrefix)));
        }

        private static bool ChatPrefix() { return !BundleMenu.IsOpen; }

        // Other mods' prefixes on the chat's Update still run when ours skips it: ChatCommands opens the chat on "/", which is
        // the menu's search key. The first time the menu opens, each of those prefixes gets this one
        // in front of it, so it waits while the menu is open too. Once: the mods are all loaded by then.
        private static bool _othersHeld;
        internal static void HoldOtherChatPrefixes()
        {
            if (_othersHeld) return;
            _othersHeld = true;
            try
            {
                MethodInfo chat = AccessTools.Method(typeof(MatchChat), "Update");
                var info = chat != null ? Harmony.GetPatchInfo(chat) : null;
                if (info == null) return;
                var h = new Harmony("dorsth.straftat.monolith.chatwait");
                var wait = new HarmonyMethod(typeof(MenuHooks), nameof(ChatPrefix));
                foreach (var p in info.Prefixes)
                {
                    if (p.PatchMethod == null || p.PatchMethod.DeclaringType == typeof(MenuHooks)) continue;
                    try { h.Patch(p.PatchMethod, wait); } catch (Exception e) { BundleMenu.Warn("holding another mod's chat keys", e); }
                }
            }
            catch (Exception e) { BundleMenu.Warn("holding other mods' chat keys", e); }
        }

        private static bool PausePrefix(PauseManager __instance)
        {
            if (!BundleMenu.IsOpen) return true;
            __instance.pause = true;
            return false;
        }

        private static bool EscapePrefix() { return !BundleMenu.IsOpen && !BundleMenu.SwallowEscape; }
    }
}
