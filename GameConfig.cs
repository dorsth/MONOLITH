// MONOLITH - the game's own settings and keybinds in a config. Dorsth was here.
//
// The game keeps its settings and keybinds in PlayerPrefs (Settings.Start reads them, InputManager.SaveBindingOverride
// writes one value per binding). A config holds them next to the mods' settings, under the section "game".
// Left out on purpose: sensitivity and the rest of the mouse feel, and what belongs to one PC (resolution, window mode,
// V-Sync, the game's frame cap, voice chat, the path of a crosshair image).
using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Monolith
{
    internal static class GameConfig
    {
        internal const string Mod = "game", Settings = "Settings", Binds = "Keybinds";
        internal static ConfigEntry<bool> TakeKeys;

        // name in PlayerPrefs, kind (b: on/off, i: number, f: decimal, s: text), field of the game's Settings it is read into
        private static readonly string[,] Prefs =
        {
            { "fovValue", "f", "normalFovValue" }, { "brightness", "f", "brightness" }, { "damageIntensity", "f", "damageIntensity" },
            { "qualitySetting", "i", "qualitySetting" }, { "reduceVFX", "b", "reduceVFX" }, { "motionBlur", "b", "motionBlur" },
            { "minimalistUi", "b", "minimalistUi" }, { "showSpeedometer", "b", "showSpeedometer" }, { "inGameMusic", "b", "inGameMusic" },
            { "enableFixedCrosshair", "b", "enableFixedCrosshair" }, { "disableCrosshair", "b", "disableCrosshair" },
            { "sprintToggle", "b", "sprintToggle" }, { "aimToggle", "b", "aimToggle" }, { "leanToggle", "b", "leanToggle" },
            { "crouchToggle", "b", "crouchToggle" }, { "reverseSprintBind", "b", "reverseSprintBind" }, { "inverseFireBinding", "b", "inverseFireBinding" },
            { "masterVolume", "f", null }, { "effectsVolume", "f", null }, { "musicVolume", "f", null }, { "ambientVolume", "f", null }, { "menuMusicVolume", "f", null },
            { "CrosshairSize", "f", null }, { "CrosshairTintColor", "s", null }, { "CrosshairFilteringDisabled", "i", null },
            { "OutlineWidthMultiplier", "f", null }, { "DisableVoiceChatUI", "i", null },
        };

        internal static void Bind(ConfigFile c)
        {
            TakeKeys = c.Bind("Configs", "Include Keybinds", false, "Loading a config also sets its keybinds. Off: your keybinds stay as they are.");
        }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static IEnumerable<InputAction> Actions()
        {
            PlayerControls pc = InputManager.inputActions;
            if (pc == null) yield break;
            foreach (InputAction a in pc.asset) yield return a;
        }

        private static string BindKey(InputAction a, int i) { return (a.actionMap != null ? a.actionMap.ToString() : null) + a.name + i; }

        /// <summary>The game's settings and keybinds as they are now, into a config.</summary>
        internal static void Capture(Dictionary<uint, string> into)
        {
            try
            {
                for (int r = 0; r < Prefs.GetLength(0); r++)
                {
                    string k = Prefs[r, 0], kind = Prefs[r, 1];
                    if (!PlayerPrefs.HasKey(k)) continue;
                    into[Profiles.IdOf(Mod, Settings, k)] = kind == "f" ? PlayerPrefs.GetFloat(k).ToString("R", Inv) : kind == "s" ? PlayerPrefs.GetString(k) : PlayerPrefs.GetInt(k).ToString(Inv);
                }
                // only the keybinds you changed, so a shared config stays one short line; the mark says the rest are the game's
                bool any = false;
                foreach (InputAction a in Actions())
                {
                    any = true;
                    for (int i = 0; i < a.bindings.Count; i++)
                        if (!string.IsNullOrEmpty(a.bindings[i].overridePath)) into[Profiles.IdOf(Mod, Binds, BindKey(a, i))] = a.bindings[i].overridePath;
                }
                if (any) into[Profiles.IdOf(Mod, Binds, "all")] = "1";
            }
            catch (Exception e) { BundlePlugin.Log.LogWarning("Configs: the game's settings could not be read: " + e.Message); }
        }

        /// <summary>Sets what a config holds of the game's settings, and its keybinds when asked. Returns how many changed.</summary>
        internal static int Apply(Dictionary<uint, string> from, bool keys)
        {
            int n = 0;
            try
            {
                global::Settings s = global::Settings.Instance;
                for (int r = 0; r < Prefs.GetLength(0); r++)
                {
                    string k = Prefs[r, 0], kind = Prefs[r, 1], field = Prefs[r, 2], v;
                    if (!from.TryGetValue(Profiles.IdOf(Mod, Settings, k), out v)) continue;
                    float f; int i;
                    if (kind == "f") { if (!float.TryParse(v, NumberStyles.Float, Inv, out f) || (PlayerPrefs.HasKey(k) && PlayerPrefs.GetFloat(k) == f)) continue; PlayerPrefs.SetFloat(k, f); if (s != null && field != null) Traverse.Create(s).Field(field).SetValue(f); }
                    else if (kind == "s") { if (PlayerPrefs.GetString(k) == v) continue; PlayerPrefs.SetString(k, v); }
                    else
                    {
                        if (!int.TryParse(v, NumberStyles.Integer, Inv, out i) || (PlayerPrefs.HasKey(k) && PlayerPrefs.GetInt(k) == i)) continue;
                        PlayerPrefs.SetInt(k, i);
                        if (s != null && field != null) Traverse.Create(s).Field(field).SetValue(kind == "b" ? (object)(i == 1) : i);
                        if (k == "qualitySetting") QualitySettings.SetQualityLevel(i, true);
                    }
                    n++;
                }
                Volumes();
                // a config from before keybinds were in it leaves yours alone
                keys = keys && from.ContainsKey(Profiles.IdOf(Mod, Binds, "all"));
                if (keys)
                    foreach (InputAction a in Actions())
                        for (int i = 0; i < a.bindings.Count; i++)
                        {
                            string v;
                            if (!from.TryGetValue(Profiles.IdOf(Mod, Binds, BindKey(a, i)), out v)) v = "";
                            if ((a.bindings[i].overridePath ?? "") == v) continue;
                            if (v.Length == 0) a.RemoveBindingOverride(i); else a.ApplyBindingOverride(i, v);
                            PlayerPrefs.SetString(BindKey(a, i), v);
                            n++;
                        }
                PlayerPrefs.Save();
                if (n > 0) Menus();
                if (keys) Keys.SyncSwap();
            }
            catch (Exception e) { BundlePlugin.Log.LogWarning("Configs: the game's settings could not all be set: " + e.Message); }
            return n;
        }

        // The game fills its settings menu once per scene (DisplayLoadValues.LoadValues), and most settings only take
        // effect through that menu: Reduce VFX, graphics quality and the HUD stayed as they were until a restart. Running
        // it again puts the new values in the menu, and each changed toggle or dropdown runs the game's own handler.
        private static void Menus()
        {
            foreach (DisplayLoadValues d in Resources.FindObjectsOfTypeAll<DisplayLoadValues>())
            {
                if (d == null || !d.gameObject.scene.IsValid()) continue;
                try
                {
                    System.Collections.IEnumerator e = Traverse.Create(d).Method("LoadValues").GetValue<System.Collections.IEnumerator>();
                    // stepped through at once: its only wait is the one at the start, for the game to finish loading
                    for (int i = 0; e != null && i < 16 && e.MoveNext(); i++) { }
                }
                catch (Exception x) { BundlePlugin.Log.LogWarning("Configs: the game's settings menu could not be updated: " + x.Message); }
            }
        }

        // the game sets its volumes once at start (Settings.Start): set now as it does there
        private static void Volumes()
        {
            AudioListener.volume = PlayerPrefs.GetFloat("masterVolume", AudioListener.volume);
            SoundManager sm = SoundManager.Instance;
            if (sm == null) return;
            if (sm._effectsSource != null) sm._effectsSource.volume = PlayerPrefs.GetFloat("effectsVolume", sm._effectsSource.volume);
            if (sm._musicSource != null) sm._musicSource.volume = PlayerPrefs.GetFloat("musicVolume", sm._musicSource.volume);
            if (sm._ambientSource != null) sm._ambientSource.volume = PlayerPrefs.GetFloat("ambientVolume", sm._ambientSource.volume);
            sm.menuMusicVolume = PlayerPrefs.GetFloat("menuMusicVolume", sm.menuMusicVolume);
        }
    }
}
