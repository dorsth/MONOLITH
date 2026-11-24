// MONOLITH - the menu's pop-ups: a list to pick one of several from, and a palette to pick a color from. Dorsth was here.
//
// Both open on E (or a click) on a row and lie over the page, in the manner of a Far dialog. What the cursor is on
// is applied at once, so you see or hear it; E or Enter keeps it, Q or Escape puts back what was there (a click beside a list does too; beside the palette it keeps the color).
// A list: up and down, page up and down, or type the first letters of a name. A palette: Far's 16 console colors in two
// rows, the arrows to pick, a hex value typed for any other color, Delete for the default.
using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace Monolith
{
    internal sealed partial class BundleMenu
    {
        private sealed class Pop
        {
            public MenuRow Row; public object Was;
            public object[] Values; public string[] Labels;
            public bool Palette; public int Cols = 1, Cur, Top;
            public string Typed = ""; public float TypedAt; public bool Editing;   // Editing: the hex field was clicked
            public float AnchorRight, AnchorY;
            public bool Free, Dragging;        // the view was scrolled away from the cursor (wheel, scroll bar); the scroll bar is held
            public Rect R; public int Shown;
        }

        private Pop _pop;
        // a list shows up to 8 names at a time, as Far's dropdowns (CBoxMaxHeight), so it stays near its row
        private const int PaletteCols = 8, PaletteRows = 3, ListRows = 8, DefaultSlot = PaletteCols * PaletteRows;
        // the 16 colors of Far's Color dialog, in its order: the dark eight, then the bright eight; then eight between them
        private static readonly Color32[] Swatches =
        {
            new Color32(0x00, 0x00, 0x00, 0xFF), new Color32(0x00, 0x00, 0x80, 0xFF), new Color32(0x00, 0x80, 0x00, 0xFF), new Color32(0x00, 0x80, 0x80, 0xFF),
            new Color32(0x80, 0x00, 0x00, 0xFF), new Color32(0x80, 0x00, 0x80, 0xFF), new Color32(0x80, 0x80, 0x00, 0xFF), new Color32(0xC0, 0xC0, 0xC0, 0xFF),
            new Color32(0x80, 0x80, 0x80, 0xFF), new Color32(0x00, 0x00, 0xFF, 0xFF), new Color32(0x00, 0xFF, 0x00, 0xFF), new Color32(0x00, 0xFF, 0xFF, 0xFF),
            new Color32(0xFF, 0x00, 0x00, 0xFF), new Color32(0xFF, 0x00, 0xFF, 0xFF), new Color32(0xFF, 0xFF, 0x00, 0xFF), new Color32(0xFF, 0xFF, 0xFF, 0xFF),
            new Color32(0x40, 0x40, 0x40, 0xFF), new Color32(0x00, 0x80, 0xFF, 0xFF), new Color32(0x80, 0xFF, 0x00, 0xFF), new Color32(0x00, 0xFF, 0x80, 0xFF),
            new Color32(0xFF, 0x80, 0x00, 0xFF), new Color32(0xFF, 0x00, 0x80, 0xFF), new Color32(0xFF, 0xC0, 0x00, 0xFF), new Color32(0x80, 0x00, 0xFF, 0xFF),
        };

        private static string LabelOf(MenuRow row, object v)
        {
            // a language reads as its own name, whatever language the menu is in
            if (v is Lang.Language) return (Lang.Language)v == Lang.Language.Russian ? "Русский" : "English";
            if (v is string) { string s = (string)v; return s.Length == 0 ? Lang.T(row != null && row.Empty != null ? row.Empty : "(empty)") : Lang.T(s); }
            if (v is float) return ((float)v).ToString("0.###", CultureInfo.InvariantCulture);
            return v != null ? Lang.T(v.ToString()) : "";
        }

        // where a pop-up for this row hangs: under the row, its right edge on the row's
        private void Anchor(Pop p, MenuPage page, int ci)
        {
            p.AnchorRight = _pageR.xMax - _cw - (page.Side ? (page.SideCols + 1) * _cw : 0f);
            p.AnchorY = _rowY0 + (ci - page.Top + 1) * _rowStep;
        }

        private void OpenList(MenuRow row, MenuPage page, int ci)
        {
            object[] values = Choices(row);
            if (values == null || values.Length == 0) return;
            var p = new Pop { Row = row, Was = row.Entry.BoxedValue, Values = values, Labels = new string[values.Length] };
            for (int i = 0; i < values.Length; i++) p.Labels[i] = LabelOf(row, values[i]);
            p.Cur = Mathf.Max(0, Array.IndexOf(values, p.Was));
            Anchor(p, page, ci);
            _pop = p;
        }

        private void OpenPalette(MenuRow row, MenuPage page, int ci)
        {
            Color was = (Color)row.Entry.BoxedValue;
            var values = new List<object>();
            foreach (Color32 c32 in Swatches) { Color c = c32; c.a = was.a; values.Add(c); }
            values.Add((Color)row.Entry.DefaultValue);
            var p = new Pop { Row = row, Was = was, Values = values.ToArray(), Palette = true, Cols = PaletteCols, Cur = -1 };
            // the cursor starts on the color in use, if it is one of them; a color of your own has none
            for (int i = DefaultSlot - 1; i >= 0; i--) if (Same((Color)values[i], was)) { p.Cur = i; break; }
            Anchor(p, page, ci);
            _pop = p;
        }

        private static bool Same(Color a, Color b) { return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.004f; }

        private void PopApply() { if (_pop.Cur >= 0) try { _pop.Row.Entry.BoxedValue = _pop.Values[_pop.Cur]; } catch (Exception e) { Warn("picking from a list", e); } }

        private void PopClose(bool keep)
        {
            Pop p = _pop; _pop = null;
            if (p == null) return;
            if (!keep) { try { if (!Equals(p.Row.Entry.BoxedValue, p.Was)) p.Row.Entry.BoxedValue = p.Was; } catch (Exception e) { Warn("closing a list", e); } }
            _modeFrame = Time.frameCount;
        }

        private void PopMove(int to)
        {
            Pop p = _pop;
            to = Mathf.Clamp(to, 0, p.Values.Length - 1);
            p.Free = false;
            if (to == p.Cur) return;
            p.Cur = to; p.Editing = false;
            PopApply();
        }

        private void PopKeys(Event ev)
        {
            Pop p = _pop; KeyCode k = ev.keyCode; char ch = ev.character;
            if (k == KeyCode.None && Time.frameCount == _modeFrame) { ev.Use(); return; }
            // the menu's own keys work here too: WASD move, E takes, Q cancels, R resets a color. In the hex field, once
            // clicked or started with a digit, A, D and E are hex digits instead
            bool hex = p.Palette && p.Editing;
            char lc = char.ToLowerInvariant(ch);
            if (k == KeyCode.None && ("wasdeq".IndexOf(lc) >= 0 || (p.Palette && lc == 'r')) && !(hex && Uri.IsHexDigit(ch))) { ev.Use(); return; }   // the letter itself: used as a key above
            if (!(hex && (k == KeyCode.A || k == KeyCode.D || k == KeyCode.E)))
            {
                if (k == KeyCode.W) k = KeyCode.UpArrow; else if (k == KeyCode.S) k = KeyCode.DownArrow;
                else if (k == KeyCode.A) k = KeyCode.LeftArrow; else if (k == KeyCode.D) k = KeyCode.RightArrow;
                else if (k == KeyCode.E) k = KeyCode.Return; else if (k == KeyCode.Q) k = KeyCode.Escape;
                else if (k == KeyCode.R && p.Palette) k = KeyCode.Delete;
            }
            if (k == KeyCode.Escape) { PopClose(false); ev.Use(); return; }
            if (k == KeyCode.Return || k == KeyCode.KeypadEnter)
            {
                if (p.Palette && p.Typed.Length > 0 && !TryHex(p)) { p.Typed = ""; ev.Use(); return; }
                PopClose(true); ev.Use(); return;
            }
            // reset: the default, and the picker stays open
            if (p.Palette && k == KeyCode.Delete) { p.Typed = ""; PopMove(DefaultSlot); ev.Use(); return; }
            int c = p.Cur;
            // the palette moves in its rows of eight; a typed hex value goes on below
            if (p.Palette && (k == KeyCode.UpArrow || k == KeyCode.DownArrow || k == KeyCode.LeftArrow || k == KeyCode.RightArrow))
            {
                if (k == KeyCode.UpArrow) { p.Typed = ""; PopMove(c >= PaletteCols ? c - PaletteCols : Mathf.Max(c, 0)); }
                else if (k == KeyCode.DownArrow) { p.Typed = ""; PopMove(c < 0 ? 0 : c < DefaultSlot - PaletteCols ? c + PaletteCols : c); }
                else if (k == KeyCode.LeftArrow) { p.Typed = ""; PopMove(c < 0 ? 0 : c < DefaultSlot && c % PaletteCols > 0 ? c - 1 : c); }
                else if (k == KeyCode.RightArrow) { p.Typed = ""; PopMove(c < 0 ? 0 : c < DefaultSlot && c % PaletteCols < PaletteCols - 1 ? c + 1 : c); }
                ev.Use(); return;
            }
            else if (k == KeyCode.UpArrow) { p.Typed = ""; PopMove(p.Cur - p.Cols); }
            else if (k == KeyCode.DownArrow) { p.Typed = ""; PopMove(p.Cur + p.Cols); }
            else if (k == KeyCode.LeftArrow) { p.Typed = ""; PopMove(p.Cur - 1); }
            else if (k == KeyCode.RightArrow) { p.Typed = ""; PopMove(p.Cur + 1); }
            else if (k == KeyCode.PageUp && !p.Palette) PopMove(p.Cur - (ListRows - 1));
            else if (k == KeyCode.PageDown && !p.Palette) PopMove(p.Cur + (ListRows - 1));
            else if (k == KeyCode.Home && !p.Palette) PopMove(0);
            else if (k == KeyCode.End && !p.Palette) PopMove(p.Values.Length - 1);
            else if (k == KeyCode.Backspace) { if (p.Typed.Length > 0) p.Typed = p.Typed.Substring(0, p.Typed.Length - 1); }
            else if (ch > ' ' || (ch == ' ' && p.Typed.Length > 0))
            {
                if (p.Palette)
                {
                    // a hex value, typed: taken as soon as it is whole
                    if ((hex ? Uri.IsHexDigit(ch) : char.IsDigit(ch)) && p.Typed.Length < 8) { p.Editing = true; p.Typed += char.ToUpperInvariant(ch); if (p.Typed.Length == 6 || p.Typed.Length == 8) TryHex(p); }
                }
                else
                {
                    // the first letters of a name: the cursor goes to the first one that starts so
                    if (Time.unscaledTime - p.TypedAt > 1f) p.Typed = "";
                    p.Typed += char.ToLowerInvariant(ch); p.TypedAt = Time.unscaledTime;
                    for (int i = 0; i < p.Labels.Length; i++)
                        if (p.Labels[i].StartsWith(p.Typed, StringComparison.OrdinalIgnoreCase)) { PopMove(i); break; }
                }
            }
            ev.Use();
        }

        private static bool TryHex(Pop p)
        {
            Color c;
            if (!ColorUtility.TryParseHtmlString("#" + p.Typed, out c)) return false;
            if (p.Typed.Length == 6) c.a = ((Color)p.Was).a;
            try { p.Row.Entry.BoxedValue = c; } catch { return false; }
            // the cursor goes to the color typed, if it is one of them
            p.Cur = -1;
            for (int i = p.Values.Length - 1; i >= 0; i--) if (Same((Color)p.Values[i], c)) { p.Cur = i; break; }
            return true;
        }

        // which item of the pop-up the mouse is on, or -1
        private int PopAt(Vector2 m)
        {
            Pop p = _pop;
            if (!p.R.Contains(m)) return -1;
            if (p.Palette)
            {
                float gx, sw, hx; PalLayout(p, out gx, out sw, out hx);
                int col = Mathf.FloorToInt((m.x - gx) / sw), row = Mathf.FloorToInt((m.y - p.R.y - PalTop) / _ch);
                if (col < 0 || col >= PaletteCols || row < 0 || row >= PaletteRows) return -1;
                return row * PaletteCols + col;
            }
            int line = Mathf.FloorToInt((m.y - p.R.y - _ch) / _ch);
            if (line < 0 || line >= p.Shown) return -1;
            return p.Top + line;
        }

        // the scroll bar of a long list: one cell wide on its right frame, over its rows
        private Rect PopBar(Pop p) { return new Rect(BarX(p.R.xMax - _cw * 0.5f), p.R.y + _ch, _cw * BarW, p.Shown * _ch); }

        // the list shows from where the mouse is on its scroll bar
        private void PopScrollTo(Pop p, float y)
        {
            Rect b = PopBar(p);
            float f = Mathf.Clamp01((y - b.y - _ch) / Mathf.Max(1f, b.height - 2f * _ch));
            p.Top = Mathf.RoundToInt(f * Mathf.Max(0, p.Values.Length - p.Shown)); p.Free = true;
        }

        private void PopMouse(Event ev)
        {
            Pop p = _pop;
            bool scrolls = !p.Palette && p.Values.Length > p.Shown;
            // the wheel scrolls the list, as in Far; it does not change the setting
            if (ev.type == EventType.ScrollWheel) { if (scrolls) { p.Top = Mathf.Clamp(p.Top + (ev.delta.y > 0 ? 3 : -3), 0, p.Values.Length - p.Shown); p.Free = true; } return; }
            if (ev.type == EventType.MouseUp) { p.Dragging = false; return; }
            if (ev.type == EventType.MouseDrag) { if (p.Dragging) PopScrollTo(p, ev.mousePosition.y); return; }
            if (ev.type != EventType.MouseDown) return;
            if (scrolls && ev.button == 0 && PopBar(p).Contains(ev.mousePosition)) { p.Dragging = true; PopScrollTo(p, ev.mousePosition.y); return; }
            Vector2 m = ev.mousePosition;
            if (p.Palette && ev.button == 0 && HexField(p).Contains(m)) { p.Editing = true; p.Typed = ""; return; }
            // a click anywhere else leaves the field, and a half-typed value with it
            if (p.Palette) { p.Editing = false; p.Typed = ""; }
            if (p.Palette && ev.button == 0 && DefaultButton(p).Contains(m)) { p.Typed = ""; PopMove(DefaultSlot); PopClose(true); return; }
            int i = PopAt(m);
            // a click beside a list puts back what was there, since the mouse over it moved the cursor; beside the palette it
            // keeps the color picked, which only a click or a key chose. A click on it between its items does nothing
            if (ev.button != 0 || i < 0) { if (ev.button != 0 || !p.R.Contains(m)) PopClose(p.Palette); return; }
            PopMove(i); PopApply();
            if (!p.Palette) PopClose(true);
        }

        // The palette, laid out as Far's Color dialog: the colors at gx, sw wide each and touching, the hex value at hx. The colors take up to three cells each; the whole is centered in the frame.
        // room between the frame and the colors, above and below them
        private float PalTop { get { return 1.5f * _ch; } }

        // the hex value's sunken field
        private Rect HexField(Pop p)
        {
            float gx, sw, hx; PalLayout(p, out gx, out sw, out hx);
            return new Rect(hx, p.R.y + PalTop + _ch, 11f * _cw, _ch);
        }

        // under the hex value: Default. The button is exactly its text, so its fill under the mouse fits the brackets.
        private Rect DefaultButton(Pop p)
        {
            float gx, sw, hx; PalLayout(p, out gx, out sw, out hx);
            return new Rect(hx, p.R.y + PalTop + 2f * _ch, (Lang.T("Default").Length + 4) * _cw, _ch);
        }

        private void PalLayout(Pop p, out float gx, out float sw, out float hx)
        {
            float hexW = 14f * _cw, gap = 2f * _cw;
            sw = Mathf.Floor(Mathf.Min(3f * _cw, (p.R.width - 3f * _cw - gap - hexW) / PaletteCols));
            float all = PaletteCols * sw + gap + hexW;
            gx = Mathf.Round(p.R.x + (p.R.width - all) * 0.5f); hx = gx + PaletteCols * sw + gap;
        }

        private void PaletteDraw(Pop p)
        {
            float gx, sw, hx; PalLayout(p, out gx, out sw, out hx);
            float y0 = p.R.y + PalTop;
            for (int i = 0; i < DefaultSlot; i++)
            {
                Rect s = new Rect(gx + (i % PaletteCols) * sw, y0 + (i / PaletteCols) * _ch, sw, _ch);
                Color c = (Color)p.Values[i];
                Fill(s, new Color(c.r, c.g, c.b, 1f));
                if (i == p.Cur) Outline(s, KnobCur);
            }
            // any other color: its hex value in a sunken field as Far's, under the order of its digits. Clicked or typed in,
            // it shows the cursor, as every text being typed in the menu
            bool typing = p.Editing || p.Typed.Length > 0;
            Rect f = HexField(p);
            GUI.Label(new Rect(hx + _cw, y0, 13f * _cw, _ch), "<color=" + Txt + ">RRGGBBAA</color>", _st);
            Fill(f, Well);
            // clicked into and nothing typed yet: the value it has, grey, until the first digit replaces it
            string shown = !typing ? "<color=" + Val + ">" + Esc(p.Row.Entry.GetSerializedValue()) + "</color>"
                : p.Typed.Length == 0 ? Typing(p.Row.Entry.GetSerializedValue(), true) : Typing(p.Typed, false);
            GUI.Label(new Rect(f.x + _cw, f.y, 10f * _cw, _ch), shown, _st);
            Rect b = DefaultButton(p);
            bool over = b.Contains(Event.current.mousePosition);
            if (over) Fill(b, Badge);
            GUI.Label(b, "<color=" + (over ? Ink : Txt) + ">[ " + Esc(Lang.T("Default")) + " ]</color>", _st);
        }

        private void Outline(Rect s, Color c)
        {
            float t = Thin;
            Fill(new Rect(s.x, s.y, s.width, t), c); Fill(new Rect(s.x, s.yMax - t, s.width, t), c); Fill(new Rect(s.x, s.y, t, s.height), c); Fill(new Rect(s.xMax - t, s.y, t, s.height), c);
        }

        // the pop-up under its row, centered on it: its frame half a cell in from each end of the row, so its filled room
        // ends on the row's ends and stops short of a preview beside the row
        private float Span(Pop p) { return p.AnchorRight - _pageR.x - _cw; }

        private void Clipped(Rect r, Rect room, Color c)
        {
            float x0 = Mathf.Max(r.x, room.x), y0 = Mathf.Max(r.y, room.y), x1 = Mathf.Min(r.xMax, room.xMax), y1 = Mathf.Min(r.yMax, room.yMax);
            if (x1 > x0 && y1 > y0) Fill(new Rect(x0, y0, x1 - x0, y1 - y0), c);
        }

        private void PopDraw(Vector2 mouse, bool mouseMoved)
        {
            Pop p = _pop;
            int n = p.Values.Length;
            float w, h;
            // the palette: its rows of colors
            if (p.Palette) { w = Span(p); h = PaletteRows * _ch + 2f * PalTop; }
            else
            {
                string title = Lang.T(p.Row.Label.Trim());
                int longest = Mathf.Max(8, title.Length);   // never narrower than its own title
                foreach (string s in p.Labels) if (s.Length > longest) longest = s.Length;
                p.Shown = Mathf.Min(n, ListRows);
                // as Far's dropdowns: under the value, as wide as its longest name (at least 22 cells), so it covers
                // as little as it can; never wider than the row
                w = Mathf.Min(Span(p), Mathf.Max(22, Mathf.Min(longest, 40) + 6) * _cw); h = (p.Shown + 2) * _ch;
            }
            // inside the page; under the row, or above it when there is no room under it. Its half cell of filled
            // room stops just inside the double line of the page.
            float edge = _cw * 0.5f;
            float x = Mathf.Clamp(p.AnchorRight - w, _pageR.x + edge, _pageR.xMax - w - edge), y = p.AnchorY;
            // held by the menu's window; from a dialog by the screen, as a dialog can sit anywhere on it and a Far
            // dropdown opens over whatever is under it
            bool fromDialog = Current().Dialog && !_searching;
            Rect hold = fromDialog ? new Rect(0f, 0f, Screen.width, Screen.height + _ch * 1.5f) : _win;
            x = Mathf.Clamp(x, hold.x + _cw, hold.xMax - w - _cw);
            // a list near the bottom shows fewer names rather than leave the screen (or the menu), as long as 4 still
            // fit; only then it opens above its row, where in a dialog it covers the samples
            if (!p.Palette)
            {
                int fit = Mathf.FloorToInt((hold.yMax - _ch * 1.5f - p.AnchorY) / _ch) - 2;
                if (fit >= 4 && fit < p.Shown) { p.Shown = fit; h = (p.Shown + 2) * _ch; }
            }
            if (y + h > hold.yMax - _ch * 1.5f) y = Mathf.Max(hold.y + _ch * 0.5f, p.AnchorY - _rowStep - h);
            // the colors are a dialog of their own, as Far's Color dialog. Far centers that on the screen; here it is
            // centered on the dialog it came from, under the row, so it never covers that dialog's samples above it
            if (p.Palette && Current().Dialog && !_searching) x = _dlgR.center.x - w * 0.5f;
            p.R = new Rect(Mathf.Round(x), Mathf.Round(y), w, h);
            if (mouseMoved && !p.Palette && !p.Dragging && !(p.Values.Length > p.Shown && PopBar(p).Contains(mouse))) { int at = PopAt(mouse); if (at >= 0) PopMove(at); }

            // a filled panel with half a cell of room around its frame, as Far draws a dialog; never over the page's own lines
            var frame = new Rect(p.R.x + _cw * 0.5f, p.R.y + _ch * 0.5f, p.R.width - _cw, p.R.height - _ch);
            float pt = Thin * 3f;
            // from a dialog it may hang out of it, as a Far dropdown is a menu of its own over the screen: then only the
            // menu's window holds it, and it is filled and framed wherever it goes
            MenuPage on = Current();
            // filled as far as its frame goes: clipped to less than the frame, it showed the game through its lower part
            var room = p.Palette || (on.Dialog && !_searching) ? new Rect(0f, 0f, Screen.width, Screen.height) : new Rect(_pageR.x + pt, _pageR.y + pt, _pageR.width - 2f * pt, _pageR.height - 2f * pt);
            // with the shadow the dialogs have
            Clipped(ShadowOf(p.R), room, Shadow);
            Clipped(p.R, room, Panel);
            // the colors: a dialog, its double frame titled; a list: a Far dropdown, one line and no title, under its field
            if (p.Palette) Box(frame, Lang.T(p.Row.Label.Trim()), true, Panel);
            else Box(frame, null, true, Panel, 1);
            if (p.Palette) { PaletteDraw(p); return; }
            // the cursor is kept in view, unless the list was scrolled away from it
            if (!p.Free && p.Cur < p.Top) p.Top = p.Cur;
            if (!p.Free && p.Cur >= p.Top + p.Shown) p.Top = p.Cur - p.Shown + 1;
            p.Top = Mathf.Clamp(p.Top, 0, Mathf.Max(0, n - p.Shown));
            // the rows the same small gap inside the frame on both sides, or, in a long list, before its scroll bar
            bool bar = n > p.Shown;
            float inL = p.R.x + _cw * 0.5f + Thin * 3f + Gap, inR = bar ? PopBar(p).x - Gap : p.R.xMax - _cw * 0.5f - Thin * 3f - Gap;
            int cols = (int)((inR - inL) / _cw) - 3;
            for (int i = 0; i < p.Shown; i++)
            {
                int idx = p.Top + i;
                Rect r = new Rect(inL, p.R.y + _ch + i * _ch, inR - inL, _ch);
                bool cur = idx == p.Cur, was = Equals(p.Values[idx], p.Was);
                if (cur) Fill(r, MenuBar);   // a list is a menu: its cursor is the menu one
                GUI.Label(new Rect(r.x + _cw * 0.5f, r.y, r.width, _ch), "<color=" + Hot + ">" + (was ? "√ " : "  ") + "</color><color=" + (cur ? Val : Txt) + ">" + Esc(Fit(p.Labels[idx], cols)) + "</color>", _st);
            }
            // a long list has the scroll bar a long page has, on its right frame
            if (n > p.Shown) { Rect b = PopBar(p); ScrollBar(b.x, b.y, p.Top, p.Shown, n, Panel); }
        }
    }
}
