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
        private void Note(float x, float y, string text, string color)
        {
            text = Lang.T(text);
            GUI.Label(new Rect(Mathf.Round(x - text.Length * _cw * 0.5f), y, text.Length * _cw + 2f, _ch), "<color=" + color + ">" + text + "</color>", _st);
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

        // ---- the crosshair: its own picture, size and color, on dark and on light

        internal void CrosshairPreview(Rect r)
        {
            try
            {
                if (EzCrosshair.EzCrosshairPlugin.Pick == null) return;
                Texture2D tex = EzCrosshair.Pictures.Get(EzCrosshair.EzCrosshairPlugin.Pick.Value);
                float half = Mathf.Floor(r.width / 2f);
                GUI.BeginGroup(r);
                Fill(new Rect(half, 0f, r.width - half, r.height), Light);
                if (tex != null)
                {
                    Vector2Int size = EzCrosshair.Overlay.ScreenSize(tex, Mathf.Clamp(EzCrosshair.EzCrosshairPlugin.Scale.Value, 0f, 2f));
                    Color was = GUI.color; GUI.color = EzCrosshair.EzCrosshairPlugin.Tint.Value;
                    for (int i = 0; i < 2; i++)
                    {
                        float cx = i == 0 ? half / 2f : half + (r.width - half) / 2f;
                        GUI.DrawTexture(new Rect(Mathf.Floor(cx - size.x / 2f), Mathf.Floor(r.height / 2f - size.y / 2f), size.x, size.y), tex);
                    }
                    GUI.color = was;
                }
                GUI.EndGroup();
            }
            catch (Exception) { }
        }

        // ---- hitmarkers: bodyshot, headshot and shotgun color, each on dark and on light, at the size on screen

        private static GameObject _marker; private static float _markerAt = -99f;

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
                float half = Mathf.Floor(r.width / 2f), row = r.height / 3f;
                GUI.BeginGroup(r);
                Fill(new Rect(half, 0f, r.width - half, r.height), Light);
                for (int i = 0; i < 3; i++)
                {
                    Color c = (i == 0 ? DorsthTweaks.DorsthTweaksPlugin.BodyshotColor : i == 1 ? DorsthTweaks.DorsthTweaksPlugin.HeadshotColor : DorsthTweaks.DorsthTweaksPlugin.ShotgunHitmarkerColor).Color;
                    Pic(new Rect(0f, i * row, half, row), sp, c, px, py);
                    Pic(new Rect(half, i * row, r.width - half, row), sp, c, px, py);
                }
                GUI.EndGroup();
            }
            catch (Exception) { }
        }

        // ---- players: the game's own wardrobe picture of each outfit and hat you picked

        internal void PlayersPreview(Rect r)
        {
            try
            {
                // room under the box's own title, the same space left and right of a picture, and a gap between the two groups
                float top = Mathf.Round(_ch * 0.5f), pad = Mathf.Round(_cw * 0.8f), half = (r.height - top) / 2f, w = r.width / 2f;
                for (int side = 0; side < 2; side++)
                {
                    float y = r.y + top + side * half;
                    Note(r.x + r.width / 2f, y, side == 0 ? "enemies" : "teammates", Head);
                    for (int hat = 0; hat < 2; hat++)
                    {
                        string name = (side == 0 ? (hat == 0 ? Players.EnemyOutfit : Players.EnemyHat) : (hat == 0 ? Players.TeamOutfit : Players.TeamHat)).Value;
                        var cell = new Rect(r.x + hat * w + pad, y + _ch + pad * 0.5f, w - 2f * pad, half - _ch - pad * 1.5f);
                        Sprite pic = Players.Picture(hat == 1, name);
                        if (pic != null) Pic(cell, pic, Color.white);
                        else Note(cell.x + cell.width / 2f, cell.y + cell.height / 2f - _ch / 2f, string.IsNullOrEmpty(name) ? "their own" : "no picture", Dim);
                    }
                }
            }
            catch (Exception) { }
        }
    }
}
