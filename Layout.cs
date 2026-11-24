// MONOLITH - what is in the menu, and where. Dorsth was here.
//
// The menu is laid out by what a setting does, not by which mod it belongs to: the mods' names are not in it.
// Rules it is written by:
//  * One setting, one row, named for what it does. Rows are merged only where one switch is clearer than several.
//  * A row that has no effect right now is not shown (the hitmarker's shape while hitmarkers are hidden, the parts
//    of "hide bodies" while all bodies are hidden, a rate while the tweak it belongs to is off).
//  * Detail goes one page down, behind a link, the way the STRAFTH menu does it.
//  * The line under the page says what the row does, short. A mod's own long description is cut to its first
//    sentence; where that is still not plain, the row has a line of its own here.
// A setting is looked up by the mod's id, its section and its name, so a mod that is missing or has renamed a
// setting costs one row and a line in the log, never an error.
using System;
using System.Reflection;
using HarmonyLib;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace Monolith
{
    internal enum RowKind { Header, Link, Entry, Group, Action, Up }

    internal sealed class MenuRow
    {
        public RowKind Kind;
        public string Label, Info;
        public string Tip;                  // Entry: what it does, in place of the mod's own description
        public Func<string> Dyn;            // a text on the right, worked out when it is drawn
        public Func<bool> If;               // shown only while this says so
        public ConfigEntryBase Entry;       // Entry: the setting
        public ConfigEntryBase[] Parts;     // Group: the switches it sets together
        public MenuPage To;
        public Func<string, string> Take;   // Action: does it and returns the line the menu shows; gets what was typed when Ask is set
        public string Ask;                  // Action: what to type first
        public bool Confirm;                // Action: E twice
        public Func<string[]> Options;      // Entry (text): what to pick from, asked when the row is used
        public string Empty;                // Entry (text): what an empty value reads as
        public bool Bind;                   // Entry (text): a key, mouse button or wheel direction, set by pressing it
        public bool Fixed;                  // Entry (text): Options are all the choices (no empty one first)
        public Action Hover, Unhover;       // a row a mod made with Mod Menu's API: told when the cursor is on it
        public Func<string> Says;           // what that mod puts in the line under the page while it is
    }

    internal sealed class MenuPage
    {
        public string[] Path;
        public MenuPage Parent;
        public string Info;                 // what is in it (shown while it is picked in the list, or its link is)
        public readonly List<MenuRow> Rows = new List<MenuRow>();
        public MenuRow Cur; public int Last, Top;
        public bool Outside;                // not part of MONOLITH (the other mods you have): dimmer in the list
        // a box on the right of the page that the page draws itself (the crosshair): its size in cells, its title
        public Action<UnityEngine.Rect> Draw; public int SideCols; public Func<float> SideRows;
        // opened from its parent page as a Far dialog over it: its rows on top, Draw's samples at the bottom (SideRows
        // high), as Far's Color dialog has its sample text under its settings
        public bool Dialog;
        public bool Side { get { return Draw != null && !Dialog; } }

        public MenuPage(MenuPage parent, string name, string info = null)
        {
            Parent = parent; Info = info;
            if (parent == null) Path = new[] { name };
            else { Path = new string[parent.Path.Length + 1]; parent.Path.CopyTo(Path, 0); Path[Path.Length - 1] = name; }
        }

        public MenuPage Head(string label, Func<bool> when = null) { Rows.Add(new MenuRow { Kind = RowKind.Header, Label = label, If = when }); return this; }

        /// <summary>One setting. Left out when the mod does not have it.</summary>
        public MenuPage Set(string label, ConfigEntryBase e, Func<bool> when = null, string tip = null)
        {
            if (e != null) Rows.Add(new MenuRow { Kind = RowKind.Entry, Label = label, Entry = e, If = when, Tip = tip });
            return this;
        }

        /// <summary>One switch for several: on sets every part on, off every part off.</summary>
        public MenuPage All(string label, string info, Func<bool> when, params ConfigEntryBase[] parts)
        {
            var have = new List<ConfigEntryBase>();
            foreach (ConfigEntryBase p in parts) if (p != null && p.SettingType == typeof(bool)) have.Add(p);
            if (have.Count == 1) return Set(label, have[0], when, info);
            if (have.Count > 1) Rows.Add(new MenuRow { Kind = RowKind.Group, Label = label, Info = info, Parts = have.ToArray(), If = when });
            return this;
        }

        /// <summary>A text setting picked from a list that is only known in the game. Empty is the first choice.</summary>
        public MenuPage Pick(string label, ConfigEntryBase e, Func<string[]> options, string empty, string tip = null)
        {
            if (e != null) Rows.Add(new MenuRow { Kind = RowKind.Entry, Label = label, Entry = e, Options = options, Empty = empty, Tip = tip });
            return this;
        }

        /// <summary>A key you set by pressing it (also a mouse button or the wheel).</summary>
        public MenuPage Key(string label, ConfigEntryBase e, string empty, string tip = null)
        {
            if (e != null) Rows.Add(new MenuRow { Kind = RowKind.Entry, Label = label, Entry = e, Bind = true, Empty = empty, Tip = tip });
            return this;
        }

        public MenuPage Go(MenuPage to, Func<string> dyn = null, Func<bool> when = null)
        {
            if (to != null && to.Rows.Count > 0) Rows.Add(new MenuRow { Kind = RowKind.Link, Label = to.Path[to.Path.Length - 1], To = to, Dyn = dyn, Info = to.Info, If = when });
            return this;
        }
    }

    internal static class MenuLayout
    {
        private const string Tweaks = "dorsth.straftat.dorsthtweaks", View = "dorsth.straftat.viewmodeltweaks", Cam = "dorsth.straftat.cameratweaks",
            Cross = "dorsth.straftat.ezcrosshair", Sens = "dorsth.straftat.truesens", Sounds = "dorsth.straftat.combatsounds", Gore = "dorsth.straftat.nogore", Match = "dorsth.straftat.matchstats";

        /// <summary>The mods whose settings the menu and its search cover.</summary>
        internal static readonly string[] Mods = { Cross, View, Cam, Tweaks, Gore, Sounds, Sens, Match };

        private static readonly List<string> Missing = new List<string>();

        internal static ConfigFile File(string mod)
        {
            PluginInfo pi;
            return Chainloader.PluginInfos.TryGetValue(mod, out pi) && pi.Instance != null ? pi.Instance.Config : null;
        }

        /// <summary>A setting of a mod, or null.</summary>
        internal static ConfigEntryBase Find(string mod, string section, string key)
        {
            try
            {
                ConfigFile f = File(mod);
                if (f != null) { var d = new ConfigDefinition(section, key); if (f.ContainsKey(d)) return f[d]; }
            }
            catch { }
            return null;
        }

        private static ConfigEntryBase E(string mod, string section, string key)
        {
            ConfigEntryBase e = Find(mod, section, key);
            if (e == null) Missing.Add(section + " / " + key);
            return e;
        }

        /// <summary>Settings one switch of the menu stands for (No post-processing, Ultimate tweaks, Performance viewmodel): kept out of the search and of profiles.</summary>
        internal static IEnumerable<ConfigEntryBase> Inert()
        {
            yield return Find(View, "Viewmodel", "One Camera Hides Weapon Effects");
        }

        private static bool On(ConfigEntryBase e) { return e != null && e.BoxedValue is bool && (bool)e.BoxedValue; }
        private static bool Off(ConfigEntryBase e) { return e != null && !On(e); }
        private static bool Is(ConfigEntryBase e, string value) { return e != null && e.GetSerializedValue() == value; }

        /// <summary>The settings of every other mod that is loaded, listed the way Mod Menu lists them, so a mod that is made for
        /// Mod Menu shows the same here:
        ///  * every loaded mod, also one without settings ("no options"), by its name from its Thunderstore manifest when
        ///    it has one (underscores as spaces), else its plugin name; sorted by name;
        ///  * its description: the one it gave Mod Menu (ModMenuCustomisation.SetPluginDescription), else its manifest's;
        ///    icon.png and manifest.json are looked for the way Mod Menu does: only in the mod's own folder under plugins;
        ///  * settings it hid from Mod Menu (ModMenuCustomisation.HideEntry/HideEntries) are hidden here too;
        ///  * every section as a heading, the settings in the order of the mod's config, each named by its key.
        ///  * what a mod adds with Mod Menu's content builder (RegisterContentBuilder), where it puts it: with MONOLITH's
        ///    stand-in for Mod Menu (ModMenu.dll) installed, see Built. One thing Mod Menu does not have: a text setting whose description lists its choices in quotes is
        /// a list to pick from (Named).
        /// MONOLITH itself and the mods inside it are left out (they are the rest of this menu). Built when the menu is built
        /// (every mod is loaded by then); nothing per frame.</summary>
        private static MenuPage OtherMods()
        {
            var page = new MenuPage(null, "Other mods", "Settings of the other mods you have installed. Not part of MONOLITH. Works with mods made for Mod Menu by kestrel.");
            HashSet<ConfigEntryBase> hidden = null; Dictionary<string, string> said = null;
            try
            {
                Type c = AccessTools.TypeByName("ModMenu.Api.ModMenuCustomisation");
                if (c != null)
                {
                    PropertyInfo h = AccessTools.Property(c, "HiddenConfigEntries"), d = AccessTools.Property(c, "Descriptions");
                    if (h != null) hidden = h.GetValue(null) as HashSet<ConfigEntryBase>;
                    if (d != null) said = d.GetValue(null) as Dictionary<string, string>;
                }
            }
            catch (Exception e) { if (BundlePlugin.Log != null) BundlePlugin.Log.LogWarning("Mods: Mod Menu's hidden settings not read: " + e.Message); }

            var list = new List<KeyValuePair<string, PluginInfo>>();
            var about = new Dictionary<PluginInfo, string>();
            foreach (PluginInfo pi in Chainloader.PluginInfos.Values)
            {
                if (pi == null || pi.Instance == null || pi.Metadata == null) continue;
                string id = pi.Metadata.GUID;
                if (id == BundlePlugin.Guid || Array.IndexOf(Mods, id) >= 0) continue;
                string name = pi.Metadata.Name, desc = null;
                Manifest(pi, ref name, ref desc);
                string api;
                if (said != null && said.TryGetValue(id, out api) && !string.IsNullOrEmpty(api)) desc = api;
                about[pi] = desc;
                list.Add(new KeyValuePair<string, PluginInfo>(name, pi));
            }
            list.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
            foreach (var kv in list)
            {
                PluginInfo pi = kv.Value;
                var mod = new MenuPage(page, kv.Key, about[pi]);
                string sec = null;
                ConfigFile f = pi.Instance.Config;
                if (f != null)
                    foreach (var ce in f)
                    {
                        ConfigEntryBase e = ce.Value;
                        if (e == null || hidden != null && hidden.Contains(e)) continue;
                        if (e.Definition.Section != sec) { sec = e.Definition.Section; mod.Head(sec); }
                        string[] named;
                        if (e.SettingType == typeof(UnityEngine.KeyCode) || e.SettingType == typeof(KeyboardShortcut)) mod.Key(e.Definition.Key, e, "none");
                        else if ((named = Named(e)) != null) mod.Rows.Add(new MenuRow { Kind = RowKind.Entry, Label = e.Definition.Key, Entry = e, Options = () => named, Fixed = true });
                        else mod.Set(e.Definition.Key, e);
                    }
                // as Mod Menu: a content builder runs for a mod that has settings, on the list of its setting rows
                if (f != null && f.Count > 0) Built(pi.Metadata.GUID, mod);
                string v = mod.Rows.Count == 0 ? Lang.T("no options") : pi.Metadata.Version != null ? "v" + pi.Metadata.Version : "";
                page.Go(mod, () => v);
            }
            return page.Rows.Count > 0 ? page : null;
        }

        // ---- a mod's own rows, made with Mod Menu's API (RegisterContentBuilder)

        private static MethodInfo _run;
        private static ConfigFile _mirror;
        private static readonly Dictionary<ConfigEntryBase, Action<object>> Write = new Dictionary<ConfigEntryBase, Action<object>>();
        private static bool _syncing;
        private static MethodInfo _bind;

        /// <summary>Runs the mod's content builder through MONOLITH's stand-in for Mod Menu and puts its rows among the
        /// mod's setting rows, in the order Mod Menu would show them. Headers, text, buttons and values (a value is
        /// a setting of its own here that reads and writes through the mod's getter and setter). Nothing happens with
        /// kestrel's Mod Menu installed: then its own window shows these.</summary>
        private static void Built(string guid, MenuPage mod)
        {
            try
            {
                if (_run == null)
                {
                    Type pages = AccessTools.TypeByName("ModMenu.Pages");
                    if (pages == null) return;
                    _run = AccessTools.Method(pages, "Run");
                }
                var rows = _run.Invoke(null, new object[] { guid, mod.Rows.Count }) as List<Dictionary<string, object>>;
                if (rows == null || rows.Count == 0) return;
                var cfg = new List<MenuRow>(mod.Rows);
                mod.Rows.Clear();
                int n = 0;
                foreach (Dictionary<string, object> r in rows)
                {
                    string kind = r["kind"] as string;
                    if (kind == "config") { mod.Rows.Add(cfg[(int)r["index"]]); continue; }
                    MenuRow row = Row(guid, n++, kind, r);
                    if (row == null) continue;
                    var active = (Func<bool>)r["active"];
                    Func<bool> was = row.If;
                    row.If = was == null ? active : () => active() && was();
                    row.Hover = (Action)r["hover"]; row.Unhover = (Action)r["unhover"]; row.Says = (Func<string>)r["info"];
                    mod.Rows.Add(row);
                }
            }
            catch (Exception e) { if (BundlePlugin.Log != null) BundlePlugin.Log.LogWarning("Mods: the page " + guid + " made with Mod Menu could not be shown: " + (e.InnerException ?? e).Message); }
        }

        private static MenuRow Row(string guid, int n, string kind, Dictionary<string, object> r)
        {
            string label = r["label"] as string ?? "";
            switch (kind)
            {
                case "header": return new MenuRow { Kind = RowKind.Header, Label = label };
                case "text": return new MenuRow { Kind = RowKind.Action, Label = label, Info = label };
                case "button":
                {
                    var click = (Action)r["click"]; var text = (Func<string>)r["button"];
                    return new MenuRow { Kind = RowKind.Action, Label = label, Dyn = text, Take = s => { click(); return null; } };
                }
                case "value": return Value(guid, n, label, r);
            }
            return null;
        }

        // A value is shown as a setting: a ConfigEntry of its own, in a config file that is never saved, kept equal to
        // the mod's getter while the row is on screen and written to the mod's setter when it is changed.
        private static MenuRow Value(string guid, int n, string label, Dictionary<string, object> r)
        {
            Type t = (Type)r["type"];
            var get = (Func<object>)r["get"]; var set = (Action<object>)r["set"];
            object min, max; bool slider = r.TryGetValue("min", out min) & r.TryGetValue("max", out max);
            // numbers as int or float, the two the menu can slide
            Type shown = t;
            if (IsNumber(t)) shown = t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte) ? typeof(int) : typeof(float);
            if (!TomlTypeConverter.CanConvert(shown)) return null;

            if (_mirror == null)
            {
                _mirror = new ConfigFile(System.IO.Path.Combine(Paths.CachePath, "MONOLITH.modmenu.cfg"), false) { SaveOnConfigSet = false };
                _mirror.SettingChanged += (s, a) =>
                {
                    Action<object> w;
                    if (_syncing || a == null || !Write.TryGetValue(a.ChangedSetting, out w)) return;
                    try { w(a.ChangedSetting.BoxedValue); } catch (Exception e) { if (BundlePlugin.Log != null) BundlePlugin.Log.LogWarning("Mods: " + e.Message); }
                };
            }
            object now = To(Safe(get), shown) ?? (shown.IsValueType ? Activator.CreateInstance(shown) : shown == typeof(string) ? "" : null);
            object def = To(r["default"], shown) ?? now;
            AcceptableValueBase range = null;
            if (slider && (shown == typeof(int) || shown == typeof(float)))
                range = (AcceptableValueBase)Activator.CreateInstance(typeof(AcceptableValueRange<>).MakeGenericType(shown), To(min, shown), To(max, shown));
            if (_bind == null)
                foreach (MethodInfo m in typeof(ConfigFile).GetMethods())
                {
                    ParameterInfo[] ps = m.GetParameters();
                    if (m.Name == "Bind" && m.IsGenericMethodDefinition && ps.Length == 3 && ps[0].ParameterType == typeof(ConfigDefinition) && ps[2].ParameterType == typeof(ConfigDescription)) { _bind = m; break; }
                }
            MethodInfo bind = _bind;
            var entry = (ConfigEntryBase)bind.MakeGenericMethod(shown).Invoke(_mirror, new object[] { new ConfigDefinition(guid, "row " + n), def, new ConfigDescription("", range) });
            _syncing = true; try { entry.BoxedValue = now; } finally { _syncing = false; }
            Write[entry] = v => set(To(v, t));

            int frame = -1;
            Func<bool> sync = () =>
            {
                // once a frame, while the menu shows the row: the mod may have changed it itself
                if (frame == UnityEngine.Time.frameCount) return true;
                frame = UnityEngine.Time.frameCount;
                object v = To(Safe(get), shown);
                if (v != null && !v.Equals(entry.BoxedValue)) { _syncing = true; try { entry.BoxedValue = v; } catch { } finally { _syncing = false; } }
                return true;
            };
            return new MenuRow { Kind = RowKind.Entry, Label = label, Entry = entry, If = sync, Tip = "",
                Bind = shown == typeof(UnityEngine.KeyCode) || shown == typeof(KeyboardShortcut), Empty = "none" };
        }

        private static object Safe(Func<object> get) { try { return get(); } catch { return null; } }

        private static bool IsNumber(Type t)
        {
            return t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte)
                || t == typeof(float) || t == typeof(double) || t == typeof(decimal);
        }

        private static object To(object v, Type t)
        {
            if (v == null) return null;
            if (t.IsInstanceOfType(v)) return v;
            try { return t.IsEnum ? Enum.ToObject(t, v) : Convert.ChangeType(v, t, System.Globalization.CultureInfo.InvariantCulture); } catch { return null; }
        }

        // As Mod Menu: the mod's own folder directly under plugins (none for a dll lying in plugins itself), searched for
        // manifest.json; its name (underscores as spaces) and description.
        private static void Manifest(PluginInfo pi, ref string name, ref string desc)
        {
            try
            {
                string dir = System.IO.Path.GetFullPath(pi.Location);
                System.IO.DirectoryInfo up = System.IO.Directory.GetParent(dir);
                while (up != null && !string.Equals(up.Name, "plugins", StringComparison.OrdinalIgnoreCase)) { dir = up.FullName; up = up.Parent; }
                if (up == null || dir.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return;
                string path = null;
                foreach (string m in System.IO.Directory.GetFiles(dir, "manifest.json", System.IO.SearchOption.AllDirectories)) { path = m; break; }
                if (path == null) return;
                string json = System.IO.File.ReadAllText(path);
                string n = JsonText(json, "name"), d = JsonText(json, "description");
                if (!string.IsNullOrEmpty(n)) name = n.Replace('_', ' ');
                if (d != null) desc = d;
            }
            catch { }
        }

        private static string JsonText(string json, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            try { return System.Text.RegularExpressions.Regex.Unescape(m.Groups[1].Value); } catch { return m.Groups[1].Value; }
        }

        // A text setting whose mod gives its choices only in the description ("Which spawn set to display: '2v2' or '1v1'"):
        // the quoted words, when there are at least two and the current value is one of them, become a list to pick from.
        private static string[] Named(ConfigEntryBase e)
        {
            if (e.SettingType != typeof(string) || e.Description == null || e.Description.AcceptableValues != null) return null;
            var found = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(e.Description.Description ?? "", "'([^'\\r\\n]{1,40})'|\"([^\"\\r\\n]{1,40})\""))
            {
                string v = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (v.Length > 0 && !found.Contains(v)) found.Add(v);
            }
            return found.Count >= 2 && found.Contains((string)e.BoxedValue) ? found.ToArray() : null;
        }

        internal static List<MenuPage> Build()
        {
            Missing.Clear();
            var cats = new List<MenuPage>();

            // ---------------------------------------------------------------- crosshair
            {
                var p = new MenuPage(null, "Crosshair", "Style, color, size.");
                ConfigEntryBase on = E(Cross, "Crosshair", "Enabled"), hit = E(Cross, "Crosshair", "Hit Color");
                Func<bool> own = () => On(on);
                // its look in a dialog with the samples under it, as Far's Color dialog
                var look = new MenuPage(p, "Crosshair") { Dialog = true };
                look.Set("Style", E(Cross, "Crosshair", "Crosshair"), own, "Every PNG in plugins/MONOLITH/Crosshairs. Add your own there and restart the game.")
                 .Set("Color", E(Cross, "Crosshair", "Color"), own, "White keeps the picture's own colors.")
                 .Set("Size", E(Cross, "Crosshair", "Scale"), own, "1 is the picture's own size at 1080p.")
                 .Set("Show when scoped", E(Cross, "Crosshair", "Show While Scoped"), own, "")
                 .Set("Hit color", hit, own, "The crosshair changes color for a moment on a hit.")
                 .Set("  color", E(Cross, "Crosshair", "Hit Color Value"), () => On(on) && On(hit), "");
                if (on != null) { look.Draw = r => BundleMenu.I.CrosshairPreview(r); look.SideRows = () => 8f; }
                p.Set("Custom crosshair", on, null, "Off: the game crosshair.").Go(look, null, own);
                if (p.Rows.Count > 0) cats.Add(p);
            }

            // ---------------------------------------------------------------- viewmodel
            {
                var p = new MenuPage(null, "Viewmodel", "Where your weapon sits, how much it moves. Weapon camera based on Minimal Viewmodels Reborn by kestrel.");
                ConfigEntryBase gone = E(View, "Viewmodel", "Invisible Viewmodel");
                Func<bool> seen = () => !On(gone);
                p.Set("FOV", E(View, "Viewmodel", "Viewmodel FOV"), seen, "")
                 .Set("Left / right", E(View, "Viewmodel", "Viewmodel X Offset"), seen, "")
                 .Set("Down / up", E(View, "Viewmodel", "Viewmodel Y Offset"), seen, "")
                 .Set("Back / forward", E(View, "Viewmodel", "Viewmodel Z Offset"), seen, "")
                 .Set("Left-handed", E(View, "Viewmodel", "Mirror Viewmodel"), seen, "")
                 .Set("Sway", E(View, "Viewmodel", "Viewmodel Sway Scale"), seen, "Sway when you turn, jump and land. 0 is none.")
                 .Set("Bob", E(View, "Viewmodel", "Viewmodel Bob Scale"), seen, "Bob when you walk and stand. 0 is none.")
                 .Set("No viewmodel climb", E(Tweaks, "Viewmodel", "No Viewmodel Climb"), seen, "No upward drift of the gun while you spray.")
                 .Set("Hide arms", E(View, "Viewmodel", "Invisible Arms"), seen, "")
                 .Set("Hide viewmodel", gone, null, "Hides your weapon and arms.");

                // per weapon: the ones you added, and one empty row to add the next
                var per = new MenuPage(p, "Per weapon", "A weapon listed here uses its own FOV and position.");
                var picks = new List<ConfigEntryBase>();
                ConfigFile vf = File(View);
                for (int i = 1; i <= 32 && vf != null; i++)
                {
                    var d = new ConfigDefinition("Weapon " + i, "Weapon");
                    if (!vf.ContainsKey(d)) break;
                    picks.Add(vf[d]);
                }
                for (int i = 0; i < picks.Count; i++)
                {
                    int me = i; string s = "Weapon " + (i + 1); ConfigEntryBase pick = picks[i];
                    Func<bool> used = () => !Is(pick, "None");
                    var slot = new MenuPage(per, s, "Pick a weapon, then its own FOV and position.");
                    slot.Set("Weapon", pick, null, "None leaves this slot empty.")
                        .Set("FOV", E(View, s, "Viewmodel FOV"), used, "")
                        .Set("Left / right", E(View, s, "Viewmodel X Offset"), used, "")
                        .Set("Down / up", E(View, s, "Viewmodel Y Offset"), used, "")
                        .Set("Back / forward", E(View, s, "Viewmodel Z Offset"), used, "")
                        .Set("Hide arms", E(View, s, "Invisible Arms"), used, "");
                    per.Go(slot, () => Is(pick, "None") ? "add a weapon" : pick.GetSerializedValue(), () =>
                    {
                        if (!Is(pick, "None")) return true;
                        for (int k = 0; k < me; k++) if (Is(picks[k], "None")) return false;   // only the first empty one
                        return true;
                    });
                }
                p.Go(per, () => { int n = 0; foreach (ConfigEntryBase e in picks) if (!Is(e, "None")) n++; return n == 0 ? "none" : Lang.Count(n, " weapon", " weapons", "weapons: "); }, seen);
                if (p.Rows.Count > 0) cats.Add(p);
            }

            // ---------------------------------------------------------------- camera
            {
                var p = new MenuPage(null, "Camera", "Tilt, shake, sprint FOV, recoil.");
                p.Set("No tilt", E(Cam, "Camera", "NoTilt"), null, "No roll when you strafe, slide or turn.")
                 .Set("Better recoil", E(Cam, "Camera", "BetterRecoil"), null, "With the AR, AK and Warden, recoil moves your aim instead of shaking the camera.")
                 .Set("No landing shake", E(Cam, "Camera", "NoFallShake"), null, "")
                 .Set("No jump shake", E(Cam, "Camera", "NoJumpShake"), null, "Slide jumps, wall jumps and ceiling hits.")
                 .Set("No vault shake", E(Cam, "Camera", "NoVaultShake"), null, "")
                 .Set("Sprint FOV increase", E(Cam, "Camera", "SprintFovIncrease"), null, "Degrees your FOV widens when you sprint. Sliding widens it by the same share. 0: no change.");
                if (p.Rows.Count > 0) cats.Add(p);
            }

            // ---------------------------------------------------------------- hud
            {
                var p = new MenuPage(null, "HUD", "Hitmarkers, prompts, counters.");

                var hm = new MenuPage(p, "Hitmarkers", "Shape, size, animation, colors.");
                ConfigEntryBase noHm = E(Tweaks, "HUD", "Hide Hitmarkers"), still = E(Tweaks, "HUD", "Static Hitmarkers"), kill = E(Tweaks, "HUD", "Kill Hitmarker");
                Func<bool> shown = () => !On(noHm);
                hm.Set("Hide hitmarkers", noHm, null, "")
                  .Set("Line ends", E(Tweaks, "HUD", "Hitmarker Shape"), shown, "")
                  .Set("Length", E(Tweaks, "HUD", "Hitmarker Length"), shown, "")
                  .Set("Thickness", E(Tweaks, "HUD", "Hitmarker Width"), shown, "")
                  .Set("Gap", E(Tweaks, "HUD", "Hitmarker Gap"), shown, "")
                  .Set("Static hitmarkers", still, shown, "They fade out without popping.")
                  .Set("Animation", E(Tweaks, "HUD", "Hitmarker Animation"), () => !On(noHm) && !On(still), "How far they pop.")
                  .Set("Same headshot animation", E(Tweaks, "HUD", "Same Headshot Animation"), () => !On(noHm) && !On(still), "Headshots pop like bodyshots.")
                  .Set("Bodyshot color", E(Tweaks, "HUD", "Hitmarker Bodyshot Color"), shown, "")
                  .Set("Headshot color", E(Tweaks, "HUD", "Hitmarker Headshot Color"), shown, "")
                  .Set("Kill hitmarker", kill, shown, "The hit that kills shows in the kill color.")
                  .Set("Kill color", E(Tweaks, "HUD", "Hitmarker Kill Color"), () => !On(noHm) && On(kill), "");
                if (noHm != null) { hm.Draw = r => BundleMenu.I.HitmarkerPreview(r); hm.SideCols = 24; hm.SideRows = () => 11f + (On(kill) ? BundleMenu.MarkerRows : 0f); }

                var pr = new MenuPage(p, "Prompts", "The texts on weapons, doors and buttons.");
                ConfigEntryBase noPick = E(Tweaks, "Interaction", "Hide Pickup Prompts"), noUse = E(Tweaks, "Interaction", "Hide Interaction Prompts");
                Func<bool> some = () => !(On(noPick) && On(noUse));
                pr.All("Hide prompts", "On weapons, doors and buttons.", null, noPick, noUse)
                  .Set("Hide the key in a prompt", E(Tweaks, "Interaction", "Hide Key Prompt"), some, "Hides the [F] after a prompt.")
                  .Set("Color", E(Tweaks, "Interaction", "Prompt Color"), some, "")
                  .Set("Size", E(Tweaks, "Interaction", "Prompt Scale"), some, "")
                  .Set("Left / right", E(Tweaks, "Interaction", "Prompt X Offset"), some, "")
                  .Set("Down / up", E(Tweaks, "Interaction", "Prompt Y Offset"), some, "")
                  .Set("Weapon highlight color", E(Tweaks, "Interaction", "Weapon Highlight Color"), null, "The outline of the weapon you look at.");

                p.Go(hm, () => On(noHm) ? "hidden" : On(still) ? "static" : "")
                 .Go(pr, () => On(noPick) && On(noUse) ? "hidden" : "")
                 .Set("Match stats", E(Match, "Match Stats", "Enabled"), null, "Kills, deaths and damage of each round on the round-end screen, the match so far over each name on the Tab screen, and a full board after the match.")
                 .Set("Hide FPS", E(Tweaks, "HUD", "Hide FPS"), null, "")
                 .Set("Hide ping", E(Tweaks, "HUD", "Hide Ping"), null, "")
                 .Set("Slide cooldown timer", E(Tweaks, "HUD", "Slide Cooldown Timer"), null, "Shows the time until you can slide again under the crosshair, as text or a bar.")
                 .Set("Minimal HUD color", E(Tweaks, "HUD", "Minimal HUD Color"), null, "Color of the minimal HUD, speedometer and slide cooldown timer.");
                if (p.Rows.Count > 0) cats.Add(p);
            }

            // ---------------------------------------------------------------- effects
            {
                var p = new MenuPage(null, "Effects", "Tracers, casings, bodies, blood, vignette.");
                ConfigEntryBase mine = E(Tweaks, "Bullet Trails", "Hide My Trails"), others = E(Tweaks, "Bullet Trails", "Hide Other Players Trails");
                Func<bool> trails = () => !(On(mine) && On(others));
                ConfigEntryBase bodies = E(Gore, "Bodies", "Hide Bodies"), blood = E(Gore, "Blood", "No Blood");
                // the rows on their own first, then the groups
                p.Set("No bullet casings", E(Tweaks, "Viewmodel", "No Bullet Casings"), null, "No casings from any gun, yours or other players. No casing sound.")
                 .Set("No near miss vignette", E(Tweaks, "HUD", "Hide Near Miss Vignette"), null, "The dark edge when a shot passes close.")
                 .Head("Tracers")
                 .Set("Hide my tracers", mine, null, "")
                 .Set("Hide other players tracers", others, null, "")
                 .Set("Color", E(Tweaks, "Bullet Trails", "Trail Color"), trails, "White keeps each gun's own color.")
                 .Set("Width", E(Tweaks, "Bullet Trails", "Trail Width"), trails, "")
                 .Set("Opacity", E(Tweaks, "Bullet Trails", "Trail Opacity"), trails, "Lower is fainter.")
                 .Head("Bodies and blood")
                 .Set("Hide bodies", bodies, null, "Hides ragdolls, gibs and shot-off hats. Turn it off to pick which.")
                 .Set("  Hide ragdolls", E(Gore, "Bodies", "Hide Ragdolls"), () => Off(bodies), "")
                 .Set("  Hide dropped hats", E(Gore, "Bodies", "Hide Dropped Hats"), () => Off(bodies), "Hats that were shot off.")
                 .Set("No blood", blood, null, "Turn it off to pick which.")
                 .Set("  No hit blood", E(Gore, "Blood", "No Hit Blood"), () => Off(blood), "")
                 .Set("  No death blood", E(Gore, "Blood", "No Death Blood"), () => Off(blood), "")
                 .Set("  No blood decals", E(Gore, "Blood", "No Blood Stains"), () => Off(blood), "");
                if (p.Rows.Count > 3) cats.Add(p);
            }

            // ---------------------------------------------------------------- players
            {
                var p = new MenuPage(null, "Players", "How other players look on your screen, from the next round. Lobby options.");
                p.Head("Enemies")
                 .Pick("Suit", Players.EnemyOutfit, Players.Outfits, "their own", "")
                 .Pick("Hat", Players.EnemyHat, Players.Hats, "their own", "")
                 .Set("Outline color", Players.EnemyOutline, null, "The same setting as in the game. Only in lobbies with enemy outlines on.")
                 .Head("Teammates")
                 .Pick("Suit", Players.TeamOutfit, Players.Outfits, "their own", "")
                 .Pick("Hat", Players.TeamHat, Players.Hats, "their own", "")
                 .Set("Outline color", Players.TeamOutline, null, "The same setting as in the game.")
                 .Head("Lobby")
                 .Set("Enemy outlines when I host", Players.HostOutlines, null, "The game's lobby option, kept between launches. In other lobbies the host decides.")
                 .Set("Friendly fire when I host", Players.HostFriendlyFire, null, "The game's lobby option, kept between launches. In other lobbies the host decides.")
                 .Set("Join message", E(Tweaks, "Menus", "Join Message"), null, "The line everyone sees when you join a lobby. {name} is your name. Color, bold, italic, underline and strikethrough tags work. Up to 120 letters, tags not counted. Leave it empty for the game's line.");
                if (p.Rows.Count > 2) { p.Draw = r => BundleMenu.I.PlayersPreview(r); p.SideCols = 24; p.SideRows = () => 16f; cats.Add(p); }
            }

            // ---------------------------------------------------------------- sound
            {
                var p = new MenuPage(null, "Audio", "Hitsounds, killsounds, gun volume.");
                // named as TF2 and Quake players know them
                var named = new[] { "Hit Sound", "Hitsound", "Head Hit Sound", "Headshot hitsound", "Kill Sound", "Killsound", "Melee Kill Sound", "Melee killsound" };
                for (int i = 0; i < named.Length; i += 2)
                {
                    string s = named[i];
                    ConfigEntryBase pick = E(Sounds, "Sounds", s);
                    p.Set(named[i + 1], pick, null, "You hear each one as you go through the list. Add your own sound files to plugins/MONOLITH/" + s.Replace(" ", "") + "s.")
                     .Set("  volume", E(Sounds, "Sounds", s + " Volume"), () => !Is(pick, "None"), "");
                }
                p.Set("Gun sound volume", E(Sounds, "Guns", "Gun Sound Volume"), null, "Shots, reloads, clicks, casings.");
                if (p.Rows.Count > 0) cats.Add(p);
            }

            // ---------------------------------------------------------------- controls: sensitivity, and keys the game has no setting for
            {
                var p = new MenuPage(null, "Controls", "Sensitivity in cm/360, extra keybinds, taunts.");
                ConfigEntryBase on = E(Sens, "Sensitivity", "Enabled");
                Func<bool> own = () => On(on);
                p.Head("Mouse")
                 .Set("Sensitivity in cm/360", on, null, "Turn it off to use the game's sensitivity.")
                 .Set("Mouse DPI", E(Sens, "Sensitivity", "Mouse DPI"), own, "The DPI of your mouse.")
                 .Set("cm/360", E(Sens, "Sensitivity", "cm/360"), own, "How far you move the mouse for one full turn.")
                 .Set("ADS multiplier", E(Sens, "Sensitivity", "ADS Multiplier"), own, "1 feels the same as hip fire.")
                 .Set("Scoped multiplier", E(Sens, "Sensitivity", "Scoped Multiplier"), own, "1 feels the same as hip fire.");
                const string how = " Press E, then a key, mouse button or scroll.";
                p.Head("Keybinds")
                 .Key("Second jump key", Keys.Jump1, "not set", "Works next to your own jump key." + how)
                 .Key("Third jump key", Keys.Jump2, "not set", "Put scroll up on one and scroll down on the other to jump with the whole wheel." + how)
                 .Key("Swap weapons", Keys.Swap, "scroll wheel", "Same as Swap in the game's settings. Press E, then a key or mouse button, or scroll to use the wheel.");
                if (p.Rows.Count > 0 && p.Rows[p.Rows.Count - 1].Entry == Keys.Swap) p.Rows[p.Rows.Count - 1].If = Keys.SyncSwap;   // shows what the game's settings have now
                var t = new MenuPage(p, "Taunts", "Move a taunt to another key, or switch it off.");
                for (int i = 0; i < Keys.Taunt.Length; i++) t.Key("Taunt " + (i + 1), Keys.Taunt[i], "key " + ((i + 1) % 10), "Press E, then its new key. Enter sets no key.");
                p.Go(t, () => { int n = 0; foreach (var e in Keys.Taunt) if (e != null && e.Value.Length > 0) n++; return n == 0 ? "number keys" : Lang.Count(n, " changed", " changed", "changed: "); });
                if (p.Rows.Count > 2) cats.Add(p);
            }

            // ---------------------------------------------------------------- performance
            {
                var p = new MenuPage(null, "Performance", "More FPS, steadier frames.");
                p.Set("Reduce VFX+", E(Tweaks, "World", "Reduce VFX+"), null, "No shadows, rain, light rays or screen effects. Fewer lights and particles. No sparks, debris or smoke from explosions. No glass shards or broken off pieces.")
                 .Set("No post-processing", E(Tweaks, "World", "No Post-Processing"), null, "Turns off color grading and screen effects. Menus lose their blur and the brightness slider stops working.")
                 .Set("Lower textures", E(Tweaks, "World", "Lower Textures"), null, "Like picmip. 0 is full quality, 3 is the lowest.")
                 .Set("Anisotropic filtering", E(Tweaks, "World", "Anisotropic Filtering"), null, "Keeps textures sharp when seen at an angle, such as a floor further away. The game leaves it off.")
                 .Set("Ultimate tweaks", E(Tweaks, "World", "Ultimate Tweaks"), null, "Fixes slow game code, uses fewer worker threads, removes the voice key freeze and compiles code at start.")
                 .Set("Performance viewmodel", E(View, "Viewmodel", "One Camera"), null, "One camera draws everything, your weapon included. Slightly more FPS. Not used while you hold a melee weapon.")
                 .Set("FPS cap", FrameCap.On, null, "Replaces the game's FPS cap, which can be up to 1 ms off per frame. This one keeps every frame the same length. Not in the main menu.")
                 .Set("  Max FPS", FrameCap.Fps, () => On(FrameCap.On), "Set it a bit below what your PC holds on busy maps.");
                if (p.Rows.Count > 3) cats.Add(p);
            }

            try { cats.Add(Profiles.Page()); } catch (Exception e) { if (BundlePlugin.Log != null) BundlePlugin.Log.LogWarning("Profiles: " + e.Message); }

            // ---------------------------------------------------------------- general
            {
                var p = new MenuPage(null, "General", "Language, the menu key, menu animations, playlists.");
                p.Set("Language / Язык", Lang.Pick, null, "")
                 .Set("Open this menu", BundleMenu.Key, null, "Press E, then the key.")
                 .Set("Fewer UI animations", E(Tweaks, "Menus", "Reduce UI Animations"), null, "No animations in menus and on the HUD.")
                 .Set("Hide default playlists", E(Tweaks, "Menus", "Hide Default Playlists"), null, "Only your own playlists show when you host.");
                // who MONOLITH builds on: a line each, the name on the right; nothing to press
                p.Head("Credits");
                p.Rows.Add(new MenuRow { Kind = RowKind.Action, Label = "Menu", Dyn = () => "klammvs", Info = "Menu inspired by the STRAFTAT theme for Far Manager by klammvs." });
                p.Rows.Add(new MenuRow { Kind = RowKind.Action, Label = "Slide timer", Dyn = () => "Glizzmn", Info = "Slide cooldown timer based on MovementPro by Glizzmn." });
                p.Rows.Add(new MenuRow { Kind = RowKind.Action, Label = "Tilt and shake switches", Dyn = () => "Kevnator", Info = "Camera tilt and shake switches build on NoCameraRoll by Kevnator." });
                p.Rows.Add(new MenuRow { Kind = RowKind.Action, Label = "Weapon camera", Dyn = () => "kestrel", Info = "Weapon camera based on Minimal Viewmodels Reborn by kestrel." });
                p.Rows.Add(new MenuRow { Kind = RowKind.Action, Label = "Mod Menu support", Dyn = () => "kestrel", Info = "Works with mods made for Mod Menu by kestrel." });
                if (p.Rows.Count > 0) cats.Add(p);
            }

            // last, and dimmer in the list: not part of MONOLITH, only a place to set the other mods you have
            try { MenuPage m = OtherMods(); if (m != null) { m.Outside = true; cats.Add(m); } } catch (Exception e) { if (BundlePlugin.Log != null) BundlePlugin.Log.LogWarning("Other mods: " + e.Message); }

            if (Missing.Count > 0 && BundlePlugin.Log != null)
                BundlePlugin.Log.LogInfo("Menu: " + Missing.Count + " settings it has a row for were not found (a mod not loaded or renamed): " + string.Join("; ", Missing.ToArray()));
            return cats;
        }
    }
}
