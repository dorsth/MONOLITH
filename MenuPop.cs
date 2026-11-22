// MONOLITH - the menu's pop-ups: a list to pick one of several from, and a palette to pick a color from. Dorsth was here.
//
// Both open on Enter (or a click) on a row and lie over the page, in the manner of a Far dialog. What the cursor is on
// is applied at once, so you see or hear it; Enter keeps it, Escape (or a click beside it) puts back what was there.
// A list: up and down, page up and down, or type the first letters of a name. A palette: the arrows, or type a hex value.
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
            public float Pitch;                // the palette: cells from one color to the next
            public string Typed = ""; public float TypedAt;
            public float AnchorRight, AnchorY;
            public bool Beside;                // the page has a preview: the pop-up stops a cell short of it
            public bool Free, Dragging;        // the view was scrolled away from the cursor (wheel, scroll bar); the scroll bar is held
            public Rect R; public int Shown;
        }

        private Pop _pop;
        private const int PaletteCols = 12, ListRows = 14;
        // the palette is as wide as the row it sets: its frame on the row's edges, the colors spread over it
        private const float PalLeft = 2f, SwatchShare = 0.83f;

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
            p.AnchorRight = _pageR.xMax - _cw - (page.Draw != null ? (page.SideCols + 1) * _cw : 0f);
            p.AnchorY = _rowY0 + (ci - page.Top + 1) * _rowStep;
            p.Beside = page.Draw != null;
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
            // top row: white to black; under it every hue, light, full, dim and dark
            for (int i = 0; i < PaletteCols; i++) { float g = 1f - i / (PaletteCols - 1f); values.Add(new Color(g, g, g, was.a)); }
            float[] sat = { 0.4f, 1f, 1f, 1f }, val = { 1f, 1f, 0.7f, 0.42f };
            for (int r = 0; r < sat.Length; r++)
                for (int i = 0; i < PaletteCols; i++) { Color c = Color.HSVToRGB(i / (float)PaletteCols, sat[r], val[r]); c.a = was.a; values.Add(c); }
            var p = new Pop { Row = row, Was = was, Values = values.ToArray(), Palette = true, Cols = PaletteCols };
            // the cursor starts on the color nearest to the one in use
            float best = float.MaxValue;
            for (int i = 0; i < p.Values.Length; i++)
            {
                Color c = (Color)p.Values[i];
                float d = (c.r - was.r) * (c.r - was.r) + (c.g - was.g) * (c.g - was.g) + (c.b - was.b) * (c.b - was.b);
                if (d < best) { best = d; p.Cur = i; }
            }
            Anchor(p, page, ci);
            _pop = p;
        }

        private void PopApply() { try { _pop.Row.Entry.BoxedValue = _pop.Values[_pop.Cur]; } catch { } }

        private void PopClose(bool keep)
        {
            Pop p = _pop; _pop = null;
            if (p == null) return;
            if (!keep) { try { if (!Equals(p.Row.Entry.BoxedValue, p.Was)) p.Row.Entry.BoxedValue = p.Was; } catch { } }
            _modeFrame = Time.frameCount;
        }

        private void PopMove(int to)
        {
            Pop p = _pop;
            to = Mathf.Clamp(to, 0, p.Values.Length - 1);
            p.Free = false;
            if (to == p.Cur) return;
            p.Cur = to; p.Typed = p.Palette ? p.Typed : p.Typed;
            PopApply();
        }

        private void PopKeys(Event ev)
        {
            Pop p = _pop; KeyCode k = ev.keyCode; char ch = ev.character;
            if (k == KeyCode.None && Time.frameCount == _modeFrame) { ev.Use(); return; }
            if (k == KeyCode.Escape) { PopClose(false); ev.Use(); return; }
            if (k == KeyCode.Return || k == KeyCode.KeypadEnter)
            {
                if (p.Palette && p.Typed.Length > 0 && !TryHex(p)) { p.Typed = ""; ev.Use(); return; }
                PopClose(true); ev.Use(); return;
            }
            if (k == KeyCode.UpArrow) { p.Typed = ""; PopMove(p.Cur - p.Cols); }
            else if (k == KeyCode.DownArrow) { p.Typed = ""; PopMove(p.Cur + p.Cols); }
            else if (k == KeyCode.LeftArrow) { p.Typed = ""; PopMove(p.Cur - 1); }
            else if (k == KeyCode.RightArrow) { p.Typed = ""; PopMove(p.Cur + 1); }
            else if (k == KeyCode.PageUp) PopMove(p.Cur - (p.Palette ? p.Cols : ListRows - 1));
            else if (k == KeyCode.PageDown) PopMove(p.Cur + (p.Palette ? p.Cols : ListRows - 1));
            else if (k == KeyCode.Home) PopMove(0);
            else if (k == KeyCode.End) PopMove(p.Values.Length - 1);
            else if (k == KeyCode.Backspace) { if (p.Typed.Length > 0) p.Typed = p.Typed.Substring(0, p.Typed.Length - 1); }
            else if (ch > ' ' || (ch == ' ' && p.Typed.Length > 0))
            {
                if (p.Palette)
                {
                    // a hex value, typed: taken as soon as it is whole
                    if (Uri.IsHexDigit(ch) && p.Typed.Length < 8) { p.Typed += char.ToUpperInvariant(ch); if (p.Typed.Length == 6 || p.Typed.Length == 8) TryHex(p); }
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
            return true;
        }

        // which item of the pop-up the mouse is on, or -1
        private int PopAt(Vector2 m)
        {
            Pop p = _pop;
            if (!p.R.Contains(m)) return -1;
            if (p.Palette)
            {
                int col = Mathf.FloorToInt((m.x - p.R.x - _cw * (PalLeft - p.Pitch * (1f - SwatchShare) / 2f)) / (_cw * p.Pitch)), row = Mathf.FloorToInt((m.y - p.R.y - _ch) / _ch);
                if (col < 0 || col >= p.Cols || row < 0) return -1;
                int i = row * p.Cols + col;
                return i < p.Values.Length ? i : -1;
            }
            int line = Mathf.FloorToInt((m.y - p.R.y - _ch) / _ch);
            if (line < 0 || line >= p.Shown) return -1;
            return p.Top + line;
        }

        // the scroll bar of a long list: one cell wide on its right frame, over its rows
        private Rect PopBar(Pop p) { return new Rect(BarX(p.R.xMax - _cw * 0.5f), p.R.y + _ch, _cw, p.Shown * _ch); }

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
            int i = PopAt(ev.mousePosition);
            if (ev.button != 0 || i < 0) { PopClose(false); return; }
            PopMove(i); PopApply(); PopClose(true);
        }

        // the pop-up as wide as its row: its frame on the row's left edge and, beside a preview, a cell short of it
        private float Span(Pop p) { return p.AnchorRight - _pageR.x - (p.Beside ? _cw : 0f); }

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
            if (p.Palette)
            {
                w = Span(p);
                p.Pitch = (w / _cw - 2f * PalLeft) / (p.Cols - 1f + SwatchShare);
                h = (n / p.Cols + 2) * _ch;   // the hex value is on the bottom line of the frame
            }
            else
            {
                string title = Lang.T(p.Row.Label.Trim());
                int longest = Mathf.Max(8, title.Length);   // never narrower than its own title
                foreach (string s in p.Labels) if (s.Length > longest) longest = s.Length;
                p.Shown = Mathf.Min(n, ListRows);
                // as wide as the row, so it hides whole rows and never half of a name; wider only for a long name
                w = Mathf.Max(Span(p), (Mathf.Min(longest, 40) + 6) * _cw); h = (p.Shown + 2) * _ch;
            }
            // inside the page; under the row, or above it when there is no room under it. Its half cell of filled
            // room stops just inside the double line of the page.
            float edge = _cw * 0.5f;
            float x = Mathf.Clamp(p.AnchorRight - (p.Beside ? _cw : 0f) - w, _pageR.x + edge, _pageR.xMax - w - edge), y = p.AnchorY;
            x = Mathf.Clamp(x, _win.x + _cw, _win.xMax - w - _cw);
            if (y + h > _win.yMax - _ch * 1.5f) y = Mathf.Max(_win.y + _ch * 0.5f, p.AnchorY - _rowStep - h);
            p.R = new Rect(Mathf.Round(x), Mathf.Round(y), w, h);
            if (mouseMoved && !p.Dragging && !(p.Values.Length > p.Shown && !p.Palette && PopBar(p).Contains(mouse))) { int at = PopAt(mouse); if (at >= 0) PopMove(at); }

            // a filled panel with half a cell of room around its frame, as Far draws a dialog; never over the page's own lines
            var frame = new Rect(p.R.x + _cw * 0.5f, p.R.y + _ch * 0.5f, p.R.width - _cw, p.R.height - _ch);
            float pt = Thin * 3f;
            var room = new Rect(_pageR.x + pt, _pageR.y + pt, _pageR.width - 2f * pt, _pageR.height - 2f * pt);
            // with the shadow the preview dialog has, half a cell down and to the right
            Clipped(new Rect(p.R.x + _cw * 0.5f, p.R.y + _ch * 0.5f, p.R.width, p.R.height), room, Shadow);
            Clipped(p.R, room, Panel);
            Box(frame, Lang.T(p.Row.Label.Trim()), true, Panel);
            if (p.Palette)
            {
                for (int i = 0; i < n; i++)
                {
                    int col = i % p.Cols, row = i / p.Cols;
                    float cx = Mathf.Round(p.R.x + _cw * (PalLeft + col * p.Pitch)), cy = p.R.y + _ch + row * _ch, sw = Mathf.Round(_cw * p.Pitch * SwatchShare), sh = _ch - 6f;
                    Color c = (Color)p.Values[i];
                    // the one picked: a line right around it
                    if (i == p.Cur) { float t = Thin; Fill(new Rect(cx - t, cy + 3f - t, sw + 2f * t, sh + 2f * t), KnobCur); }
                    Fill(new Rect(cx, cy + 3f, sw, sh), new Color(c.r, c.g, c.b, 1f));
                }
                string hex = p.Typed.Length > 0 ? "<color=" + Hot + ">#" + p.Typed + "_</color>" : "<color=" + Val + ">#" + p.Row.Entry.GetSerializedValue() + "</color>";
                // on the bottom line of the frame, in the middle, as Far puts its totals there
                int len = (p.Typed.Length > 0 ? p.Typed.Length + 2 : p.Row.Entry.GetSerializedValue().Length + 1) + 2;
                float hw = len * _cw, hx = Mathf.Round(p.R.x + (p.R.width - hw) * 0.5f), hy = p.R.yMax - _ch;
                Fill(new Rect(hx, hy, hw, _ch), Panel);
                GUI.Label(new Rect(hx + _cw, hy, hw, _ch), hex, _st);
                return;
            }
            // the cursor is kept in view, unless the list was scrolled away from it
            if (!p.Free && p.Cur < p.Top) p.Top = p.Cur;
            if (!p.Free && p.Cur >= p.Top + p.Shown) p.Top = p.Cur - p.Shown + 1;
            p.Top = Mathf.Clamp(p.Top, 0, Mathf.Max(0, n - p.Shown));
            int cols = (int)(p.R.width / _cw) - 6;
            for (int i = 0; i < p.Shown; i++)
            {
                int idx = p.Top + i;
                Rect r = new Rect(p.R.x + _cw * 1.5f, p.R.y + _ch + i * _ch, p.R.width - _cw * 3f, _ch);
                bool cur = idx == p.Cur, was = Equals(p.Values[idx], p.Was);
                if (cur) Fill(r, Bar);
                GUI.Label(new Rect(r.x + _cw * 0.5f, r.y, r.width, _ch), "<color=" + Hot + ">" + (was ? "√ " : "  ") + "</color><color=" + (cur ? Val : Txt) + ">" + Esc(Fit(p.Labels[idx], cols)) + "</color>", _st);
            }
            // a long list has the scroll bar a long page has, on its right frame
            if (n > p.Shown) { Rect b = PopBar(p); ScrollBar(b.x, b.y, p.Top, p.Shown, n, Panel); }
        }
    }
}
