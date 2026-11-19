// MONOLITH - finds out whether a newer MONOLITH is on Thunderstore and, when it was not installed by a mod manager,
// puts it in place itself. Dorsth was here.
//
// A mod manager (r2modman, Thunderstore Mod Manager, Gale) keeps its own list of what version each mod is. Swapping
// the files behind its back would leave that list wrong, so inside a manager's profile this only says where to update.
// What is downloaded: only Dorsth's own package, from thunderstore.io, at a version number checked to be just that.
// Windows does not let a loaded dll be overwritten but does let it be renamed: the running one is moved aside to
// .old, the new one takes its name and loads at the next start, which also clears the .old files away.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Networking;

namespace Monolith
{
    internal static class Updates
    {
        private const string Package = "Dorsth/MONOLITH";
        private const string Api = "https://thunderstore.io/api/experimental/package/" + Package + "/";
        private const string Download = "https://thunderstore.io/package/download/" + Package + "/";
        private const string InZip = "BepInEx/plugins/MONOLITH/";

        internal static ConfigEntry<bool> AtStart;
        private static string _latest, _manager;
        private static bool _busy, _ready;

        internal static void Bind(ConfigFile c)
        {
            AtStart = c.Bind("General", "Check For Updates", true, "At start, asks Thunderstore once whether a newer MONOLITH is out.");
        }

        internal static void Start(MonoBehaviour host)
        {
            _manager = Manager(Dll);
            ClearOld();
            if (AtStart.Value) host.StartCoroutine(Ask(null));
        }

        private static string Dll { get { return typeof(BundlePlugin).Assembly.Location; } }
        private static bool Newer { get { return _latest != null && new Version(_latest) > new Version(BundlePlugin.Version); } }

        /// <summary>What the menu row shows on its right.</summary>
        internal static string Status()
        {
            if (_ready) return Lang.T("restart to finish");
            if (_busy) return "...";
            if (_latest == null) return BundlePlugin.Version;
            return Newer ? _latest + Lang.T(" is out") : BundlePlugin.Version + Lang.T(", newest");
        }

        internal static bool Ready() { return _ready; }

        /// <summary>Enter on the row: check, or update when a newer one is known.</summary>
        internal static string Use(MonoBehaviour host)
        {
            if (_busy) return Lang.T("Busy, one moment.");
            if (_ready) return Lang.T("Restart the game to finish the update.");
            if (!Newer) { host.StartCoroutine(Ask(null)); return Lang.T("Checking Thunderstore."); }
            if (_manager != null) return Lang.T("MONOLITH was installed with ") + _manager + Lang.T(". Update it there.");
            host.StartCoroutine(Ask(_latest));
            return Lang.T("Downloading ") + _latest + "...";
        }

        internal static string Restart()
        {
            try
            {
                uint app = Steamworks.SteamUtils.GetAppID().m_AppId;
                System.Diagnostics.Process.Start("steam://rungameid/" + app);
                Application.Quit();
                return null;
            }
            catch (Exception e) { return Lang.T("Could not restart: ") + e.Message; }
        }

        // the files of a mod manager's profile sit under its own folder, in a "profiles" folder
        private static string Manager(string path)
        {
            string p = (path ?? "").Replace('/', '\\').ToLowerInvariant();
            if (p.Contains("r2modman")) return "r2modman";
            if (p.Contains("thunderstore mod manager")) return "Thunderstore Mod Manager";
            if (p.Contains("gale") && p.Contains("\\profiles\\")) return "Gale";
            if (p.Contains("\\profiles\\")) return Lang.T("your mod manager");
            return null;
        }

        // ask for the newest version, then (when install is set) download that version and put it in place
        private static IEnumerator Ask(string install)
        {
            _busy = true;
            try
            {
                if (install == null)
                {
                    using (UnityWebRequest req = UnityWebRequest.Get(Api))
                    {
                        req.timeout = 10;
                        yield return req.SendWebRequest();
                        if (req.result != UnityWebRequest.Result.Success) { BundlePlugin.Log.LogInfo("Updates: Thunderstore did not answer: " + req.error); yield break; }
                        Match m = Regex.Match(req.downloadHandler.text, "\"latest\"\\s*:\\s*\\{[^{}]*?\"version_number\"\\s*:\\s*\"(\\d+\\.\\d+\\.\\d+)\"");
                        if (m.Success) _latest = m.Groups[1].Value;
                        if (Newer) BundlePlugin.Log.LogMessage("MONOLITH " + _latest + " is out (you have " + BundlePlugin.Version + ").");
                    }
                    yield break;
                }
                if (!Regex.IsMatch(install, "^\\d+\\.\\d+\\.\\d+$")) yield break;
                using (UnityWebRequest req = UnityWebRequest.Get(Download + install + "/"))
                {
                    req.timeout = 60;
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success) { BundlePlugin.Log.LogWarning("Updates: the download failed: " + req.error); yield break; }
                    try { Put(req.downloadHandler.data); _ready = true; BundlePlugin.Log.LogMessage("MONOLITH " + install + " is in place. It loads at the next start."); }
                    catch (Exception e) { BundlePlugin.Log.LogWarning("Updates: the new version could not be put in place, nothing was changed: " + e.Message); }
                }
            }
            finally { _busy = false; }
        }

        // the package's own folder goes next to the running dll; anything outside it in the zip is left alone
        private static void Put(byte[] zip)
        {
            string dir = Path.GetDirectoryName(Dll);
            bool own = Path.GetFileName(dir).IndexOf("MONOLITH", StringComparison.OrdinalIgnoreCase) >= 0;   // a folder of its own, not plugins itself
            var files = new List<KeyValuePair<string, byte[]>>();
            foreach (KeyValuePair<string, byte[]> f in Unzip(zip))
            {
                if (!f.Key.StartsWith(InZip, StringComparison.Ordinal) || f.Key.EndsWith("/", StringComparison.Ordinal)) continue;
                string rel = f.Key.Substring(InZip.Length);
                if (rel.Contains("..")) continue;
                if (!own && rel != "MONOLITH.dll") continue;
                files.Add(new KeyValuePair<string, byte[]>(rel, f.Value));
            }
            if (!files.Exists(f => f.Key == "MONOLITH.dll")) throw new InvalidDataException("no MONOLITH.dll in the download");
            foreach (KeyValuePair<string, byte[]> f in files)
            {
                string to = Path.Combine(dir, f.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                if (to.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(to))
                {
                    string old = to + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(to, old);
                }
                File.WriteAllBytes(to, f.Value);
            }
        }

        private static void ClearOld()
        {
            try
            {
                string dir = Path.GetDirectoryName(Dll);
                foreach (string f in Directory.GetFiles(dir, "*.dll.old")) { try { File.Delete(f); } catch { } }
            }
            catch { }
        }

        // a zip's own table of contents, read by hand: the game has no zip reader, only the deflate it uses
        private static IEnumerable<KeyValuePair<string, byte[]>> Unzip(byte[] z)
        {
            int end = -1;
            for (int i = z.Length - 22; i >= Math.Max(0, z.Length - 65557); i--)
                if (BitConverter.ToUInt32(z, i) == 0x06054b50) { end = i; break; }
            if (end < 0) throw new InvalidDataException("not a zip");
            int count = BitConverter.ToUInt16(z, end + 10), at = (int)BitConverter.ToUInt32(z, end + 16);
            for (int n = 0; n < count; n++)
            {
                if (BitConverter.ToUInt32(z, at) != 0x02014b50) throw new InvalidDataException("broken zip");
                int method = BitConverter.ToUInt16(z, at + 10);
                int packed = (int)BitConverter.ToUInt32(z, at + 20), size = (int)BitConverter.ToUInt32(z, at + 24);
                int nameLen = BitConverter.ToUInt16(z, at + 28), extra = BitConverter.ToUInt16(z, at + 30), note = BitConverter.ToUInt16(z, at + 32);
                int local = (int)BitConverter.ToUInt32(z, at + 42);
                string name = System.Text.Encoding.UTF8.GetString(z, at + 46, nameLen).Replace('\\', '/');
                at += 46 + nameLen + extra + note;
                int data = local + 30 + BitConverter.ToUInt16(z, local + 26) + BitConverter.ToUInt16(z, local + 28);
                byte[] bytes = new byte[size];
                if (method == 0) Buffer.BlockCopy(z, data, bytes, 0, size);
                else if (method == 8)
                {
                    using (var s = new DeflateStream(new MemoryStream(z, data, packed), CompressionMode.Decompress))
                    {
                        int got = 0, r;
                        while (got < size && (r = s.Read(bytes, got, size - got)) > 0) got += r;
                        if (got != size) throw new InvalidDataException("short file in zip: " + name);
                    }
                }
                else throw new InvalidDataException("unknown packing in zip: " + name);
                yield return new KeyValuePair<string, byte[]>(name, bytes);
            }
        }
    }
}
