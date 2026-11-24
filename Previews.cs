// MONOLITH - the pictures beside a page: what a setting will look like. Dorsth was here.
//
// Drawn only while the menu is open on that page. Each one uses what the game or the mod itself uses, not a copy made
// for the menu: the crosshair's own picture and size, the hitmarker picture the mod makes for the current settings,
// the wardrobe pictures the game has for its outfits and hats. Nothing is made or kept for the previews themselves.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Monolith
{
    internal sealed partial class BundleMenu
    {
        private static readonly Color Light = new Color32(0xB9, 0xC2, 0xBC, 0xFF);

        // a line of text, centred on x
        private void Note(float x, float y, string text, string color) { Put(x, y, Lang.T(text), color); }

        private void Put(float x, float y, string text, string color)
        {
            GUI.Label(new Rect(Mathf.Round(x - text.Length * _cw * 0.5f), y, text.Length * _cw + 2f, _ch), "<color=" + color + ">" + text + "</color>", _st);
        }

        // a note in the middle of a box: on two lines when one line would touch its sides
        private void NoteIn(Rect box, string text, string color)
        {
            text = Lang.T(text);
            float x = box.x + box.width / 2f, y = box.y + box.height / 2f;
            int cut = -1;
            if (text.Length * _cw > box.width - _cw)
                for (int i = text.IndexOf(' '); i >= 0; i = text.IndexOf(' ', i + 1))
                    if (cut < 0 || Mathf.Max(i, text.Length - i - 1) < Mathf.Max(cut, text.Length - cut - 1)) cut = i;
            if (cut < 0) { Put(x, y - _ch / 2f, text, color); return; }
            Put(x, y - _ch, text.Substring(0, cut), color);
            Put(x, y, text.Substring(cut + 1), color);
        }

        // a sprite, as large as it fits in the box without changing its shape (or at the size given)
        private static void Pic(Rect box, Sprite s, Color tint, float w = 0f, float h = 0f)
        {
            Texture2D t = s.texture; Rect tr = s.textureRect;
            if (t == null || tr.width <= 0f || tr.height <= 0f) return;
            if (w <= 0f) { float k = Mathf.Min(box.width / tr.width, box.height / tr.height); w = tr.width * k; h = tr.height * k; }
            var at = new Rect(Mathf.Round(box.x + (box.width - w) * 0.5f), Mathf.Round(box.y + (box.height - h) * 0.5f), Mathf.Round(w), Mathf.Round(h));
            Color was = GUI.color; GUI.color = tint;
            GUI.DrawTextureWithTexCoords(at, t, new Rect(tr.x / t.width, tr.y / t.height, tr.width / t.width, tr.height / t.height), true);
            GUI.color = was;
        }

        // Inside the preview dialog, two groups as in Far's Color dialog: single line boxes named on their top line, side
        // by side or one under the other. Draws group i and returns where it shows its samples.
        private Rect Part(Rect box, int i, string title, bool under = false)
        {
            // half a row of room under the dialog's title before the first group's title, so the two titles don't stack
            float t = Thin, frame = 3f * t, top = Mathf.Round(_ch * 1.75f);
            var all = new Rect(box.x + frame + _cw, box.y + top, box.width - 2f * (frame + _cw), box.height - top - frame - Mathf.Round(_ch * 0.5f));
            float w = under ? all.width : Mathf.Floor((all.width - 2f * _cw) / 2f), h = under ? Mathf.Floor((all.height - _ch) / 2f) : all.height;
            var b = new Rect(Mathf.Round(under ? all.x : i == 0 ? all.x : all.xMax - w), Mathf.Round(under && i == 1 ? all.yMax - h : all.y), w, h);
            Box(b, Lang.T(title), false, Panel, 1);
            float pad = Mathf.Round(_cw * 0.8f), head = Mathf.Round(_ch * 0.5f) + pad * 0.5f;
            return new Rect(b.x + t + pad, b.y + head, b.width - 2f * (t + pad), b.height - head - t - pad);
        }

        // the dark group and the light group of a preview, filled
        private Rect Tone(Rect box, int i)
        {
            Rect s = Part(box, i, i == 0 ? "Dark" : "Light");
            Fill(s, i == 0 ? Color.black : Light);
            return s;
        }

        // ---- the crosshair: its own picture, size and color, on dark and on light

        internal void CrosshairPreview(Rect r)
        {
            try
            {
                if (EzCrosshair.EzCrosshairPlugin.Pick == null) return;
                Texture2D tex = EzCrosshair.Pictures.Get(EzCrosshair.EzCrosshairPlugin.Pick.Value);
                Vector2Int size = tex != null ? EzCrosshair.Overlay.ScreenSize(tex, Mathf.Clamp(EzCrosshair.EzCrosshairPlugin.Scale.Value, 0f, 2f)) : Vector2Int.zero;
                for (int i = 0; i < 2; i++)
                {
                    Rect s = Tone(r, i);
                    if (tex == null) continue;
                    GUI.BeginGroup(s);
                    Color was = GUI.color; GUI.color = EzCrosshair.EzCrosshairPlugin.Tint.Value;
                    GUI.DrawTexture(new Rect(Mathf.Floor((s.width - size.x) / 2f), Mathf.Floor((s.height - size.y) / 2f), size.x, size.y), tex);
                    GUI.color = was;
                    GUI.EndGroup();
                }
            }
            catch (Exception) { }
        }

        // ---- hitmarkers: bodyshot, headshot and (when it is on) kill color, each on dark and on light, at the size on screen

        private static GameObject _marker; private static float _markerAt = -99f;

        // how many rows of the box each hitmarker has: one more hitmarker makes the box this much higher and moves nothing
        internal const float MarkerRows = 3.5f;

        // the hitmarker most guns use: the mod knows it once a weapon is in the map; before that, any weapon the game has loaded
        private static GameObject Marker()
        {
            GameObject common = DorsthTweaks.Hitmarkers.Common();
            if (common != null) return common;
            if (_marker != null || Time.unscaledTime - _markerAt < 5f) return _marker;
            _markerAt = Time.unscaledTime;
            var users = new Dictionary<GameObject, int>(); int most = 0;
            foreach (Weapon w in Resources.FindObjectsOfTypeAll<Weapon>())
            {
                if (w == null || w.hitMarker == null) continue;
                int n; users.TryGetValue(w.hitMarker, out n); users[w.hitMarker] = ++n;
                if (n > most) { most = n; _marker = w.hitMarker; }
            }
            return _marker;
        }

        internal void HitmarkerPreview(Rect r)
        {
            try
            {
                if (DorsthTweaks.Hitmarkers.Hidden) { Note(r.x + r.width / 2f, r.y + r.height / 2f - _ch / 2f, "hidden", Dim); return; }
                GameObject marker = Marker();
                Image img = marker != null ? marker.GetComponent<Image>() : null;
                RectTransform rt = marker != null ? marker.transform as RectTransform : null;
                if (img == null || img.sprite == null || rt == null || rt.sizeDelta.x <= 0f)
                {
                    Note(r.x + r.width / 2f, r.y + r.height / 2f - _ch, "shown once a", Dim);
                    Note(r.x + r.width / 2f, r.y + r.height / 2f, "map has loaded", Dim);
                    return;
                }
                PauseManager pm = PauseManager.Instance;
                float px = DorsthTweaks.Hitmarkers.ScreenPx(pm != null ? pm.transform : null, rt.sizeDelta.x * marker.transform.localScale.x);
                float py = px * rt.sizeDelta.y / rt.sizeDelta.x;
                Sprite sp = img.sprite;
                if (DorsthTweaks.Hitmarkers.Resized())
                {
                    DorsthTweaks.GapKept.Made made = DorsthTweaks.GapKept.Get(sp, DorsthTweaks.Hitmarkers.Width, DorsthTweaks.Hitmarkers.Gap, DorsthTweaks.Hitmarkers.Length, px);
                    if (made != null && made.Sprite != null) { sp = made.Sprite; px *= made.GrowX; py *= made.GrowY; }
                }
                int n = DorsthTweaks.Hitmarkers.KillOn ? 3 : 2;
                for (int tone = 0; tone < 2; tone++)
                {
                    Rect s = Tone(r, tone);
                    float cell = MarkerRows * _ch, y0 = Mathf.Round((s.height - n * cell) / 2f);
                    GUI.BeginGroup(s);
                    for (int i = 0; i < n; i++)
                    {
                        Color c = (i == 0 ? DorsthTweaks.DorsthTweaksPlugin.BodyshotColor : i == 1 ? DorsthTweaks.DorsthTweaksPlugin.HeadshotColor : DorsthTweaks.DorsthTweaksPlugin.KillColor).Color;
                        Pic(new Rect(0f, y0 + i * cell, s.width, cell), sp, c, px, py);
                    }
                    GUI.EndGroup();
                }
            }
            catch (Exception) { }
        }

        // ---- players: the game's own wardrobe picture of each outfit and hat you picked

        internal void PlayersPreview(Rect r)
        {
            try
            {
                // enemies in the upper group and teammates in the lower, each a suit and a hat
                for (int side = 0; side < 2; side++)
                {
                    Rect s = Part(r, side, side == 0 ? "Enemies" : "Teammates", true);
                    string suit = (side == 0 ? Players.EnemyOutfit : Players.TeamOutfit).Value, top = (side == 0 ? Players.EnemyHat : Players.TeamHat).Value;
                    // nothing picked for either: said once, not once for the suit and once for the hat
                    if (string.IsNullOrEmpty(suit) && string.IsNullOrEmpty(top)) { NoteIn(s, "their own", Dim); continue; }
                    float w = Mathf.Floor((s.width - _cw) / 2f);
                    for (int hat = 0; hat < 2; hat++)
                    {
                        string name = hat == 0 ? suit : top;
                        var cell = new Rect(hat == 0 ? s.x : s.xMax - w, s.y, w, s.height);
                        Sprite pic = Players.Picture(hat == 1, name);
                        if (pic != null) Pic(cell, pic, Color.white);
                        else NoteIn(cell, string.IsNullOrEmpty(name) ? "their own" : "no picture", Dim);
                    }
                }
            }
            catch (Exception) { }
        }
    }
}
