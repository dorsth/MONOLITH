// MONOLITH - profiles: a setup saved under a name, loaded again, or passed to someone else. Dorsth was here.
//
// A profile is every setting of every mod in the bundle that is not at its default, EXCEPT:
//  * sensitivity (never copied: it belongs to a hand and a mouse, not to a setup),
//  * the menu's own key, where a window was, a mod's internal notes, the join message.
// Loading one puts every setting it does not name back to its default, so a loaded profile is the whole setup.
//
// To pass one on it becomes one line of text on the clipboard: MONOLITH:<name>:<code>. The code is letters and digits
// only, so a chat program cannot mangle it. Importing reads the clipboard, keeps what you had as "previous setup",
// and loads the new one. Experimental settings are NOT taken from somebody else's line: yours stay as they are.
//
// Saved profiles are lines of the same kind in BepInEx/config/dorsth.straftat.monolith.profiles.txt.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace Monolith
{
    internal static class Profiles
    {
        internal sealed class Profile { public string Name; public readonly Dictionary<uint, string> Values = new Dictionary<uint, string>(); }

        internal static readonly List<Profile> All = new List<Profile>();
        private const string Tag = "MONOLITH:", NotMine = "dorsth.straftat.truesens", Before = "previous config";
        private static bool _read;
        private static string FilePath { get { return Path.Combine(Paths.ConfigPath, "dorsth.straftat.monolith.profiles.txt"); } }

        // Game: a setting that belongs to the game and is only shown here too (the two outline colors). A profile sets it
        // only when it holds a value for it: Defaults, and a profile from before these rows existed, leave yours as it is.
        private struct Item { public uint Id; public ConfigEntryBase Entry; public bool Experimental, Game; }

        // FNV-1a over mod, section and name: a setting keeps its number as long as it keeps its name
        private static uint Id(string mod, ConfigDefinition d)
        {
            uint h = 2166136261;
            foreach (string part in new[] { mod, d.Section, d.Key })
            {
                foreach (char c in part) { h ^= c; h *= 16777619; }
                h ^= '|'; h *= 16777619;
            }
            return h;
        }

        private static List<Item> Items()
        {
            var list = new List<Item>(); var seen = new HashSet<uint>();
            var files = new List<KeyValuePair<string, ConfigFile>>();
            foreach (string mod in MenuLayout.Mods) if (mod != NotMine) files.Add(new KeyValuePair<string, ConfigFile>(mod, MenuLayout.File(mod)));
            files.Add(new KeyValuePair<string, ConfigFile>(BundlePlugin.Guid, MenuLayout.File(BundlePlugin.Guid)));
            foreach (var kv in files)
            {
                ConfigFile f = kv.Value; if (f == null) continue;
                foreach (ConfigDefinition d in f.Keys)
                {
                    ConfigEntryBase e = f[d];
                    if (e == null || d.Section == "Internal" || d.Section == "Keys" || (kv.Key == BundlePlugin.Guid && d.Section == "Menu") || BundleMenu.Hidden.Contains(e) || e.SettingType == typeof(KeyCode) || e.SettingType == typeof(KeyboardShortcut)) continue;
                    if (d.Key == "Join Message") continue;
                    string desc = e.Description != null ? e.Description.Description : null;
                    if (desc != null && desc.IndexOf("Not used since", StringComparison.Ordinal) >= 0) continue;
                    uint id = Id(kv.Key, d);
                    if (!seen.Add(id)) continue;
                    bool exp = d.Section == "Experimental" || d.Section == "Test" || (d.Section == "Tiers" && d.Key == "Experimental");
                    list.Add(new Item { Id = id, Entry = e, Experimental = exp, Game = ReferenceEquals(e, Players.EnemyOutline) || ReferenceEquals(e, Players.TeamOutline) });
                }
            }
            return list;
        }

        private static string DefaultText(ConfigEntryBase e) { return TomlTypeConverter.ConvertToString(e.DefaultValue, e.SettingType); }

        internal static string Clean(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in (name ?? "").Trim()) if (c >= ' ' && c != ':' && c != 127 && sb.Length < 24) sb.Append(c);
            return sb.ToString().Trim();
        }

        internal static Profile Find(string name)
        {
            foreach (Profile p in All) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        /// <summary>The setup as it is now.</summary>
        internal static Profile Capture(string name)
        {
            var p = new Profile { Name = name };
            foreach (Item it in Items())
            {
                string v = it.Entry.GetSerializedValue();
                if (it.Game || v != DefaultText(it.Entry)) p.Values[it.Id] = v;
            }
            return p;
        }

        /// <summary>Makes the setup what the profile says. Returns how many settings changed.</summary>
        internal static int Apply(Profile p)
        {
            int changed = 0;
            var files = new List<ConfigFile>(); var saved = new List<bool>();
            List<Item> items = Items();
            // one write per settings file at the end, not one per setting
            foreach (Item it in items)
                if (!files.Contains(it.Entry.ConfigFile)) { files.Add(it.Entry.ConfigFile); saved.Add(it.Entry.ConfigFile.SaveOnConfigSet); it.Entry.ConfigFile.SaveOnConfigSet = false; }
            // many settings change at once: the sound mod does not play each newly picked sound
            CombatSounds.CombatSoundsPlugin.Quiet = true;
            try
            {
                foreach (Item it in items)
                {
                    string want;
                    if (!p.Values.TryGetValue(it.Id, out want)) { if (it.Game) continue; want = DefaultText(it.Entry); }
                    if (it.Entry.GetSerializedValue() == want) continue;
                    try { it.Entry.SetSerializedValue(want); changed++; } catch { }
                }
            }
            finally
            {
                CombatSounds.CombatSoundsPlugin.Quiet = false;
                for (int i = 0; i < files.Count; i++) { files[i].SaveOnConfigSet = saved[i]; try { files[i].Save(); } catch { } }
            }
            return changed;
        }

        // ---- as text

        internal static string Encode(Profile p)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write((byte)1);
                w.Write(p.Name);
                w.Write((ushort)p.Values.Count);
                foreach (var kv in p.Values) { w.Write(kv.Key); w.Write(kv.Value); }
                w.Flush();
                return Tag + p.Name + ":" + Convert.ToBase64String(ms.ToArray());
            }
        }

        internal static Profile Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int at = text.IndexOf(Tag, StringComparison.Ordinal);
            if (at < 0) return null;
            text = text.Substring(at + Tag.Length);
            int colon = text.IndexOf(':');   // a name has no colon in it
            if (colon < 0) return null;
            // the code ends where the letters and digits of it end (whatever was pasted after it is not part of it)
            var code = new StringBuilder();
            for (int i = colon + 1; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '=') code.Append(c);
                else if (c == ' ' || c == '\r' || c == '\n' || c == '\t') continue;
                else break;
            }
            try
            {
                using (var r = new BinaryReader(new MemoryStream(Convert.FromBase64String(code.ToString())), Encoding.UTF8))
                {
                    if (r.ReadByte() != 1) return null;
                    var p = new Profile { Name = Clean(r.ReadString()) };
                    if (p.Name.Length == 0) p.Name = "imported";
                    int n = r.ReadUInt16();
                    for (int i = 0; i < n; i++) { uint id = r.ReadUInt32(); p.Values[id] = r.ReadString(); }
                    return p;
                }
            }
            catch { return null; }
        }

        // ---- kept on disk

        internal static void Read()
        {
            if (_read) return;
            _read = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    Profile p = Decode(line);
                    if (p != null && Find(p.Name) == null) All.Add(p);
                }
            }
            catch (Exception e) { BundlePlugin.Log.LogWarning("Profiles could not be read: " + e.Message); }
        }

        private static void Write()
        {
            try
            {
                var lines = new List<string>();
                foreach (Profile p in All) lines.Add(Encode(p));
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception e) { BundlePlugin.Log.LogWarning("Profiles could not be saved: " + e.Message); }
        }

        private static void Put(Profile p)
        {
            Profile old = Find(p.Name);
            if (old != null) All[All.IndexOf(old)] = p; else All.Add(p);
        }

        // ---- what the menu's rows do; each returns the line the menu shows

        internal static string SaveAs(string name)
        {
            name = Clean(name);
            if (name.Length == 0) return Lang.T("No name given, nothing saved.");
            bool was = Find(name) != null;
            Put(Capture(name)); Write();
            return Lang.T(was ? "Overwrote " : "Saved ") + name + ".";
        }

        internal static string Load(Profile p)
        {
            // what you had is kept under one name, so the last load can always be taken back
            if (!string.Equals(p.Name, Before, StringComparison.OrdinalIgnoreCase)) { Put(Capture(Before)); Write(); }
            int n = Apply(p);
            return Loaded(p.Name, n) + (string.Equals(p.Name, Before, StringComparison.OrdinalIgnoreCase) ? "" : Lang.T(WhatYouHad));
        }

        // ---- the setups that come with it

        // Dorsth's own: what differs from the defaults. His outline settings and anything experimental are left out.
        private static readonly string[,] Dorsth =
        {
            { "dorsth.straftat.ezcrosshair", "Crosshair", "Crosshair", "plus" },
            { "dorsth.straftat.ezcrosshair", "Crosshair", "Color", "FF00FFFF" },
            { "dorsth.straftat.ezcrosshair", "Crosshair", "Show While Scoped", "true" },
            { "dorsth.straftat.viewmodeltweaks", "Viewmodel", "Viewmodel FOV", "80" },
            { "dorsth.straftat.viewmodeltweaks", "Viewmodel", "Viewmodel Y Offset", "-1.250001" },
            { "dorsth.straftat.viewmodeltweaks", "Viewmodel", "Viewmodel Z Offset", "1.6" },
            { "dorsth.straftat.viewmodeltweaks", "Viewmodel", "Viewmodel Sway Scale", "0" },
            { "dorsth.straftat.viewmodeltweaks", "Viewmodel", "Viewmodel Bob Scale", "0.35" },
            { "dorsth.straftat.viewmodeltweaks", "Weapon 1", "Weapon", "Katana" },
            { "dorsth.straftat.viewmodeltweaks", "Weapon 1", "Invisible Arms", "true" },
            { "dorsth.straftat.viewmodeltweaks", "Weapon 1", "Viewmodel FOV", "90" },
            { "dorsth.straftat.viewmodeltweaks", "Weapon 1", "Viewmodel Y Offset", "-1" },
            { "dorsth.straftat.viewmodeltweaks", "Weapon 1", "Viewmodel Z Offset", "5" },
            { "dorsth.straftat.viewmodeltweaks", "Weapon 2", "Weapon", "Flamberge" },
            { "dorsth.straftat.viewmodeltweaks", "Weapon 2", "Invisible Arms", "true" },
            { "dorsth.straftat.viewmodeltweaks", "Weapon 2", "Viewmodel FOV", "90" },
            { "dorsth.straftat.viewmodeltweaks", "Weapon 2", "Viewmodel Y Offset", "-1" },
            { "dorsth.straftat.viewmodeltweaks", "Weapon 2", "Viewmodel Z Offset", "5" },
            { "dorsth.straftat.dorsthtweaks", "Bullet Trails", "Hide My Trails", "true" },
            { "dorsth.straftat.dorsthtweaks", "HUD", "Static Hitmarkers", "true" },
            { "dorsth.straftat.dorsthtweaks", "HUD", "Same Headshot Animation", "true" },
            { "dorsth.straftat.dorsthtweaks", "HUD", "Hitmarker Shotgun Color", "FFFFFFFF" },
            { "dorsth.straftat.dorsthtweaks", "HUD", "Hide Near Miss Vignette", "true" },
            { "dorsth.straftat.dorsthtweaks", "Interaction", "Hide Key Prompt", "true" },
            { "dorsth.straftat.dorsthtweaks", "Interaction", "Prompt Scale", "0.8" },
            { "dorsth.straftat.dorsthtweaks", "Menus", "Hide Default Playlists", "true" },
            { "dorsth.straftat.dorsthtweaks", "Menus", "Reduce UI Animations", "true" },
            { "dorsth.straftat.dorsthtweaks", "Viewmodel", "No Viewmodel Climb", "true" },
            { "dorsth.straftat.dorsthtweaks", "Viewmodel", "No Bullet Casings", "true" },
            { "dorsth.straftat.dorsthtweaks", "World", "Reduce VFX+", "true" },
            { "dorsth.straftat.dorsthtweaks", "World", "No Post-Processing", "true" },
            { "dorsth.straftat.nogore", "Bodies", "Hide Ragdolls", "true" },
            { "dorsth.straftat.nogore", "Bodies", "Hide Hats", "true" },
            { "dorsth.straftat.nogore", "Blood", "No Hit Blood", "true" },
            { "dorsth.straftat.nogore", "Blood", "No Blood Stains", "true" },
            { "dorsth.straftat.combatsounds", "Sounds", "Hit Sound", "keyboard hit" },
            { "dorsth.straftat.combatsounds", "Sounds", "Head Hit Sound", "keyboard hs" },
            { "dorsth.straftat.combatsounds", "Sounds", "Melee Kill Sound", "sword cut" },
            { "dorsth.straftat.combatsounds", "Sounds", "Melee Kill Sound Volume", "200" },
            { "dorsth.straftat.monolith", "Players", "Enemy Outfit", "JumpSuit00" },
            { "dorsth.straftat.monolith", "Players", "Enemy Hat", "No Hat" },
            { "dorsth.straftat.monolith", "Players", "Team Outfit", "JumpSuit00" },
            { "dorsth.straftat.monolith", "Players", "Team Hat", "No Hat" },
            { "dorsth.straftat.monolith", "Frame Cap", "Enabled", "true" },
            { "dorsth.straftat.monolith", "Frame Cap", "FPS", "390" }
        };

        private static Profile Made(string name, string[,] rows)
        {
            var p = new Profile { Name = name };
            for (int i = 0; i < rows.GetLength(0); i++) p.Values[Id(rows[i, 0], new ConfigDefinition(rows[i, 1], rows[i, 2]))] = rows[i, 3];
            return p;
        }

        private static string LoadMade(string name, string[,] rows)
        {
            Profile p = Made(name, rows);
            KeepMyExperimental(p);
            // Dorsth's outline settings are not in his setup: yours are left as they are
            if (rows == Dorsth)
                foreach (Item it in Items())
                {
                    ConfigDefinition d = it.Entry.Definition;
                    if (!(d.Section == "Players" && d.Key.IndexOf("Outline", StringComparison.Ordinal) >= 0)) continue;
                    string mine = it.Entry.GetSerializedValue();
                    if (it.Game || mine != DefaultText(it.Entry)) p.Values[it.Id] = mine; else p.Values.Remove(it.Id);
                }
            Put(Capture(Before)); Write();
            int n = Apply(p);
            return Loaded(name, n) + Lang.T(WhatYouHad);
        }

        // experimental settings are never switched by a profile that is not your own: yours stay as they are
        private static int KeepMyExperimental(Profile p)
        {
            int had = 0;
            foreach (Item it in Items())
            {
                if (!it.Experimental) continue;
                string mine = it.Entry.GetSerializedValue();
                if (p.Values.ContainsKey(it.Id)) had++;
                if (mine != DefaultText(it.Entry)) p.Values[it.Id] = mine; else p.Values.Remove(it.Id);
            }
            return had;
        }

        /// <summary>Your setup as it is now, under your Steam name, as text on the clipboard.</summary>
        internal static string ExportNow()
        {
            string name = "";
            try { name = Clean(Steamworks.SteamFriends.GetPersonaName() ?? ""); } catch { }
            return Export(Capture(name.Length > 0 ? name : "shared"));
        }

        internal static string Export(Profile p)
        {
            try { GUIUtility.systemCopyBuffer = Encode(p); return Lang.T("Copied. Paste it to a friend. They copy the line and use Import from clipboard."); }
            catch (Exception e) { return Lang.T("Could not reach the clipboard: ") + e.Message; }
        }

        internal static string Delete(Profile p)
        {
            All.Remove(p); Write();
            return Lang.T("Deleted ") + p.Name + ".";
        }

        internal static string Import()
        {
            string text;
            try { text = GUIUtility.systemCopyBuffer; } catch (Exception e) { return Lang.T("Could not reach the clipboard: ") + e.Message; }
            Profile p = Decode(text);
            if (p == null) return Lang.T("No config on the clipboard. Copy the whole line, from MONOLITH: to the end.");
            int kept = KeepMyExperimental(p);
            if (string.Equals(p.Name, Before, StringComparison.OrdinalIgnoreCase)) p.Name = "imported";
            // what you had is kept, so one Load brings it back
            Put(Capture(Before));
            string name = p.Name;
            for (int i = 2; Find(p.Name) != null && i < 100; i++) p.Name = Clean(name.Length > 21 ? name.Substring(0, 21) : name) + " " + i;
            Put(p);
            int n = Apply(p);
            Write();
            return Loaded(p.Name, n) + Lang.T(WhatYouHad) + (kept > 0 ? " " + Lang.Count(kept, " experimental setting was left out.", " experimental settings were left out.", "Experimental settings left out: ") + (Lang.Ru ? "." : "") : "");
        }

        // ---- the page

        private static MenuPage _page;

        // the lines the menu shows after a load
        private const string WhatYouHad = " What you had is \"" + Before + "\".";
        private static string Loaded(string name, int n)
        {
            return Lang.T("Loaded ") + name + (Lang.Ru ? ". " : ": ") + Lang.Count(n, " setting changed", " settings changed", "Settings changed: ") + ".";
        }

        /// <summary>The page built again (the language changed: a few of its lines hold a number).</summary>
        internal static void Refill() { if (_page != null) Fill(); }

        internal static MenuPage Page()
        {
            Read();
            if (_page == null) _page = new MenuPage(null, "Configs", "Save, load and share configs. Sensitivity and keys are not included.");
            Fill();
            return _page;
        }

        private static void Fill()
        {
            MenuPage pg = _page;
            pg.Rows.Clear(); pg.Cur = null;
            pg.Rows.Add(new MenuRow { Kind = RowKind.Action, Label = "Copy my config to clipboard", Info = "Your config as one line of text. Paste it to a friend.", Take = s => ExportNow() });
            pg.Rows.Add(new MenuRow { Kind = RowKind.Action, Label = "Import from clipboard", Info = "Copy a line from a friend (it starts with MONOLITH:), then press Enter here. Yours is kept as \"" + Before + "\".", Take = s => { string r = Import(); Fill(); return r; } });
            pg.Rows.Add(new MenuRow { Kind = RowKind.Action, Label = "Save my config as", Ask = "name", Info = "Type a name and press Enter. A config with the same name is replaced.", Take = s => { string r = SaveAs(s); Fill(); return r; } });
            foreach (Profile each in All)
            {
                Profile p = each;
                var sub = new MenuPage(pg, p.Name, Lang.Count(p.Values.Count, " setting differs from the defaults.", " settings differ from the defaults.", "Differences from the defaults: ") + (Lang.Ru ? "." : ""));
                sub.Rows.Add(new MenuRow { Kind = RowKind.Action, Label = "Load", Info = "Loads everything except sensitivity and keys.", Take = s => { string r = Load(p); Fill(); return r; } });
                sub.Rows.Add(new MenuRow { Kind = RowKind.Action, Label = "Copy to clipboard", Info = "This config as one line of text. Paste it to a friend.", Take = s => Export(p) });
                sub.Rows.Add(new MenuRow { Kind = RowKind.Action, Label = "Overwrite with current config", Confirm = true, Info = "Saves your current config under this name.", Take = s => { string r = SaveAs(p.Name); Fill(); return r; } });
                sub.Rows.Add(new MenuRow { Kind = RowKind.Action, Label = "Delete", Confirm = true, Info = "", Take = s => { string r = Delete(p); Fill(); return r; } });
                pg.Go(sub);
            }
            pg.Head("Presets");
            pg.Rows.Add(new MenuRow { Kind = RowKind.Action, Label = "Dorsth", Confirm = true, Info = "The config Dorsth plays with, except sensitivity, keys, outline settings and Performance viewmodel.", Take = s => { string r = LoadMade("Dorsth", Dorsth); Fill(); return r; } });
            pg.Rows.Add(new MenuRow { Kind = RowKind.Action, Label = "Defaults", Confirm = true, Info = "Every setting back to its default, except sensitivity, keys, outline colors and Performance viewmodel.", Take = s => { string r = LoadMade("Defaults", new string[0, 4]); Fill(); return r; } });
        }
    }
}
