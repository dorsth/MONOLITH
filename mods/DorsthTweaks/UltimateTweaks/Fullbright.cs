// Fullbright: the soft fill light that comes with DorsthTweaks' No Post-Processing. Without post-processing the game
// loses its own brightening, so a little light is given back: one white, shadowless sun-like light (0.1) on top of the
// game's own lighting, only on maps that need it (see SunCheck). A fill light is a real light, so glossy map materials
// (ice, metal) would answer it with glints the baked lighting never produced: their gloss is taken out while it is on
// (matte). Everything is put back when it goes off.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UltimateTweaks
{
    internal static class Fullbright
    {
        const float FillBrightness = 0.1f;
        public const string FillName = "UT_FullbrightFillLight";
        static string applied = "Off", appliedShine = ""; static float appliedB = -1f; static int sceneHandle = -1;
        static GameObject fill;

        /// Called on the slow housekeeping pass whenever settings or scenes changed.
        public static void Refresh()
        {
            string want = TweaksPlugin.Plain ? "FillLight" : "Off";
            if (TweaksPlugin.InMenu) want = "Off";
            float b = FillBrightness; int scene = SceneManager.GetActiveScene().handle; bool sceneChanged = scene != sceneHandle;
            string shine = want == "FillLight" ? "Matte" : "";
            // The fill light lives outside the maps and stays through round and map changes: nothing is rebuilt when only the active scene
            // switches (the game does that twice at every round start). Flat and Ambient hang on the map's own lighting data and are redone per scene.
            if (want == "FillLight") sceneChanged = false;
            if (want == applied && shine == appliedShine && Mathf.Approximately(appliedB, b) && !sceneChanged && StillOurs()) { if (want == "FillLight") { SunCheck(); if (shine != "Keep") QueueNewScenes(); } return; }
            try
            {
                Undo(sceneChanged);
                sceneHandle = scene; applied = want; appliedB = b; appliedShine = shine; sunSaid = null;
                if (want == "FillLight") { Fill(b); sunScenes.Clear(); SunCheck(); if (shine != "Keep") QueueNewScenes(); }
            }
            catch (Exception e) { TweaksPlugin.L.LogWarning("Fullbright could not be applied: " + e.Message); }
        }

        // a map load replaces the lightmap list; if ours is no longer in place it has to be applied again
        static bool StillOurs()
        {
            if (applied == "FillLight") return fill != null;
            return true;
        }

        static void Undo(bool sceneChanged)
        {
            if (fill != null) { UnityEngine.Object.Destroy(fill); fill = null; }
            foreach (var d in dulled) { if (!d.M) continue; if (d.Keyword) { d.M.DisableKeyword(SpecOff); continue; } if (d.IsColor) d.M.SetColor(d.Id, d.C); else d.M.SetFloat(d.Id, d.F); }
            dulled.Clear(); dullScenes.Clear(); dullSeen.Clear(); pending.Clear(); pendingAt = 0; touched.Clear();
        }

        // ---- shine on the map's materials
        const string SpecOff = "_SPECULARHIGHLIGHTS_OFF";
        static readonly int SpecFlag = Shader.PropertyToID("_SpecularHighlights");
        struct Dulled { public Material M; public bool Keyword, IsColor; public int Id; public float F; public Color C; }
        static readonly List<Dulled> dulled = new List<Dulled>();
        struct Prop { public int Id; public string Name; public bool IsColor; }
        static readonly Dictionary<Shader, List<Prop>> glossProps = new Dictionary<Shader, List<Prop>>();

        static bool Has(string n, params string[] words) { foreach (var w in words) if (n.Contains(w)) return true; return false; }

        // Which settings of a shader make a surface glossy, found by name. The game's maps mostly use its own shaders (S_Surfaces_00, S_ICE_00 ...),
        // which do not have the Standard shader's switches, so this goes by what the settings are called. Every change is logged.
        static List<Prop> GlossProps(Shader sh)
        {
            List<Prop> l; if (glossProps.TryGetValue(sh, out l)) return l;
            l = new List<Prop>();
            for (int i = 0; i < sh.GetPropertyCount(); i++)
            {
                string name = sh.GetPropertyName(i), n = name.ToLowerInvariant(); var t = sh.GetPropertyType(i);
                bool number = t == ShaderPropertyType.Float || t == ShaderPropertyType.Range;
                if (number && Has(n, "smooth", "gloss") && !Has(n, "min", "max", "remap", "channel", "use")) l.Add(new Prop { Id = Shader.PropertyToID(name), Name = name });
                else if (number && n.Contains("spec") && Has(n, "intensity", "strength", "amount", "scale", "mult")) l.Add(new Prop { Id = Shader.PropertyToID(name), Name = name });
                else if (t == ShaderPropertyType.Color && n.Contains("spec")) l.Add(new Prop { Id = Shader.PropertyToID(name), Name = name, IsColor = true });
            }
            glossProps[sh] = l; return l;
        }

        // Matte: the materials of the maps' meshes get their gloss taken out (shared materials: nothing is copied, the values are put back in Undo).
        // Standard-shader materials get their highlight switch set off; every material gets its gloss and specular settings set to 0.
        // A scene is listed once, when it is first seen, and then worked through a few renderers per frame (Tick), so a map load or a
        // round start never pays for it in one frame. Nothing here asks Unity for "all objects of a type": that walks every loaded object.
        static readonly HashSet<int> dullScenes = new HashSet<int>(), dullSeen = new HashSet<int>();
        static readonly List<MeshRenderer> pending = new List<MeshRenderer>(); static int pendingAt;
        static readonly Dictionary<string, string> touched = new Dictionary<string, string>(); static readonly List<Material> matBuf = new List<Material>();
        static string dullMap; static readonly HashSet<int> liveScenes = new HashSet<int>();
        static void QueueNewScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                if (!sc.isLoaded || sc.name == "MainMenu" || !dullScenes.Add(sc.handle)) continue;
                // Another map: the last map's materials are let go (their gloss put back first), so the game can free
                // them and their textures. Holding on to them keeps every map's materials in memory for the whole
                // session.
                string n = sc.name;
                if (n != "EmptyScene" && n != "VictoryScene" && n != "MovedObjectsHolder" && n != "DontDestroyOnLoad" && n != dullMap)
                {
                    if (dullMap != null)
                    {
                        foreach (var d in dulled) { if (!d.M) continue; if (d.Keyword) { d.M.DisableKeyword(SpecOff); continue; } if (d.IsColor) d.M.SetColor(d.Id, d.C); else d.M.SetFloat(d.Id, d.F); }
                        dulled.Clear(); dullSeen.Clear(); touched.Clear();
                        liveScenes.Clear(); for (int k = 0; k < SceneManager.sceneCount; k++) liveScenes.Add(SceneManager.GetSceneAt(k).handle);
                        dullScenes.RemoveWhere(h => !liveScenes.Contains(h));
                    }
                    dullMap = n;
                }
                foreach (var root in sc.GetRootGameObjects()) if (root.layer != 5) pending.AddRange(root.GetComponentsInChildren<MeshRenderer>(true));
            }
        }
        /// Every frame: a small piece of the waiting material work.
        public static void Tick()
        {
            if (pendingAt >= pending.Count) return;
            int end = Math.Min(pending.Count, pendingAt + 24);
            for (; pendingAt < end; pendingAt++)
            {
                var r = pending[pendingAt]; if (r == null || r.gameObject.layer == 5) continue;
                r.GetSharedMaterials(matBuf);
                foreach (var m in matBuf)
                {
                    if (m == null || m.shader == null || !dullSeen.Add(m.GetInstanceID())) continue;
                    string what = "";
                    if (m.HasProperty(SpecFlag))
                    {
                        if (!m.IsKeywordEnabled(SpecOff)) { m.EnableKeyword(SpecOff); dulled.Add(new Dulled { M = m, Keyword = true }); }
                        dulled.Add(new Dulled { M = m, Id = SpecFlag, F = m.GetFloat(SpecFlag) }); m.SetFloat(SpecFlag, 0f); what = "highlight switch";
                    }
                    foreach (var pr in GlossProps(m.shader))
                    {
                        if (pr.IsColor) { var c = m.GetColor(pr.Id); dulled.Add(new Dulled { M = m, Id = pr.Id, IsColor = true, C = c }); m.SetColor(pr.Id, new Color(0f, 0f, 0f, c.a)); what += (what.Length > 0 ? ", " : "") + pr.Name; }
                        else { float f = m.GetFloat(pr.Id); dulled.Add(new Dulled { M = m, Id = pr.Id, F = f }); m.SetFloat(pr.Id, 0f); what += (what.Length > 0 ? ", " : "") + pr.Name; }
                    }
                    if (what.Length > 0 && !touched.ContainsKey(m.shader.name)) touched[m.shader.name] = what;
                }
            }
            if (pendingAt >= pending.Count)
            {
                int n = pending.Count; pending.Clear(); pendingAt = 0;
                if (dulled.Count > 4000) dulled.RemoveAll(d => !d.M);   // materials of maps that are gone
                var sb = new System.Text.StringBuilder("Fullbright fill light, shine Matte: " + n + " meshes looked at, gloss taken out on");
                foreach (var kv in touched) sb.Append(" [" + kv.Key + ": " + kv.Value + "]");
                DorsthTweaks.DorsthTweaksPlugin.Note(sb.ToString()); touched.Clear();
            }
        }

        // The fill light is a second sun-type light drawn per pixel. Under forward rendering the engine draws every
        // object once more for the second such light: measured on Chateaux_07_NoGrass_01, 2733 draw calls and 1481
        // SetPass calls a frame with it, 1383 and 90 without (about 520 against 810 FPS there: the map has 1326
        // separately drawn chunk parts); on a second map 858 against 451. So on a map that has a sun of its own the fill
        // light can stay off; elsewhere it is the only such light and costs nothing extra. Decided once per loaded scene
        // from that scene's own lights; nothing per frame.
        static readonly HashSet<int> sunScenes = new HashSet<int>(); static readonly List<Light> lightBuf = new List<Light>(); static readonly List<DetachableObject> chunkBuf = new List<DetachableObject>(); static string sunSaid;
        static void SunCheck()
        {
            if (fill == null) return;
            bool looked = false, sun = false; string map = null;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i); string n = sc.name;
                if (!sc.isLoaded || n == "MainMenu" || n == "EmptyScene" || n == "VictoryScene" || n == "MovedObjectsHolder" || n == "DontDestroyOnLoad") continue;
                if (sunScenes.Add(sc.handle)) looked = true;
                map = n;
            }
            if (!looked) return;
            if (sunScenes.Count > 64) sunScenes.Clear();
            // With the fill light off everywhere a sun exists, maps like Prison get very dark: where the lighting is
            // baked into lightmaps, the fill light is most of what lifts the dark parts. So it goes off only where it is
            // not needed or costs far too much: a map with a sun and no lightmaps (lit by that sun as it is), or a map
            // with many chunks that can be shot off.
            int chunks = 0; string sunKind = "none";
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i); string n = sc.name;
                if (!sc.isLoaded || n == "MainMenu" || n == "EmptyScene" || n == "VictoryScene" || n == "MovedObjectsHolder" || n == "DontDestroyOnLoad") continue;
                foreach (var root in sc.GetRootGameObjects())
                {
                    root.GetComponentsInChildren(false, lightBuf);
                    foreach (Light l in lightBuf)
                        // a sun of the map: directional, on, not baked into the lightmaps only, and lighting the default layer (the game's HUD lights light the weapon's layer only)
                        if (l.enabled && l.type == LightType.Directional && (l.cullingMask & 1) != 0 && l.intensity > 0f && !(l.bakingOutput.isBaked && l.bakingOutput.lightmapBakeType == LightmapBakeType.Baked))
                        { sun = true; sunKind = (l.bakingOutput.isBaked ? "mixed" : "realtime") + " " + l.intensity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); break; }
                    lightBuf.Clear();
                    root.GetComponentsInChildren(false, chunkBuf); chunks += chunkBuf.Count; chunkBuf.Clear();
                }
            }
            int maps = LightmapSettings.lightmaps.Length;
            // 50 chunks, not 100: Chateaux_06_NoGrass has 99 (858 draw calls a frame with the fill light against 451
            // without). Putting the lift into the lightmaps instead of a light gains nothing (Prison_00: 1397 against
            // 1410 FPS, for 64 MB of graphics memory and a flatter look).
            bool off = sun && (maps == 0 || chunks >= 50);
            var fl = fill.GetComponent<Light>();
            if (fl != null && fl.enabled == off) fl.enabled = !off;
            if (map != null && map != sunSaid)
            {
                sunSaid = map;
                DorsthTweaks.DorsthTweaksPlugin.Note("Fullbright fill light: " + (off ? "off" : "on") + " on " + map + " (sun of its own: " + sunKind + ", lightmaps: " + maps + ", chunks that can be shot off: " + chunks
                    + (off ? "; a second sun-type light would draw every object twice" : sun ? "; kept for the dark parts, every object is drawn twice" : "") + ")");
            }
        }

        static void Fill(float b)
        {
            fill = new GameObject(FillName) { hideFlags = HideFlags.DontSave };
            UnityEngine.Object.DontDestroyOnLoad(fill);   // one light for the whole session: it is not rebuilt at round or map changes
            fill.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            var l = fill.AddComponent<Light>(); l.type = LightType.Directional; l.color = Color.white; l.intensity = b; l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel; l.cullingMask = ~0;
        }
    }
}
