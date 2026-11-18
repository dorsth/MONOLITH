// Ultimate Tweaks - performance fixes to the game's code. Dorsth was here.
// Part of DorsthTweaks. One setting, "Ultimate Tweaks", switches every tweak here on or off. None of them changes what
// you see or hear, hit registration, damage, or what is sent to other players.
// Forward rendering (with the depth picture kept) and the soft fill light come with DorsthTweaks' No Post-Processing;
// switching the post-processing layers off is DorsthTweaks' own code.
// Every tweak has a name, so a benchmark (Ultimate Diag) can run them one by one through Test(); that is never saved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;

namespace UltimateTweaks
{
    internal sealed class TweaksPlugin : MonoBehaviour
    {
        public const string Guid = "dorsth.straftat.ultimatetweaks";   // the Harmony id, as before
        internal static ManualLogSource L; internal static TweaksPlugin I;
        /// DorsthTweaks' "Ultimate Tweaks" setting.
        internal static ConfigEntry<bool> Master;
        /// Benchmarks only (Ultimate Diag, by reflection through Test). Empty = normal; "none" = every tweak off; tweak names
        /// joined with '+' = only those; "tier:Safe" / "tier:Experimental" / "tier:All" as before. Never saved.
        internal static string Solo = "";
        static readonly Dictionary<string, string> tierOf = new Dictionary<string, string>();

        // live state read by the patches (plain statics: cheapest possible check)
        internal static ulong GcStepNs = 250000;   // 0.25 ms a frame for the garbage collector (the game's own is 3 ms)
        internal static bool FadedLines, KillCamText, LineMatOne, ArcPath, WeaponLayer, GuiNoLayout, FovReuse, SvNoSearch, OtherArcs, FovPile, MycPoll, ItemNameOn, SceneNameOn, HudText, LobbyRowsLate, DecalSmall, MouseEventsOff, CrosshairFix, LobbyThrottle, SteamCheckThrottle, PreviewDollIdle, Plain, Forward, OwnTagCanvasOff, KillCamLean, WeaponInputCopyOff, DeadInputOff, DeadNetOff, GcSteps, VoiceOffThread, PreCompileOn, JobLimit, TrickLine, GunScan;
        /// Worker threads with JobWorkerLimit. A benchmark can try others through Test("jobworkers", n).
        internal static int JobCount = 2;
        internal static float LobbyInterval = 0.2f;   // the lobby list is rebuilt 5 times a second during a match
        internal static bool InMenu = true;

        /// Called by DorsthTweaks once its settings are bound.
        internal static void Create(GameObject host, ConfigEntry<bool> master, ManualLogSource log)
        {
            Master = master; L = log;
            host.AddComponent<TweaksPlugin>();
        }

        /// For Ultimate Diag (by reflection): "solo" = see Solo; "jobworkers" = the worker threads with JobWorkerLimit.
        /// Returns null when done, otherwise what was wrong.
        public static string Test(string key, string value)
        {
            switch ((key ?? "").ToLowerInvariant())
            {
                case "solo": Solo = value ?? ""; break;
                case "jobworkers": int n; if (!int.TryParse(value, out n)) return "jobworkers needs a number"; JobCount = n; break;
                default: return "unknown key " + key;
            }
            if (I != null) I.Refresh();
            return null;
        }

        Harmony lateHarmony;
        // Plugins that load after this one are not there yet in Awake; Start runs when all of them are.
        void Start()
        {
            try { if (lateHarmony != null) Patches.ApplyLate(lateHarmony); } catch (Exception e) { L.LogWarning("Late patching failed: " + e.Message); }
            Patches.PreInitOnMainThread();
            Refresh();
        }

        void Awake()
        {
            I = this;
            // What each tweak does and what it measured: ULTIMATE-TWEAKS.md. The tier only matters to a benchmark (Test).
            foreach (string n in new[]
            {
                "FadedLinesEndEarly", "NoMouseEvents", "CrosshairFix", "LobbyListThrottle", "SteamRunningCheckThrottle", "PreviewDollIdle",
                "OwnNameTagCanvasOff", "KillCamLean", "WeaponNoInputCopy", "DeadInputCallbacks", "TrickShotLineNoGarbage", "GunSoundsNoScanInPlay",
                "DecalSmallBuffers", "ItemNameNoGarbage", "SceneNameNoGarbage", "HudNumbersNoGarbage", "LobbyRowsNotInPlay", "MyceliumPollRate",
                "OtherArcsAtScreenRate", "FovTweensNoPileUp", "SpawnViewerNoSearch", "WeaponLayerNoGarbage", "OnGuiNoLayout", "FovTweensReuse",
                "ArcPathNoGarbage", "ExplosionLinesOneMaterial", "KillCamNoGarbage", "DeadNetworkObjects", "GcSmallSteps"
            }) tierOf[n] = "Safe";
            foreach (string n in new[] { "PreCompile", "VoiceKeyOffThread", "JobWorkerLimit" }) tierOf[n] = "Experimental";
            Master.SettingChanged += (s, e) => Refresh();
            try { DorsthTweaks.DorsthTweaksPlugin.NoPostProcessing.SettingChanged += (s, e) => Refresh(); } catch { }
            SceneManager.sceneLoaded += (sc, m) => { Keeper.SceneLoaded(sc); Keeper.Dirty = true; }; SceneManager.sceneUnloaded += sc => { Keeper.SceneUnloaded(sc); };

            try { Patches.Apply(new Harmony(Guid)); } catch (Exception e) { L.LogError("Patching failed: " + e); }
            lateHarmony = new Harmony(Guid + ".late");
            var go = new GameObject("UltimateTweaks") { hideFlags = HideFlags.HideAndDontSave }; DontDestroyOnLoad(go); go.AddComponent<Keeper>();
            SceneManager.activeSceneChanged += (a, b) => { InMenu = b.name == "MainMenu"; Keeper.Dirty = true; };
            InMenu = SceneManager.GetActiveScene().name == "MainMenu";
            Refresh();
            L.LogInfo("Ultimate Tweaks " + (Master.Value ? "on: " + string.Join(", ", tierOf.Keys.Where(Active).ToArray()) : "off"));
        }

        bool Active(string name)
        {
            string s = (Solo ?? "").Trim(), tier = tierOf[name];
            if (s.Length == 0) return Master.Value;
            if (s.Equals("none", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (var raw in s.Split('+'))
            {
                string x = raw.Trim();
                if (x.StartsWith("tier:", StringComparison.OrdinalIgnoreCase)) { string t = x.Substring(5); if (t.Equals("All", StringComparison.OrdinalIgnoreCase) || t.Equals(tier, StringComparison.OrdinalIgnoreCase)) return true; }
                else if (x.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        internal void Refresh()
        {
            // DorsthTweaks' No Post-Processing brings forward rendering and the soft fill light (0.1, matte)
            bool plain = false; try { plain = DorsthTweaks.DorsthTweaksPlugin.NoPostProcessing.Value; } catch { }
            Plain = plain; Forward = plain;
            MouseEventsOff = Active("NoMouseEvents"); CrosshairFix = Active("CrosshairFix"); LobbyThrottle = Active("LobbyListThrottle"); SteamCheckThrottle = Active("SteamRunningCheckThrottle");
            PreviewDollIdle = Active("PreviewDollIdle"); OwnTagCanvasOff = Active("OwnNameTagCanvasOff"); JobLimit = Active("JobWorkerLimit");
            KillCamLean = Active("KillCamLean"); WeaponInputCopyOff = Active("WeaponNoInputCopy"); DeadInputOff = Active("DeadInputCallbacks"); DeadNetOff = Active("DeadNetworkObjects");
            TrickLine = Active("TrickShotLineNoGarbage"); GunScan = Active("GunSoundsNoScanInPlay");
            DecalSmall = Active("DecalSmallBuffers"); MycPoll = Active("MyceliumPollRate"); OtherArcs = Active("OtherArcsAtScreenRate"); FovPile = Active("FovTweensNoPileUp"); SvNoSearch = Active("SpawnViewerNoSearch"); WeaponLayer = Active("WeaponLayerNoGarbage"); ArcPath = Active("ArcPathNoGarbage"); LineMatOne = Active("ExplosionLinesOneMaterial"); FadedLines = Active("FadedLinesEndEarly"); KillCamText = Active("KillCamNoGarbage"); GuiNoLayout = Active("OnGuiNoLayout"); FovReuse = Active("FovTweensReuse") && FovPile; Patches.GuiLayoutRefresh();
            ItemNameOn = Active("ItemNameNoGarbage"); SceneNameOn = Active("SceneNameNoGarbage"); HudText = Active("HudNumbersNoGarbage"); LobbyRowsLate = Active("LobbyRowsNotInPlay");
            GcSteps = Active("GcSmallSteps"); VoiceOffThread = Active("VoiceKeyOffThread"); PreCompileOn = Active("PreCompile"); Patches.PreCompile();
            Keeper.Dirty = true;
        }
    }

    [DefaultExecutionOrder(-31000)]
    internal sealed class Keeper : MonoBehaviour
    {
        public static bool Dirty = true; float next; int lastCamCount = -1; Camera[] dollCams;
        static bool mouseRemoved; static PlayerLoopSystem mouseSys; static Type mousePhase; static int mouseIndex; static Type[] mouseHandlerTypes;
        // remembered originals
        readonly List<Behaviour> dollAnim = new List<Behaviour>();
        readonly List<KeyValuePair<Camera, RenderingPath>> camPaths = new List<KeyValuePair<Camera, RenderingPath>>();
        readonly List<Camera> camDepth = new List<Camera>();   // cameras this tweak asked the depth picture for

        /// The depth picture for the cameras moved to the forward path. Called by the housekeeping pass (a new camera, a setting).
        /// Always on with forward rendering. Making it only while a puddle, a grenade's ring or a mine exists gave +2.5% average
        /// FPS and nothing in the 1% lows, cost a 4 to 5 ms frame when it came on, and needed a complete list of what uses the picture.
        internal void Depth()
        {
            bool want = TweaksPlugin.Forward;
            if (want)
            {
                for (int i = 0; i < camPaths.Count; i++)
                {
                    Camera c = camPaths[i].Key;
                    if (c != null && (c.depthTextureMode & DepthTextureMode.Depth) == 0) { c.depthTextureMode |= DepthTextureMode.Depth; camDepth.Add(c); }
                }
            }
            else if (camDepth.Count > 0)
            {
                for (int i = 0; i < camDepth.Count; i++) if (camDepth[i] != null) camDepth[i].depthTextureMode &= ~DepthTextureMode.Depth;
                camDepth.Clear();
            }
            if (camDepth.Count > 32) camDepth.RemoveAll(c => c == null);   // cameras of players long gone
        }
        // The Visual tweaks are meant for the map. Cameras and lights that belong to menus, the HUD or a render-to-texture view are left as the game made them,
        // because menu and HUD parts can depend on their lighting and post-processing.
        internal static bool WorldCamera(Camera c)
        {
            if (c.targetTexture != null) return false;                       // draws into a texture (menu screens, previews), not onto the screen
            if ((c.cullingMask & 1) == 0 && c.GetComponentInParent<FirstPersonController>() == null) return false; // does not draw the map's default layer and is not part of a player (the weapon camera stays included)
            if (c.GetComponentInParent<Canvas>() != null) return false;
            return true;
        }
        readonly List<SkinnedMeshRenderer> dollSkin = new List<SkinnedMeshRenderer>(); GameObject dollRoot; bool dollIdle;

        void Update()
        {
            float t = Time.unscaledTime;
            Fullbright.Tick();
            // once more a few seconds after a scene loaded: by then the scene before it is gone and its objects are destroyed
            if (deadAgainAt > 0f && t >= deadAgainAt) { deadAgainAt = 0f; DeadInput(); DeadNet(); }
            if (Dirty || t >= next || Camera.allCamerasCount != lastCamCount)   // a camera that came or went (respawn, kill cam) gets the light pass at once
            {
                lastCamCount = Camera.allCamerasCount;
                // A "full" pass re-reads settings and scene-wide state. It runs after a scene load, a settings change or
                // a switch of the active scene, never on a timer and never because a camera came or went (a respawn or a
                // kill cam). Nothing in it may search all objects: in a match one such search took 5 ms, and a full pass
                // at every round start cost 11 ms (measured).
                bool full = Dirty; Dirty = false; next = t + 0.5f;
                try { Housekeeping(full); } catch (Exception e) { TweaksPlugin.L.LogWarning("Housekeeping: " + e.Message); next = t + 5f; }
            }
        }
        void LateUpdate()
        {
            Patches.FlushEnabled();
        }

        void Housekeeping(bool full)
        {
            // a full pass also lets go of the parts of destroyed cameras: without that, destroyed objects of earlier
            // takes stay held by this list
            if (full) { MouseEvents(); JobWorkers(); GcSteps(); Fullbright.Refresh(); camPaths.RemoveAll(kv => !kv.Key); }
            // ---- forward rendering: comes with DorsthTweaks' No Post-Processing
            if (TweaksPlugin.Forward) { foreach (var c in Camera.allCameras) if (WorldCamera(c) && c.actualRenderingPath == RenderingPath.DeferredShading) { camPaths.Add(new KeyValuePair<Camera, RenderingPath>(c, c.renderingPath)); c.renderingPath = RenderingPath.Forward; } }
            else if (camPaths.Count > 0) { foreach (var kv in camPaths) if (kv.Key) kv.Key.renderingPath = kv.Value; camPaths.Clear(); }
            // the depth picture for the cameras moved to the forward path (see ForwardRenderingKeepsDepth)
            if (camPaths.Count > 32) camPaths.RemoveAll(kv => kv.Key == null);   // cameras of players long gone (one more at every spawn)
            Depth();
            Doll(full);
            OwnTag();
        }

        // ---- Safe: the garbage collector's allowance per frame
        static ulong gcOrig; static bool gcSet;
        void GcSteps()
        {
            if (TweaksPlugin.GcSteps && UnityEngine.Scripting.GarbageCollector.isIncremental)
            {
                if (!gcSet) { gcOrig = UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds; gcSet = true; TweaksPlugin.L.LogInfo("GcSmallSteps: the game allows the garbage collector " + (gcOrig / 1e6).ToString("0.##") + " ms per frame; now " + (TweaksPlugin.GcStepNs / 1e6).ToString("0.##") + " ms"); }
                if (UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds != TweaksPlugin.GcStepNs) UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds = TweaksPlugin.GcStepNs;
            }
            else if (gcSet) { UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds = gcOrig; gcSet = false; }
        }

        // ---- Experimental: job worker threads
        static int jobOrig = -1;
        void JobWorkers()
        {
            if (TweaksPlugin.JobLimit)
            {
                int max = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerMaximumCount, n = Mathf.Clamp(TweaksPlugin.JobCount, 0, max), cur = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount;
                if (jobOrig < 0) { jobOrig = cur; TweaksPlugin.L.LogInfo("JobWorkerLimit: the game runs " + cur + " job worker threads (maximum " + max + ")"); }
                if (cur != n) { Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount = n; TweaksPlugin.L.LogInfo("JobWorkerLimit: now " + n); }
            }
            else if (jobOrig >= 0) { Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount = jobOrig; TweaksPlugin.L.LogInfo("JobWorkerLimit: back to " + jobOrig); jobOrig = -1; }
        }

        // ---- Safe: own name-tag canvas
        Canvas ownTag; FirstPersonController ownTagOwner;
        void OwnTag()
        {
            var me = Settings.Instance != null ? Settings.Instance.localPlayer : null;
            if (!TweaksPlugin.OwnTagCanvasOff || me == null || me != ownTagOwner)
            {
                if (ownTag) ownTag.enabled = true;
                ownTag = null; ownTagOwner = null;
                if (!TweaksPlugin.OwnTagCanvasOff || me == null) return;
            }
            if (ownTagOwner == me) return;
            ownTagOwner = me;
            var vi = me.transform.root.GetComponentInChildren<VisualInfo>(true);
            if (vi == null || !vi.IsOwner) return;
            var c = vi.GetComponentInParent<Canvas>();
            if (c != null && c.enabled && c.transform.IsChildOf(me.transform.root)) { c.enabled = false; ownTag = c; }
        }

        // ---- Safe: lobby preview character
        void Doll(bool full)
        {
            if (dollRoot == null && full && searchDoll) { searchDoll = false; var host = GameObject.Find("HostGameCamera"); if (host != null) { var p = host.transform.Find("Preview"); dollRoot = p != null ? p.gameObject : null; dollCams = dollRoot != null ? host.transform.root.GetComponentsInChildren<Camera>(true) : null; } }
            if (dollRoot == null) return;
            bool drawn = false;
            if (TweaksPlugin.InMenu || dollCams == null) drawn = true;
            else foreach (var c in dollCams) if (c && c.isActiveAndEnabled) { drawn = true; break; }
            bool wantIdle = TweaksPlugin.PreviewDollIdle && !drawn;
            if (wantIdle && !dollIdle)
            {
                foreach (var a in dollRoot.GetComponentsInChildren<Animator>(true)) if (a.enabled) { a.enabled = false; dollAnim.Add(a); }
                foreach (var s in dollRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (s.updateWhenOffscreen) { s.updateWhenOffscreen = false; dollSkin.Add(s); }
                dollIdle = true;
            }
            else if (!wantIdle && dollIdle)
            {
                foreach (var a in dollAnim) if (a) a.enabled = true; dollAnim.Clear();
                foreach (var s in dollSkin) if (s) s.updateWhenOffscreen = true; dollSkin.Clear();
                dollIdle = false;
            }
        }

        // ---- Safe: legacy mouse events
        void MouseEvents()
        {
            bool want = TweaksPlugin.MouseEventsOff && !LiveMouseHandlers();
            if (want == mouseRemoved) return;
            var loop = PlayerLoop.GetCurrentPlayerLoop(); var phases = (PlayerLoopSystem[])loop.subSystemList.Clone();
            if (want)
            {
                for (int i = 0; i < phases.Length; i++)
                {
                    var subs = phases[i].subSystemList; if (subs == null) continue;
                    int idx = Array.FindIndex(subs, s => s.type != null && s.type.Name == "SendMouseEvents"); if (idx < 0) continue;
                    mouseSys = subs[idx]; mousePhase = phases[i].type; mouseIndex = idx;
                    var l = subs.ToList(); l.RemoveAt(idx); phases[i].subSystemList = l.ToArray(); mouseRemoved = true; break;
                }
            }
            else
            {
                for (int i = 0; i < phases.Length; i++)
                {
                    if (phases[i].type != mousePhase || phases[i].subSystemList == null) continue;
                    var l = phases[i].subSystemList.ToList(); if (l.Any(s => s.type == mouseSys.type)) { mouseRemoved = false; return; }
                    l.Insert(Math.Min(mouseIndex, l.Count), mouseSys); phases[i].subSystemList = l.ToArray(); mouseRemoved = false; break;
                }
            }
            loop.subSystemList = phases; PlayerLoop.SetPlayerLoop(loop);
        }

        // Does any loaded scene hold a script with an OnMouse... handler? (Then Unity's mouse events must stay.) Each scene is looked
        // through once, when it loads, and only its own objects: asking Unity for "any object of this type" walks every loaded object
        // and took about 5 ms per type in a match.
        static readonly Dictionary<int, bool> sceneHasHandlers = new Dictionary<int, bool>(); static bool searchDoll = true, ownSceneChecked;
        static Type[] MouseTypes()
        {
            if (mouseHandlerTypes == null)
            {
                var found = new List<Type>(); string[] names = { "OnMouseDown", "OnMouseUp", "OnMouseEnter", "OnMouseExit", "OnMouseOver", "OnMouseDrag", "OnMouseUpAsButton" };
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string n = a.GetName().Name;
                    if (n.StartsWith("System") || n == "mscorlib" || n == "netstandard" || n.StartsWith("UnityEngine") || n.StartsWith("Mono") || n.StartsWith("BepInEx") || n == "0Harmony" || (n.StartsWith("Unity.") && !n.StartsWith("Unity.VisualScripting"))) continue;
                    Type[] types; try { types = a.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); } catch { continue; }
                    foreach (var t in types) { if (t.IsAbstract || !typeof(MonoBehaviour).IsAssignableFrom(t)) continue; foreach (var m in names) if (t.GetMethod(m, bf, null, Type.EmptyTypes, null) != null) { found.Add(t); break; } }
                }
                mouseHandlerTypes = found.ToArray();
            }
            return mouseHandlerTypes;
        }
        static bool HasHandlers(Scene sc)
        {
            var types = MouseTypes(); if (types.Length == 0 || !sc.IsValid() || !sc.isLoaded) return false;
            foreach (var root in sc.GetRootGameObjects()) foreach (var t in types) { try { if (root.GetComponentInChildren(t, true) != null) return true; } catch { } }
            return false;
        }
        public static void SceneLoaded(Scene sc) { searchDoll = true; try { sceneHasHandlers[sc.handle] = HasHandlers(sc); } catch { } DeadInput(); DeadNet(); deadAgainAt = Time.unscaledTime + 4f; }
        static float deadAgainAt;
        static int deadNetLogged, deadNetRemoved; static MethodInfo setFirst; static readonly Predicate<FishNet.Object.NetworkObject> isGone = o => o == null;
        static void PruneOwned(FishNet.Connection.NetworkConnection c)
        {
            if (c == null || c.Objects == null || c.Objects.Count == 0) return;
            int n = c.Objects.RemoveWhere(isGone); if (n == 0) return;
            deadNetRemoved += n;
            // the library also remembers the first object a player owns: if that one is gone, have the library pick again
            var first = c.FirstObject;
            if (!ReferenceEquals(first, null) && first == null)
            {
                if (setFirst == null) setFirst = AccessTools.Method(typeof(FishNet.Connection.NetworkConnection), "SetFirstObject", Type.EmptyTypes);
                if (setFirst != null && setFirst.GetParameters().Length == 0) setFirst.Invoke(c, null);
            }
        }
        static FieldInfo pendingField; static readonly List<int> goneIds = new List<int>();
        static readonly Dictionary<Type, FieldInfo[]> eventsOf = new Dictionary<Type, FieldInfo[]>();
        // the library's table of spawned objects by id: entries whose object was destroyed with its scene (no despawn ever came for them)
        // every list, set and id table of networked objects an object manager of the library keeps (spawned, spawned locally, pending ...):
        // destroyed objects are taken out. Tables keyed by scene id are left alone.
        static readonly Dictionary<Type, FieldInfo[]> listsOf = new Dictionary<Type, FieldInfo[]>();
        static void PruneLists(object objects)
        {
            if (objects == null) return; Type t = objects.GetType(); FieldInfo[] fields;
            if (!listsOf.TryGetValue(t, out fields))
            {
                var l = new List<FieldInfo>();
                for (Type b = t; b != null && b != typeof(object); b = b.BaseType)
                    foreach (FieldInfo f in b.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (f.FieldType == typeof(List<FishNet.Object.NetworkObject>) || f.FieldType == typeof(HashSet<FishNet.Object.NetworkObject>) || f.FieldType == typeof(Dictionary<int, FishNet.Object.NetworkObject>)) l.Add(f);
                listsOf[t] = fields = l.ToArray();
            }
            foreach (FieldInfo f in fields)
            {
                object v = f.GetValue(objects); if (v == null) continue;
                var list = v as List<FishNet.Object.NetworkObject>; if (list != null) { if (list.Count > 0) deadNetRemoved += list.RemoveAll(isGone); continue; }
                var set = v as HashSet<FishNet.Object.NetworkObject>; if (set != null) { if (set.Count > 0) deadNetRemoved += set.RemoveWhere(isGone); continue; }
                var table = v as Dictionary<int, FishNet.Object.NetworkObject>; if (table == null || table.Count == 0) continue;
                goneIds.Clear(); foreach (var kv in table) if (kv.Value == null) goneIds.Add(kv.Key);
                foreach (int id in goneIds) table.Remove(id);
                deadNetRemoved += goneIds.Count; goneIds.Clear();
            }
        }
        static bool OwnerGone(Delegate d)
        {
            object target = d.Target; if (target == null) return false;
            var u = target as UnityEngine.Object; if (!ReferenceEquals(u, null)) return u == null;
            // a lambda that uses its script keeps it in a small helper object, in a field named <>4__this
            Type tt = target.GetType(); if (tt.Name.IndexOf("DisplayClass", StringComparison.Ordinal) < 0) return false;
            FieldInfo self = tt.GetField("<>4__this", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); if (self == null) return false;
            var owner = self.GetValue(target) as UnityEngine.Object; return !ReferenceEquals(owner, null) && owner == null;
        }
        // events of one of the library's managers: subscribers whose script is destroyed are taken off (they were still called, and kept in memory)
        static void PruneEvents(object manager)
        {
            if (manager == null) return; Type t = manager.GetType(); FieldInfo[] fields;
            if (!eventsOf.TryGetValue(t, out fields))
            {
                var l = new List<FieldInfo>();
                for (Type b = t; b != null && b != typeof(MonoBehaviour) && b != typeof(object); b = b.BaseType)
                    foreach (FieldInfo f in b.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) if (typeof(Delegate).IsAssignableFrom(f.FieldType)) l.Add(f);
                eventsOf[t] = fields = l.ToArray();
            }
            foreach (FieldInfo f in fields)
            {
                var d = f.GetValue(manager) as Delegate; if (d == null) continue;
                Delegate kept = d; int n = 0;
                foreach (Delegate one in d.GetInvocationList()) if (OwnerGone(one)) { kept = Delegate.Remove(kept, one); n++; }
                if (n > 0) { f.SetValue(manager, kept); deadNetRemoved += n; }
            }
        }
        static void DeadNet()
        {
            if (!TweaksPlugin.DeadNetOff) return;
            try
            {
                var nm = FishNet.InstanceFinder.NetworkManager; if (nm == null) return;
                deadNetRemoved = 0;
                if (nm.ClientManager != null) { PruneOwned(nm.ClientManager.Connection); foreach (var kv in nm.ClientManager.Clients) PruneOwned(kv.Value); }
                if (nm.ServerManager != null) foreach (var kv in nm.ServerManager.Clients) PruneOwned(kv.Value);
                // As host: the library parks despawned objects in a "pending destroy" table until the host's own client side has handled the
                // despawn. Objects that went with the scene never get that far and stay in the table, destroyed, with everything they point at.
                if (nm.ServerManager != null && nm.ServerManager.Objects != null)
                {
                    if (pendingField == null) pendingField = AccessTools.Field(nm.ServerManager.Objects.GetType(), "_pendingDestroy");
                    var pending = pendingField != null ? pendingField.GetValue(nm.ServerManager.Objects) as Dictionary<int, FishNet.Object.NetworkObject> : null;
                    if (pending != null && pending.Count > 0)
                    {
                        goneIds.Clear(); foreach (var kv in pending) if (kv.Value == null) goneIds.Add(kv.Key);
                        foreach (int id in goneIds) pending.Remove(id);
                        deadNetRemoved += goneIds.Count; goneIds.Clear();
                    }
                }
                if (nm.ClientManager != null) PruneLists(nm.ClientManager.Objects);
                if (nm.ServerManager != null) PruneLists(nm.ServerManager.Objects);
                PruneEvents(nm.SceneManager); PruneEvents(nm.TimeManager); PruneEvents(nm.ClientManager); PruneEvents(nm.ServerManager);
                var gm = GameManager.Instance;
                if (gm != null) { foreach (var c in gm.ConnectionsToStartGame) PruneOwned(c); foreach (var c in gm.NetworkConnectionsReady) PruneOwned(c); }
                if (deadNetRemoved > 0 && deadNetLogged < 3) { deadNetLogged++; TweaksPlugin.L.LogInfo("DeadNetworkObjects: " + deadNetRemoved + " destroyed objects and dead subscribers taken off the network library's lists (owned, spawned, pending destroy, events)" + (deadNetLogged == 3 ? " (not logged again)" : "")); }
            }
            catch (Exception e) { if (deadNetLogged < 3) { deadNetLogged = 3; TweaksPlugin.L.LogWarning("DeadNetworkObjects: could not read the lists (" + e.Message + "); nothing changed"); } }
        }
        static int deadInputLogged;
        static void DeadInput()
        {
            if (!TweaksPlugin.DeadInputOff) return;
            try
            {
                int total, gone; InputCallbacks.Scan(true, out total, out gone);
                if (gone > 0 && deadInputLogged < 3) { deadInputLogged++; TweaksPlugin.L.LogInfo("DeadInputCallbacks: " + gone + " of " + total + " input callbacks belonged to destroyed objects and were taken off" + (deadInputLogged == 3 ? " (not logged again)" : "")); }
            }
            catch (Exception e) { if (deadInputLogged < 3) { deadInputLogged = 3; TweaksPlugin.L.LogWarning("DeadInputCallbacks: could not read the input callbacks (" + e.Message + "); nothing changed"); } }
        }
        public static void SceneUnloaded(Scene sc) { sceneHasHandlers.Remove(sc.handle); }
        bool LiveMouseHandlers()
        {
            // the scene this object lives in (objects that survive scene changes) is not announced as "loaded": look through it once
            if (!ownSceneChecked) { ownSceneChecked = true; try { sceneHasHandlers[gameObject.scene.handle] = HasHandlers(gameObject.scene); for (int i = 0; i < SceneManager.sceneCount; i++) { var sc = SceneManager.GetSceneAt(i); if (!sceneHasHandlers.ContainsKey(sc.handle)) sceneHasHandlers[sc.handle] = HasHandlers(sc); } } catch { } }
            foreach (var kv in sceneHasHandlers) if (kv.Value) return true;
            return false;
        }
    }

    internal static class Patches
    {
        static Behaviour pendB; static bool pendV, pend;
        static float lobbyNext, steamCheckNext;

        public static void Apply(Harmony h)
        {
            var self = typeof(Patches); const BindingFlags bf = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            Func<string, HarmonyMethod> M = n => new HarmonyMethod(self.GetMethod(n, bf));
            // ezCrosshair up to 1.2.0 hides the game's crosshair by switching its image off after Crosshair.Update. Applying
            // "the game's last write" would then switch it back on, so this fix stays out of the way until ezCrosshair is 1.2.1+.
            BepInEx.PluginInfo ez; bool oldEz = false;
            try { oldEz = BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("dorsth.straftat.ezcrosshair", out ez) && ez.Metadata.Version < new Version(1, 2, 1); } catch { }
            if (oldEz) TweaksPlugin.L.LogWarning("CrosshairFix is off: ezCrosshair older than 1.2.1 is installed. Update ezCrosshair to 1.2.1 (it no longer flips the game's crosshair every frame).");
            else
            {
                Try("CrosshairFix (Crosshair)", () => h.Patch(AccessTools.Method(typeof(Crosshair), "Update"), transpiler: M("EnabledTr")));
                Try("CrosshairFix (PauseManager)", () => h.Patch(AccessTools.Method(typeof(PauseManager), "Update"), transpiler: M("EnabledTr")));
            }
            Try("LobbyListThrottle", () => h.Patch(AccessTools.Method(typeof(LobbyController), "Update"), prefix: M("LobbyPre")));
            Try("SteamRunningCheckThrottle", () => { var t = Type.GetType("HeathenEngineering.SteamworksIntegration.API.Api, Heathen.Steamworks", true); h.Patch(AccessTools.Method(t, "LateUpdate"), prefix: M("SteamCheckPre")); });
            Try("TrickShotLineNoGarbage", () =>
            {
                tsShot = AccessTools.FieldRefAccess<HeathenEngineering.PhysKit.TrickShotLine, HeathenEngineering.PhysKit.TrickShot>("trickShot");
                tsLine = AccessTools.FieldRefAccess<HeathenEngineering.PhysKit.TrickShotLine, LineRenderer>("lineRenderer");
                h.Patch(AccessTools.Method(typeof(HeathenEngineering.PhysKit.TrickShotLine), "LateUpdate"), prefix: M("TrickLinePre"));
            });
            Try("GunSoundsNoScanInPlay", () =>
            {
                var reg = AccessTools.TypeByName("GunSounds.Registry"); if (reg == null) return;   // GunSounds is not installed: nothing to do
                var scan = AccessTools.Method(reg, "Scan", Type.EmptyTypes); gsLast = AccessTools.Field(reg, "lastScan");
                if (scan == null || gsLast == null || gsLast.FieldType != typeof(float)) throw new Exception("GunSounds is not the version this was written for (2.2.0)");
                gsScan = (Action)Delegate.CreateDelegate(typeof(Action), scan);
                h.Patch(scan, prefix: M("GunScanPre"));
                PauseManager.OnRoundStarted += GunScanAtCountdown;
                TweaksPlugin.L.LogInfo("GunSoundsNoScanInPlay: GunSounds found, its weapon search is kept out of play time");
            });
            Try("DecalSmallBuffers", () =>
            {
                var dm = AccessTools.TypeByName("ch.sycoforge.Decal.Projectors.Geometry.DynamicMesh"); if (dm == null) throw new Exception("the decal library was not found");
                decalHits = 0;
                foreach (var c in AccessTools.GetDeclaredConstructors(dm)) if (!c.IsStatic) h.Patch(c, transpiler: M("DecalCapTr"));
                if (decalHits == 0) throw new Exception("no list of 4048 found in the decal library (changed?)");
                TweaksPlugin.L.LogInfo("DecalSmallBuffers: " + decalHits + " lists in the decal library start small");
            });
            Try("ItemNameNoGarbage", () => { nameHits = 0; h.Patch(AccessTools.Method(typeof(ItemBehaviour), "Update"), transpiler: M("ItemNameTr")); if (nameHits != 1) throw new Exception(nameHits + " name reads found in ItemBehaviour.Update, expected 1"); });
            sceneHits = 0;
            foreach (var tm in new[] { new KeyValuePair<Type, string>(typeof(PauseManager), "Update"), new KeyValuePair<Type, string>(typeof(SetMenuMusicVolume), "UpdateMusic"), new KeyValuePair<Type, string>(typeof(Settings), "Update"),
                                       new KeyValuePair<Type, string>(typeof(Crosshair), "Update"), new KeyValuePair<Type, string>(typeof(SceneLoader), "Update"), new KeyValuePair<Type, string>(typeof(TabScreen), "Update") })
            { var x = tm; Try("SceneNameNoGarbage (" + x.Key.Name + ")", () => h.Patch(AccessTools.Method(x.Key, x.Value), transpiler: M("SceneNameTr"))); }
            TweaksPlugin.L.LogInfo("SceneNameNoGarbage: " + sceneHits + " reads of the map name are remembered (14 expected)");
            SceneManager.sceneLoaded += (sc, m) => { snHandle = int.MinValue; }; SceneManager.sceneUnloaded += sc => { snHandle = int.MinValue; itemNames.Clear(); }; SceneManager.activeSceneChanged += (x, y) => { snHandle = int.MinValue; };
            textHits = 0;
            foreach (var tm in new[] { new KeyValuePair<Type, string>(typeof(PlayerHealth), "Update"), new KeyValuePair<Type, string>(typeof(ProgressManager), "HandleUIUpdate"), new KeyValuePair<Type, string>(typeof(Weapon), "WeaponUpdate"),
                                       new KeyValuePair<Type, string>(typeof(PauseManager), "ChangeAmmoText") })
            { var x = tm; Try("HudNumbersNoGarbage (" + x.Key.Name + ")", () => h.Patch(AccessTools.Method(x.Key, x.Value), transpiler: M("HudTextTr"))); }
            TweaksPlugin.L.LogInfo("HudNumbersNoGarbage: " + textHits + " places where a number is turned into text use a remembered text (14 expected)");
            Try("LobbyRowsNotInPlay", () =>
            {
                lobbyTab = AccessTools.FieldRefAccess<LobbyController, Transform>("tabScreen");
                h.Patch(AccessTools.Method(typeof(LobbyController), "CreateNewObjects"), prefix: M("LobbyRowPre"));
            });
            Try("LobbyLeaveFix", () => h.Patch(AccessTools.Method(typeof(ClientInstance), "OnDisable"), finalizer: M("LeaveFin")));
            Try("LobbyStaleIds", () => h.Patch(AccessTools.Method(typeof(ClientInstance), "RpcLogic___AddNewPlayer_2166193949"), prefix: M("AddNewPre")));
            Try("LobbyStaleRows", () =>
            {
                rowsById = AccessTools.FieldRefAccess<LobbyController, Dictionary<int, PlayerListItems>>("PlayerIdToListItem");
                h.Patch(AccessTools.Method(typeof(LobbyController), "UpdatePlayerList"), prefix: M("StaleRowsPre"));
            });
            Try("FovTweensNoPileUp", () =>
            {
                var m = AccessTools.Method(typeof(DG.Tweening.ShortcutExtensions), "DOFieldOfView", new[] { typeof(Camera), typeof(float), typeof(float) });
                if (m == null) throw new Exception("DOFieldOfView not found");
                h.Patch(m, postfix: M("FovTweenPost"));
            });
            {
                int hz = 0; try { hz = Screen.currentResolution.refreshRate; } catch { }
                if (hz < 120) hz = 120;
                arcStep = 1f / hz;
                TweaksPlugin.L.LogInfo("OtherArcsAtScreenRate: other players' launcher arcs are worked out " + hz + " times a second");
            }
            SceneManager.sceneUnloaded += sc => { arcWeapon.Clear(); arcNext.Clear(); fovTweens.Clear(); };
            Try("KillCamLean", () => h.Patch(AccessTools.Method(typeof(KillCam), "Update"), transpiler: M("KillCamTr")));
            Try("KillCamNoGarbage", () =>
            {
                kcHits = 0;
                h.Patch(AccessTools.Method(typeof(KillCam), "Update"), transpiler: M("KillCamTextTr"));
                if (kcHits != 10) TweaksPlugin.L.LogWarning("KillCamNoGarbage: " + kcHits + " of 10 places found in KillCam.Update (game update?); the ones found are handled");
                else TweaksPlugin.L.LogInfo("KillCamNoGarbage: 10 places in the kill camera no longer make garbage every frame");
            });
            Try("FadedLinesEndEarly", () =>
            {
                fadeHits = 0;
                foreach (var t in new[] { typeof(ShrapnelBallistic), typeof(PhysicsGrenade) })
                {
                    var m = AccessTools.Method(t, "RenderObject", new[] { typeof(Vector3), typeof(float) });
                    if (m == null) throw new Exception(t.Name + ".RenderObject not found");
                    h.Patch(m, transpiler: M("LineLifeTr"));
                }
                if (fadeHits != 2) TweaksPlugin.L.LogWarning("FadedLinesEndEarly: " + fadeHits + " of 2 places found (game update?); the others keep the game's 2.5 seconds");
                else TweaksPlugin.L.LogInfo("FadedLinesEndEarly: the fragment lines of 2 explosion scripts are removed when they have faded out");
            });
            Try("ExplosionLinesOneMaterial", () =>
            {
                lineHits = 0;
                foreach (var t in new[] { typeof(ShrapnelBallistic), typeof(PhysicsGrenade), typeof(Claymore) })
                {
                    var m = AccessTools.Method(t, "RenderObject", new[] { typeof(Vector3), typeof(float) });
                    if (m == null) throw new Exception(t.Name + ".RenderObject not found");
                    h.Patch(m, transpiler: M("LineMatTr"));
                }
                if (lineHits != 3) throw new Exception(lineHits + " of 3 material creations found");
                LineMat();   // made now, not in the first explosion
                TweaksPlugin.L.LogInfo("ExplosionLinesOneMaterial: the fragment lines of 3 explosion scripts share one material");
            });
            Try("ArcPathNoGarbage", () =>
            {
                var t = AccessTools.TypeByName("HeathenEngineering.PhysKit.API.Ballistics"); if (t == null) throw new Exception("Ballistics not found");
                arcHits = 0; int methods = 0;
                foreach (var m in AccessTools.GetDeclaredMethods(t))
                {
                    if (m.Name != "SphereCast") continue;
                    bool hasPath = false; foreach (var pa in m.GetParameters()) if (pa.ParameterType == typeof(List<(Vector3, Vector3, float)>).MakeByRefType()) hasPath = true;
                    if (!hasPath) continue;
                    h.Patch(m, transpiler: M("ArcPathTr")); methods++;
                }
                if (arcHits != methods || methods == 0) throw new Exception(arcHits + " list creations found in " + methods + " path methods (library changed?)");
                TweaksPlugin.L.LogInfo("ArcPathNoGarbage: " + arcHits + " path builders of the arc library use one reused point list");
            });
            Try("GunSoundsNoScanInPlay (countdown)", () => h.Patch(AccessTools.Method(typeof(PlayerManager), "WaitForRoundStartCoroutineStart"), postfix: M("CountdownPost")));
            Try("WeaponLayerNoGarbage", () =>
            {
                var m = AccessTools.Method(typeof(Weapon), "SetLayerAllChildren", new[] { typeof(Transform), typeof(int) });
                if (m == null) throw new Exception("Weapon.SetLayerAllChildren not found");
                h.Patch(m, prefix: M("WeaponLayerPre"));
            });
            Try("OnGuiNoLayout", () =>
            {
                h.Patch(AccessTools.Method(typeof(FirstPersonController), "Awake"), postfix: M("FpcGuiPost"));
                SceneManager.sceneLoaded += (sc, mode) => GuiLayoutRefresh();
                TweaksPlugin.L.LogInfo("OnGuiNoLayout: no GUI layout pass for the player script, the ping script and the FPS counter");
            });
            Try("WeaponNoInputCopy", () => h.Patch(AccessTools.Method(typeof(Weapon), "Awake___UserLogic"), transpiler: M("WeaponControlsTr")));
            Try("VoiceKeyOffThread", () =>
            {
                var su = Type.GetType("Steamworks.SteamUser, com.rlabrecque.steamworks.net", true);
                var start = AccessTools.Method(su, "StartVoiceRecording"); var stop = AccessTools.Method(su, "StopVoiceRecording");
                voiceStart = (Action)Delegate.CreateDelegate(typeof(Action), start); voiceStop = (Action)Delegate.CreateDelegate(typeof(Action), stop);
                h.Patch(start, prefix: M("VoiceStartPre")); h.Patch(stop, prefix: M("VoiceStopPre"));
            });
        }
        // ---- GunSoundsNoScanInPlay. GunSounds 2.2.0 (1ntrospective) puts a prefix on both AudioSource.PlayOneShot overloads:
        //        Registry.Swap(source, clip): the source sits under a Weapon -> that weapon's entry, done.
        //        otherwise: clip known as a gun sound -> swap; known "not one" less than 5 s ago -> leave;
        //                   else, if the last search is more than 2 s ago: lastScan = now; Scan(); look again; remember "not one" for 5 s.
        //        Scan() = Resources.FindObjectsOfTypeAll<Weapon>() and an entry for each (the engine walks every loaded object).
        // Footsteps, jump, vault and ladder sounds come from the player's own AudioSource, so each of those clips sends GunSounds
        // searching again 5 s after its last answer: one search about every 5 s for as long as anyone moves. In the profile of
        // that search was 1.76 ms on average in the 21 frames of 4 ms or more while the player could move
        // (0.6 us in a normal one), reached through HandleFootsteps, JumpClipObservers, VaultClipObservers, HandleLadderSounds
        // and PlaySoundObservers (other players' steps).
        // Here: a search that GunSounds starts from a sound is skipped while the local player can move. It is told apart from
        // GunSounds' own search at a map load by the clock GunSounds sets right before it. What a search can add is a weapon whose
        // prefab came into memory since the last one; that is caught at the map load, at the countdown of every take (below)
        // and by GunSounds itself as soon as the player cannot move. A weapon's own shots never needed the search.
        static System.Reflection.FieldInfo gsLast; static Action gsScan; static bool gsOwn;
        // ---- DecalSmallBuffers. ch.sycoforge.Decal (Easy Decal): DynamicMesh has six lists made with room for 4048 entries
        // (uv, uv2: 8 bytes each; vertices, normals: 12; tangents, colours: 16 = 72 bytes x 4048 = 291,456 bytes). Every projector
        // makes one DynamicMesh, and an EasyDecal that was just created makes its projector twice: in Awake (Reset with
        // recreate, because its id differs from the prefab's) and again in OnEnable. So one decal = 582,912 bytes, and the first
        // half is garbage at once. Only the starting size changes here; a List grows by itself when it needs more.
        internal static int decalHits;
        public static int DecalCap() { return TweaksPlugin.DecalSmall ? 256 : 4048; }
        // ---- FadedLinesEndEarly. ShrapnelBallistic / PhysicsGrenade .RenderObject end with Object.Destroy(obj, 2.5f), while the
        // line's LineFade multiplies its alpha by (1 - deltaTime * speed) every frame: at most e^(-speed * t) is left after t
        // seconds (less at a low frame rate). With the game's speed of 9 that is 0.15% after 0.72 s; the line is drawn with
        // Sprites/Default (colour times alpha), so nothing of it reaches the screen after that. The 2.5 becomes
        // min(2.5, 6.5 / speed), read from the line's own LineFade (one GetComponent per line made). Claymore.RenderObject has no LineFade and is left alone.
        internal static int fadeHits;
        static float LineLife(float game, LineRenderer line)
        {
            if (!TweaksPlugin.FadedLines || line == null) return game;
            LineFade fade = line.GetComponent<LineFade>();
            if (fade == null) return game;
            float s = fade.speed;
            if (!(s > 0.5f)) return game;
            float t = 6.5f / s;
            return t < game ? t : game;
        }
        static IEnumerable<CodeInstruction> LineLifeTr(IEnumerable<CodeInstruction> ins)
        {
            var list = new List<CodeInstruction>(ins);
            var life = AccessTools.Method(typeof(Patches), "LineLife");
            var destroy = AccessTools.Method(typeof(UnityEngine.Object), "Destroy", new[] { typeof(UnityEngine.Object), typeof(float) });
            // the new LineFade is never put in a local (the compiler keeps it on the stack while it fills it in); the line's
            // LineRenderer is: the stloc right after AddComponent<LineRenderer>(). The fade is asked from it (once per line).
            CodeInstruction load = null;
            for (int i = 0; i + 1 < list.Count; i++)
            {
                var mi = list[i].operand as MethodInfo;
                if (mi == null || mi.Name != "AddComponent" || !mi.IsGenericMethod || mi.GetGenericArguments()[0] != typeof(LineRenderer)) continue;
                var st = list[i + 1];
                if (st.opcode == OpCodes.Stloc_0) load = new CodeInstruction(OpCodes.Ldloc_0);
                else if (st.opcode == OpCodes.Stloc_1) load = new CodeInstruction(OpCodes.Ldloc_1);
                else if (st.opcode == OpCodes.Stloc_2) load = new CodeInstruction(OpCodes.Ldloc_2);
                else if (st.opcode == OpCodes.Stloc_3) load = new CodeInstruction(OpCodes.Ldloc_3);
                else if (st.opcode == OpCodes.Stloc_S || st.opcode == OpCodes.Stloc) load = new CodeInstruction(st.opcode == OpCodes.Stloc_S ? OpCodes.Ldloc_S : OpCodes.Ldloc, st.operand);
                break;
            }
            if (load == null) return list;
            for (int i = 0; i + 1 < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldc_R4 && list[i + 1].Calls(destroy) && list[i + 1].labels.Count == 0)
                {
                    list.Insert(i + 1, load); list.Insert(i + 2, new CodeInstruction(OpCodes.Call, life));
                    fadeHits++; break;
                }
            }
            return list;
        }

        // ---- ExplosionLinesOneMaterial. ShrapnelBallistic / PhysicsGrenade / Claymore .RenderObject, once per fragment ray:
        //        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        // The material is never changed afterwards (LineFade writes lineRenderer.startColor / endColor) and never destroyed.
        // Assigning a material does not copy it, so one material serves every line.
        internal static int lineHits; static Material lineMat;
        static Material LineMat()
        {
            if (!TweaksPlugin.LineMatOne) return new Material(Shader.Find("Sprites/Default"));
            if (lineMat == null) { lineMat = new Material(Shader.Find("Sprites/Default")); lineMat.name = "UltimateTweaks shared line material"; lineMat.hideFlags = HideFlags.HideAndDontSave; }
            return lineMat;
        }
        static IEnumerable<CodeInstruction> LineMatTr(IEnumerable<CodeInstruction> ins)
        {
            var list = new List<CodeInstruction>(ins);
            var get = AccessTools.Method(typeof(Patches), "LineMat"); var find = AccessTools.Method(typeof(Shader), "Find", new[] { typeof(string) });
            var ctor = AccessTools.Constructor(typeof(Material), new[] { typeof(Shader) });
            for (int i = 0; i + 2 < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldstr && (list[i].operand as string) == "Sprites/Default" && list[i + 1].Calls(find)
                    && list[i + 2].opcode == OpCodes.Newobj && (list[i + 2].operand as ConstructorInfo) == ctor
                    && list[i + 1].labels.Count == 0 && list[i + 2].labels.Count == 0)
                {
                    var n = new CodeInstruction(OpCodes.Call, get); n.labels.AddRange(list[i].labels); n.blocks.AddRange(list[i].blocks);
                    list[i] = n; list.RemoveRange(i + 1, 2); lineHits++;
                }
            }
            return list;
        }

        // ---- Do not reuse the arrays of a launcher's arc (ArcStepsReuse, tried and removed): fired shells stayed where
        // they were made and BallisticPathFollow.Lerp threw 20,454 times in one match (a path without points). The
        // saving was about 3.5 KB a frame with a launcher in hand.
        // ---- ArcPathNoGarbage. Ballistics.SphereCast(..., out List<(Vector3, Vector3, float)> path, ...) starts with
        // path = new List<...> and adds one entry per step of the flight (100 and more for a launcher: the list regrows
        // 4, 8, 16 ... 256). Its three callers (BallisticsData.Predict x2, BallisticPath.Get) copy the entries into a
        // new Step[] and drop the list at once, so one list can serve every call. Main thread only (it calls
        // Physics.SphereCast), one call at a time.
        internal static int arcHits;
        static readonly List<(Vector3, Vector3, float)> arcPathBuf = new List<(Vector3, Vector3, float)>(512);
        static List<(Vector3, Vector3, float)> ArcPathList()
        {
            if (!TweaksPlugin.ArcPath) return new List<(Vector3, Vector3, float)>();
            arcPathBuf.Clear(); return arcPathBuf;
        }
        static IEnumerable<CodeInstruction> ArcPathTr(IEnumerable<CodeInstruction> ins)
        {
            var get = AccessTools.Method(typeof(Patches), "ArcPathList"); var ctor = typeof(List<(Vector3, Vector3, float)>).GetConstructor(Type.EmptyTypes);
            foreach (var ci in ins)
            {
                if (ci.opcode == OpCodes.Newobj && ci.operand is ConstructorInfo && (ConstructorInfo)ci.operand == ctor)
                { var n = new CodeInstruction(OpCodes.Call, get); n.labels.AddRange(ci.labels); n.blocks.AddRange(ci.blocks); arcHits++; yield return n; }
                else yield return ci;
            }
        }

        static IEnumerable<CodeInstruction> DecalCapTr(IEnumerable<CodeInstruction> ins)
        {
            var cap = AccessTools.Method(typeof(Patches), "DecalCap");
            foreach (var ci in ins)
            {
                if (ci.opcode == OpCodes.Ldc_I4 && ci.operand is int && (int)ci.operand == 4048)
                { var n = new CodeInstruction(OpCodes.Call, cap); n.labels.AddRange(ci.labels); n.blocks.AddRange(ci.blocks); decalHits++; yield return n; }
                else yield return ci;
            }
        }

        // ---- ItemNameNoGarbage. ItemBehaviour.Update starts with: if (gameObject.name == "Pig Held Item") return;
        // Object.name builds a new string in the engine on every call. One per item per frame.
        internal static int nameHits, sceneHits, textHits;
        internal static readonly Dictionary<int, string> itemNames = new Dictionary<int, string>();
        public static string ItemNameOf(UnityEngine.Object o)
        {
            if (!TweaksPlugin.ItemNameOn) return o.name;
            int id = o.GetInstanceID(); string n;
            if (itemNames.TryGetValue(id, out n)) return n;
            n = o.name; if (itemNames.Count > 4096) itemNames.Clear();
            itemNames[id] = n; return n;
        }
        static IEnumerable<CodeInstruction> ItemNameTr(IEnumerable<CodeInstruction> ins)
        {
            var get = AccessTools.PropertyGetter(typeof(UnityEngine.Object), "name"); var rep = AccessTools.Method(typeof(Patches), "ItemNameOf");
            foreach (var ci in ins)
            {
                if (ci.Calls(get)) { var n = new CodeInstruction(OpCodes.Call, rep); n.labels.AddRange(ci.labels); n.blocks.AddRange(ci.blocks); nameHits++; yield return n; }
                else yield return ci;
            }
        }

        // ---- SceneNameNoGarbage. SceneManager.GetActiveScene().name builds a new string in the engine on every call.
        internal static int snHandle = int.MinValue; static string snName;
        public static string SceneNameOf(ref Scene s)
        {
            if (!TweaksPlugin.SceneNameOn) return s.name;
            int hnd = s.handle;
            if (hnd == snHandle && snName != null) return snName;
            snName = s.name; snHandle = hnd; return snName;
        }
        static IEnumerable<CodeInstruction> SceneNameTr(IEnumerable<CodeInstruction> ins)
        {
            var get = AccessTools.PropertyGetter(typeof(Scene), "name"); var rep = AccessTools.Method(typeof(Patches), "SceneNameOf");
            foreach (var ci in ins)
            {
                if (ci.Calls(get)) { var n = new CodeInstruction(OpCodes.Call, rep); n.labels.AddRange(ci.labels); n.blocks.AddRange(ci.blocks); sceneHits++; yield return n; }
                else yield return ci;
            }
        }

        // ---- HudNumbersNoGarbage. "health : " + number, ping + " ms", fps + " fps", xp + "/" + max, the ammo counter: built every
        // frame. Whole numbers from 0 to 4096 get their text once; a text put together from remembered parts is remembered too.
        // Anything else (fractions, negative numbers, minus zero, other texts) goes to the game's own calls unchanged.
        sealed class RefCmp : IEqualityComparer<string>
        {
            public bool Equals(string a, string b) { return ReferenceEquals(a, b); }
            public int GetHashCode(string s) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(s); }
        }
        struct K3 { public string A, B, C; }
        sealed class K3Cmp : IEqualityComparer<K3>
        {
            public bool Equals(K3 x, K3 y) { return ReferenceEquals(x.A, y.A) && ReferenceEquals(x.B, y.B) && ReferenceEquals(x.C, y.C); }
            public int GetHashCode(K3 k)
            {
                int hc = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(k.A) * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(k.B);
                return k.C == null ? hc : hc * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(k.C);
            }
        }
        static readonly string[] wholeI = new string[4097], wholeF = new string[4097];
        static readonly HashSet<string> kept = new HashSet<string>(new RefCmp());
        static readonly Dictionary<K3, string> joined = new Dictionary<K3, string>(new K3Cmp());
        public static string FloatText(ref float v)
        {
            float f = v;
            if (TweaksPlugin.HudText && f >= 0f && f <= 4096f)
            {
                int i = (int)f;
                if (i == f && (i != 0 || 1f / f > 0f))
                { var t = wholeF[i]; if (t == null) { t = f.ToString(); wholeF[i] = t; kept.Add(t); } return t; }
            }
            return v.ToString();
        }
        public static string IntText(ref int v)
        {
            int i = v;
            if (TweaksPlugin.HudText && (uint)i <= 4096u) { var t = wholeI[i]; if (t == null) { t = i.ToString(); wholeI[i] = t; kept.Add(t); } return t; }
            if (TweaksPlugin.HudText) return BigText(i);
            return v.ToString();
        }
        public static string LongText(ref long v)
        {
            long l = v;
            if (TweaksPlugin.HudText && l >= 0 && l <= 4096) { var t = wholeI[(int)l]; if (t == null) { t = l.ToString(); wholeI[(int)l] = t; kept.Add(t); } return t; }
            if (TweaksPlugin.HudText) return BigText(l);
            return v.ToString();
        }
        // numbers outside 0..4096 (the XP text shows values in the thousands, the same ones frame after frame): the last
        // 64 different ones are remembered. An int and a long of the same value give the same text.
        static readonly long[] bigKey = new long[64]; static readonly string[] bigText = new string[64];
        static string BigText(long v)
        {
            int slot = (int)((ulong)v % 64UL);
            var t = bigText[slot];
            if (t != null && bigKey[slot] == v) return t;
            t = v.ToString();
            if (kept.Count > 20000) { kept.Clear(); joined.Clear(); foreach (var x in wholeI) if (x != null) kept.Add(x); foreach (var x in wholeF) if (x != null) kept.Add(x); Array.Clear(bigText, 0, 64); }
            bigKey[slot] = v; bigText[slot] = t; kept.Add(t);
            return t;
        }
        static bool Kept(string t) { return kept.Contains(t) || ReferenceEquals(string.IsInterned(t), t); }
        static void Keep(K3 k, string r)
        {
            if (joined.Count > 8192)
            {
                joined.Clear(); kept.Clear();
                foreach (var t in wholeI) if (t != null) kept.Add(t);
                foreach (var t in wholeF) if (t != null) kept.Add(t);
            }
            joined[k] = r; kept.Add(r);
        }
        public static string Join2(string a, string b)
        {
            if (!TweaksPlugin.HudText || a == null || b == null || a.Length == 0 || b.Length == 0) return string.Concat(a, b);
            var k = new K3 { A = a, B = b }; string r;
            if (joined.TryGetValue(k, out r)) return r;
            r = string.Concat(a, b);
            if (Kept(a) && Kept(b)) Keep(k, r);
            return r;
        }
        public static string Join3(string a, string b, string c)
        {
            if (!TweaksPlugin.HudText || a == null || b == null || c == null || a.Length == 0 || b.Length == 0 || c.Length == 0) return string.Concat(a, b, c);
            var k = new K3 { A = a, B = b, C = c }; string r;
            if (joined.TryGetValue(k, out r)) return r;
            r = string.Concat(a, b, c);
            if (Kept(a) && Kept(b) && Kept(c)) Keep(k, r);
            return r;
        }
        static IEnumerable<CodeInstruction> HudTextTr(IEnumerable<CodeInstruction> ins)
        {
            var self = typeof(Patches);
            var map = new Dictionary<MethodInfo, MethodInfo>
            {
                { AccessTools.Method(typeof(float), "ToString", Type.EmptyTypes), AccessTools.Method(self, "FloatText") },
                { AccessTools.Method(typeof(int), "ToString", Type.EmptyTypes), AccessTools.Method(self, "IntText") },
                { AccessTools.Method(typeof(long), "ToString", Type.EmptyTypes), AccessTools.Method(self, "LongText") },
                { AccessTools.Method(typeof(string), "Concat", new[] { typeof(string), typeof(string) }), AccessTools.Method(self, "Join2") },
                { AccessTools.Method(typeof(string), "Concat", new[] { typeof(string), typeof(string), typeof(string) }), AccessTools.Method(self, "Join3") },
            };
            foreach (var ci in ins)
            {
                MethodInfo to;
                // only a plain call: a ToString reached through 'constrained.' or callvirt has a different shape and is left alone
                if (ci.opcode == OpCodes.Call && ci.operand is MethodInfo && map.TryGetValue((MethodInfo)ci.operand, out to))
                { var n = new CodeInstruction(OpCodes.Call, to); n.labels.AddRange(ci.labels); n.blocks.AddRange(ci.blocks); textHits++; yield return n; }
                else yield return ci;
            }
        }

        // ---- LobbyRowsNotInPlay. LobbyController.UpdatePlayerList runs from Update (and from join events) and calls
        // CreateNewObjects for every player that has no rows yet. Skipping the call leaves the player without rows, so the next
        // pass asks again: the rows appear on the first pass on which the local player cannot move, holds Tab or has the menu open.
        static AccessTools.FieldRef<LobbyController, Transform> lobbyTab;
        static bool LobbyRowPre(LobbyController __instance)
        {
            if (!TweaksPlugin.LobbyRowsLate) return true;
            try
            {
                if (!LocalCanMove()) return true;
                var tab = lobbyTab(__instance); if (tab != null && tab.gameObject.activeInHierarchy) return true;
                var pm = PauseManager.Instance; if (pm == null || pm.pause || pm.inMainMenu || pm.inVictoryMenu) return true;
                return false;
            }
            catch { return true; }
        }

        // ---- LobbyLeaveFix. When you leave or get kicked, the game's ClientInstance.OnDisable redraws the lobby list
        // before it takes the player out of ClientInstance.playerInstances. That redraw can throw (the lobby is half gone),
        // which ends OnDisable early: the player stays in the list, and on leaving yourself the list is never cleared.
        // In the next lobby the same player number is then "already added", and that player gets no name, picture or
        // ready box. The rest of OnDisable is done here when it threw.
        static Exception LeaveFin(ClientInstance __instance, Exception __exception)
        {
            if (__exception == null) return null;
            try
            {
                ClientInstance mine;
                if (ClientInstance.playerInstances.TryGetValue(__instance.PlayerId, out mine) && mine == __instance) ClientInstance.playerInstances.Remove(__instance.PlayerId);
                var lc = LobbyController.Instance;
                if (lc == null || lc.LocalPlayerController == null || lc.LocalPlayerController == __instance) ClientInstance.playerInstances.Clear();
            }
            catch { }
            return null;
        }

        // ---- LobbyStaleIds. A player number still held by a player that is gone (left over from before the fix above,
        // or from another mod) is freed for the new player, so joining never fails on "already added".
        static void AddNewPre(int id)
        {
            ClientInstance old;
            if (ClientInstance.playerInstances.TryGetValue(id, out old) && old == null) ClientInstance.playerInstances.Remove(id);
        }

        // ---- LobbyStaleRows. The lobby keeps its name rows by player number. Rows left from the last lobby (its cleanup
        // was cut short, see LobbyLeaveFix) still hold a number, so the game never makes a row for the new player with that
        // number: no name, picture or ready box. A row that is gone or belongs to another Steam account is dropped, and
        // the game makes a new one in the same pass.
        static AccessTools.FieldRef<LobbyController, Dictionary<int, PlayerListItems>> rowsById;
        static readonly List<int> staleRows = new List<int>();
        static void StaleRowsPre(LobbyController __instance)
        {
            try
            {
                var rows = rowsById(__instance);
                if (rows == null || rows.Count == 0) return;
                foreach (var kv in rows)
                {
                    ClientInstance ci;
                    PlayerListItems r = kv.Value;
                    bool gone = r == null || r.PlayerListItem == null || r.PlayerListItemTab == null;
                    if (gone || (ClientInstance.playerInstances.TryGetValue(kv.Key, out ci) && ci != null && ci.PlayerSteamID != r.PlayerListItem.PlayerSteamID)) staleRows.Add(kv.Key);
                }
                if (staleRows.Count == 0) return;
                foreach (int id in staleRows)
                {
                    PlayerListItems r = rows[id];
                    rows.Remove(id);
                    if (r == null) continue;
                    if (r.PlayerListItem != null) UnityEngine.Object.Destroy(r.PlayerListItem.gameObject);
                    if (r.PlayerListItemTab != null) UnityEngine.Object.Destroy(r.PlayerListItemTab.gameObject);
                }
                staleRows.Clear();
            }
            catch { staleRows.Clear(); }
        }

        // ---- MyceliumPollRate. MyceliumNetworking.PersistentGameObject.Update calls MyceliumNetwork.ReceiveMessages() every frame:
        // SteamNetworkingMessages.ReceiveMessagesOnChannel(120, buffer, 500). Asked 60 times a second instead.
        static float mycNext;
        public static void ApplyLate(Harmony h)
        {
            var self = typeof(Patches); const BindingFlags bf = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            Try("MyceliumPollRate", () =>
            {
                var t = AccessTools.TypeByName("MyceliumNetworking.MyceliumNetwork"); if (t == null) return;   // not installed
                var m = AccessTools.Method(t, "ReceiveMessages", Type.EmptyTypes); if (m == null) throw new Exception("Mycelium is not the version this was written for (1.1.17)");
                h.Patch(m, prefix: new HarmonyMethod(self.GetMethod("MycPre", bf)));
                TweaksPlugin.L.LogInfo("MyceliumPollRate: Mycelium found, it asks Steam for mod messages 60 times a second");
            });
            Try("SpawnViewerNoSearch", () =>
            {
                BepInEx.PluginInfo pi;
                if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("suvilba.straftat.spawnviewer", out pi) || pi.Instance == null) return;   // not installed
                var t = pi.Instance.GetType();
                var m = AccessTools.Method(t, "RefreshPostfix", Type.EmptyTypes); var f = AccessTools.Field(t, "_refreshRequested");
                if (m == null || f == null || f.FieldType != typeof(bool)) throw new Exception("SpawnViewer is not the version this was written for (1.6.0)");
                svPlugin = pi.Instance; svFlag = f;
                h.Patch(m, prefix: new HarmonyMethod(self.GetMethod("SvRefreshPre", bf)));
                TweaksPlugin.L.LogInfo("SpawnViewerNoSearch: SpawnViewer found, its refresh no longer searches every loaded object");
            });
            Try("OnGuiNoLayout (SpawnViewer)", () =>
            {
                BepInEx.PluginInfo pi;
                if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("suvilba.straftat.spawnviewer", out pi) || pi.Instance == null) return;   // not installed
                var t = pi.Instance.GetType();
                var m = AccessTools.Method(t, "Update", Type.EmptyTypes); var f = AccessTools.Field(t, "_uiVisible");
                if (m == null || f == null || f.FieldType != typeof(bool) || f.IsStatic) throw new Exception("SpawnViewer is not the version this was written for (1.6.0)");
                svGui = pi.Instance; svVisible = AccessTools.FieldRefAccess<object, bool>(f);
                h.Patch(m, postfix: new HarmonyMethod(self.GetMethod("SvGuiPost", bf)));
                TweaksPlugin.L.LogInfo("OnGuiNoLayout: SpawnViewer found, no GUI layout pass for it while its panel is hidden");
            });
        }
        // ---- SpawnViewerNoSearch. SpawnViewer.RefreshPostfix (run after the game's spawn methods):
        //        var all = Resources.FindObjectsOfTypeAll<SpawnViewer>(); if (all.Length != 0) all[0]._refreshRequested = true;
        // SpawnViewer is the plugin class itself, so there is exactly one and BepInEx already holds it.
        static object svPlugin; static FieldInfo svFlag;
        static bool SvRefreshPre()
        {
            if (!TweaksPlugin.SvNoSearch || svFlag == null) return true;
            try
            {
                var u = svPlugin as UnityEngine.Object; if (u == null) return true;     // gone: let it search as before
                svFlag.SetValue(svPlugin, true);
                return false;
            }
            catch { return true; }
        }

        // ---- WeaponLayerNoGarbage. Weapon.WeaponUpdate, for a weapon in somebody's hands whose holder is not on layer 6 (= not you):
        //        SetLayerAllChildren(transform, 9)  ->  foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        // every frame: a new array each time. Same walk here into one reused list; a part already on the layer is not written again.
        static readonly List<Transform> layerBuf = new List<Transform>(64);
        static bool WeaponLayerPre(Transform root, int layer)
        {
            if (!TweaksPlugin.WeaponLayer || root == null) return true;
            try
            {
                root.GetComponentsInChildren(true, layerBuf);
                for (int i = 0; i < layerBuf.Count; i++) { var go = layerBuf[i].gameObject; if (go.layer != layer) go.layer = layer; }
                layerBuf.Clear();
                return false;
            }
            catch { layerBuf.Clear(); return true; }
        }

        // ---- OnGuiNoLayout. A MonoBehaviour that has an OnGUI method and useGUILayout = true (the default) gets a Layout event before
        // every other GUI event, and GUILayoutUtility.Begin creates two GUILayoutGroup objects in each Layout event. With useGUILayout =
        // false the engine sends no Layout event to that script and creates nothing. Only allowed for a method that uses no GUILayout calls:
        //   FirstPersonController.OnGUI: empty.      FpsCounter.OnGUI: one GUI.Label with a fixed rectangle.
        //   FishNet PingDisplay.OnGUI (the game's build of it): only works out the public 'ping' number that the HUD reads; no GUI call.
        //   SpawnViewer.OnGUI: GUILayout calls, but only inside if (_uiVisible): the layout pass is given back while the panel is visible.
        static object svGui; static AccessTools.FieldRef<object, bool> svVisible; static bool svLayoutOff;
        static void FpcGuiPost(FirstPersonController __instance)
        {
            if (!TweaksPlugin.GuiNoLayout) return;
            try { __instance.useGUILayout = false; } catch { }
        }
        /// At a scene load and when the setting changes: the FPS counter(s) and the player scripts that exist now.
        internal static void GuiLayoutRefresh()
        {
            try
            {
                bool off = TweaksPlugin.GuiNoLayout;
                foreach (var c in UnityEngine.Object.FindObjectsOfType<FpsCounter>(true)) if (c.useGUILayout == off) c.useGUILayout = !off;
                foreach (var c in UnityEngine.Object.FindObjectsOfType<FirstPersonController>(true)) if (c.useGUILayout == off) c.useGUILayout = !off;
                foreach (var c in UnityEngine.Object.FindObjectsOfType<FishNet.Component.Utility.PingDisplay>(true)) if (c.useGUILayout == off) c.useGUILayout = !off;
                if (!off && svLayoutOff) { var b = svGui as MonoBehaviour; if (b != null) b.useGUILayout = true; svLayoutOff = false; }
            }
            catch { }
        }
        static void SvGuiPost(MonoBehaviour __instance)
        {
            if (svVisible == null) return;
            try
            {
                bool wantOff = TweaksPlugin.GuiNoLayout && !svVisible(__instance);
                if (wantOff == svLayoutOff) return;
                __instance.useGUILayout = !wantOff; svLayoutOff = wantOff;
            }
            catch { }
        }

        static bool MycPre()
        {
            if (!TweaksPlugin.MycPoll) return true;
            float t = Time.unscaledTime; if (t < mycNext) return false;
            mycNext = t + 1f / 60f; return true;
        }

        // ---- OtherArcsAtScreenRate (state; the test itself is at the top of TrickLinePre)
        static readonly Dictionary<int, Weapon> arcWeapon = new Dictionary<int, Weapon>();
        static readonly Dictionary<int, float> arcNext = new Dictionary<int, float>();
        static float arcStep = 1f / 240f;

        // ---- FovTweensNoPileUp. FirstPersonController.HandleCameraController calls ChangeFOV -> playerCamera.DOFieldOfView(fov, speed)
        // on every frame on which the camera's field of view is not yet the wanted one. Each call starts a new tween; the earlier ones
        // keep running. DOTween updates tweens in the order they were started, and a tween reads its start value on its first update.
        // So in one update pass: ..., T(n-2) writes, T(n-1) writes, T(n) starts from what T(n-1) just wrote and writes. What the frame
        // shows is T(n)'s value; what T(n) started from is T(n-1)'s. Nothing ever reads what T(n-2) and older wrote, as long as a
        // newer tween is still running after them. Rule used here: when a new tween is started, every tween older than the previous
        // one is stopped if some newer tween ends at the same time or later (then it can never be the last writer again).
        // A tween with callbacks or loops is never touched. A tween is recognised as one of these by a mark that DOTween wipes when it
        // recycles the object, so a recycled tween is never stopped by mistake.
        struct FovT { public DG.Tweening.Tween T; public float End; }
        static readonly Dictionary<int, List<FovT>> fovTweens = new Dictionary<int, List<FovT>>();
        const int FovMark = 0x464F5631;
        internal static int fovStopped;
        static bool PlainTween(DG.Tweening.Tween t)
        {
            return t.onComplete == null && t.onKill == null && t.onUpdate == null && t.onStepComplete == null && t.onPlay == null && t.onPause == null && t.onRewind == null
                && DG.Tweening.TweenExtensions.Loops(t) == 1;
        }
        static void FovTweenPost(Camera target, float duration, DG.Tweening.Tween __result)
        {
            if (!TweaksPlugin.FovPile || __result == null || target == null) return;
            try
            {
                int id = target.GetInstanceID(); List<FovT> list;
                if (!fovTweens.TryGetValue(id, out list)) { list = new List<FovT>(8); fovTweens[id] = list; }
                float now = Time.time, end = now + duration;
                // forget entries that are over, were recycled by DOTween (mark gone), or are this very object under an older entry
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var e = list[i];
                    if (now >= e.End || ReferenceEquals(e.T, __result) || e.T == null || !e.T.active || e.T.intId != FovMark || !ReferenceEquals(e.T.target, target)) list.RemoveAt(i);
                }
                __result.intId = FovMark;
                // FovTweensReuse: when this one ends or is stopped, DOTween keeps the object in its pool and hands it out for the next one.
                // DOTween wipes this flag (and the mark) when it recycles, so it is set on every new one. The game keeps no reference to these.
                if (TweaksPlugin.FovReuse) DG.Tweening.TweenSettingsExtensions.SetRecyclable(__result, true);
                int prev = list.Count - 1;          // the one before the new one: always kept
                float cover = end;                  // the latest end among the tweens newer than the one being looked at
                for (int i = prev; i >= 0; i--)
                {
                    var e = list[i];
                    if (i != prev && e.End <= cover && PlainTween(e.T)) { DG.Tweening.TweenExtensions.Kill(e.T, false); list.RemoveAt(i); fovStopped++; }
                    else if (e.End > cover) cover = e.End;
                }
                list.Add(new FovT { T = __result, End = end });
            }
            catch { }
        }

        static bool GunScanPre()
        {
            if (!TweaksPlugin.GunScan || gsOwn) return true;
            float last; try { last = (float)gsLast.GetValue(null); } catch { return true; }
            if (Time.realtimeSinceStartup - last > 0.005f) return true;   // not started from a sound: the map-load search
            return !LocalCanMove();
        }
        // The scan for GunSounds runs 0.6 s after the countdown coroutine of your own player starts (the player stands
        // still for at least 1.9 s there). From PauseManager.OnRoundStarted alone it cost 2 ms on the first frame of
        // every take, the frame the player is let go; OnRoundStarted only does it if the countdown did not.
        static bool gsDoneThisTake;
        static void CountdownPost(PlayerManager __instance)
        {
            if (!TweaksPlugin.GunScan || gsScan == null) return;
            try { if (__instance != null && __instance.IsOwner && TweaksPlugin.I != null) { gsDoneThisTake = false; TweaksPlugin.I.StartCoroutine(GunScanInCountdown()); } } catch { }
        }
        static System.Collections.IEnumerator GunScanInCountdown()
        {
            yield return new WaitForSecondsRealtime(0.6f);
            if (!TweaksPlugin.GunScan || gsScan == null || gsDoneThisTake || LocalCanMove()) yield break;
            gsOwn = true;
            try { gsScan(); gsDoneThisTake = true; } catch { } finally { gsOwn = false; }
        }
        static void GunScanAtCountdown()
        {
            if (!TweaksPlugin.GunScan || gsScan == null) return;
            if (gsDoneThisTake) { gsDoneThisTake = false; return; }
            gsOwn = true;
            try { gsScan(); } catch { } finally { gsOwn = false; }
        }
        static bool LocalCanMove()
        {
            try
            {
                var ci = ClientInstance.Instance;
                var p = ci != null && ci.PlayerSpawner != null ? ci.PlayerSpawner.player : null;
                return p != null && p.canMove && p.isActiveAndEnabled && p.IsOwner;
            }
            catch { return false; }
        }

        // ---- TrickShotLineNoGarbage. HeathenEngineering.PhysKit.TrickShotLine.LateUpdate is
        //        if (continuousRun) trickShot.Predict();
        //        var list = new List<Vector3>(); foreach path, foreach step: list.Add(step.position);
        //        lineRenderer.positionCount = list.Count; lineRenderer.SetPositions(list.ToArray());
        // every frame for every launcher in the map. The same here, with the points in one reused array (SetPositions only reads as
        // many as positionCount says). Predict() is called exactly as the game does: its result is what the weapon fires with.
        static Vector3[] tsPts = new Vector3[256];
        static AccessTools.FieldRef<HeathenEngineering.PhysKit.TrickShotLine, HeathenEngineering.PhysKit.TrickShot> tsShot;
        static AccessTools.FieldRef<HeathenEngineering.PhysKit.TrickShotLine, LineRenderer> tsLine;
        /// True only for a weapon that some player other than you has in the hands right now. A weapon on the ground, in a dispenser or
        /// in your own hands is false, whoever owns it: those can be fired by you next frame, with the arc worked out this frame.
        static bool HeldByAnother(Weapon w)
        {
            try
            {
                var ib = w.behaviour; if ((object)ib == null) return false;
                if ((object)ib.rootObject == null) return false;
                var pc = ib.playerController; if ((object)pc == null || pc == null) return false;
                return !pc.IsOwner;
            }
            catch { return false; }
        }
        static bool TrickLinePre(HeathenEngineering.PhysKit.TrickShotLine __instance)
        {
            if (TweaksPlugin.OtherArcs)
            {
                int id = __instance.GetInstanceID(); Weapon w;
                if (!arcWeapon.TryGetValue(id, out w)) { w = __instance.GetComponentInParent<Weapon>(); arcWeapon[id] = w; }
                if (w != null && !w.IsOwner && HeldByAnother(w))      // in another player's hands: its arc is only a picture here
                {
                    float now = Time.unscaledTime, next;
                    if (arcNext.TryGetValue(id, out next) && now < next) return false;
                    arcNext[id] = now + arcStep;
                }
            }
            if (!TweaksPlugin.TrickLine) return true;
            var shot = tsShot(__instance); var line = tsLine(__instance);
            if (shot == null || line == null) return true;   // not started: the game's own code, as it was
            if (__instance.continuousRun)
            {
                shot.Predict();
            }
            var paths = shot.prediction; if (paths == null) return false;
            int n = 0;
            for (int p = 0; p < paths.Count; p++) { var st = paths[p].steps; if (st != null) n += st.Length; }
            if (tsPts.Length < n) tsPts = new Vector3[Mathf.NextPowerOfTwo(n)];
            int k = 0;
            for (int p = 0; p < paths.Count; p++) { var st = paths[p].steps; if (st == null) continue; for (int i = 0; i < st.Length; i++) tsPts[k++] = st[i].position; }
            line.positionCount = n;
            line.SetPositions(tsPts);
            return false;
        }

        // ---- KillCamLean. KillCam.Update starts its mid-match check with
        //        if (GameObject.FindGameObjectsWithTag("PlayerGraphics").Length > 0) { if (<my own player> == null) { ... } }
        // every frame. With a living player of your own the inner test fails whatever the search finds, so the search (the first of several
        // in that method) gets an empty answer then and is not made. Without a player of your own it is made exactly as before.
        static readonly GameObject[] noObjects = new GameObject[0];
        static GameObject[] KillCamTags(string tag)
        {
            if (TweaksPlugin.KillCamLean)
            {
                var lc = LobbyController.Instance; var me = lc != null ? lc.LocalPlayerController : null; var sp = me != null ? me.PlayerSpawner : null;
                if (sp != null && sp.player != null) return noObjects;
            }
            return GameObject.FindGameObjectsWithTag(tag);
        }
        static IEnumerable<CodeInstruction> KillCamTr(IEnumerable<CodeInstruction> ins)
        {
            var find = AccessTools.Method(typeof(GameObject), "FindGameObjectsWithTag", new[] { typeof(string) });
            var mine = typeof(Patches).GetMethod("KillCamTags", BindingFlags.Static | BindingFlags.NonPublic);
            bool done = false;
            foreach (var i in ins)
            {
                if (!done && i.opcode == System.Reflection.Emit.OpCodes.Call && Equals(i.operand, find)) { i.operand = mine; done = true; }
                yield return i;
            }
            if (!done) TweaksPlugin.L.LogWarning("KillCamLean: the search was not found in KillCam.Update (game update?); nothing changed");
        }

        // ---- KillCamNoGarbage. KillCam.Update, every frame while dead and watching:
        //   switchCamText.text = "Mouse Buttons to switch player. " + InteractPromptLetter + " to toggle free cam. H to hide HUD";   (2 places)
        //   playerHpText.text = $"Health : {Mathf.Ceil(health / 4f * 100f)}";                                                       (2 places)
        //   playerNameText.text = "Player : " + PlayerName;                                                                         (2 places)
        //   enemy.gameObject.name != "Graphics"                                                                                     (1 place)
        //   transform.DOMove(...); transform.DOLookAt(...);                                                                         (3 places)
        // Each text keeps its last inputs and result: the same inputs give the same string object back. The tweens are marked
        // recyclable (the game keeps no reference to them; it only calls transform.DOKill()).
        internal static int kcHits;
        static string kc3a, kc3b, kc3c, kc3r, kc2a, kc2b, kc2r, kcFf, kcFr; static float kcFv = float.NaN;
        static string KcJoin3(string a, string b, string c)
        {
            if (!TweaksPlugin.KillCamText) return string.Concat(a, b, c);
            if (kc3r != null && a == kc3a && b == kc3b && c == kc3c) return kc3r;
            kc3a = a; kc3b = b; kc3c = c; return kc3r = string.Concat(a, b, c);
        }
        static string KcJoin2(string a, string b)
        {
            if (!TweaksPlugin.KillCamText) return string.Concat(a, b);
            if (kc2r != null && a == kc2a && b == kc2b) return kc2r;
            kc2a = a; kc2b = b; return kc2r = string.Concat(a, b);
        }
        static string KcFormat(string format, float v)
        {
            if (!TweaksPlugin.KillCamText) return string.Format(format, v);
            if (kcFr != null && v == kcFv && format == kcFf) return kcFr;
            kcFf = format; kcFv = v; return kcFr = string.Format(format, v);
        }
        static string KcName(UnityEngine.Object o)
        {
            if (!TweaksPlugin.KillCamText) return o.name;
            int id = o.GetInstanceID(); string n;
            if (itemNames.TryGetValue(id, out n)) return n;
            n = o.name; if (itemNames.Count > 4096) itemNames.Clear();
            itemNames[id] = n; return n;
        }
        static void KcRecycle(DG.Tweening.Tween t)
        {
            if (TweaksPlugin.KillCamText && t != null) DG.Tweening.TweenSettingsExtensions.SetRecyclable(t, true);
        }
        static IEnumerable<CodeInstruction> KillCamTextTr(IEnumerable<CodeInstruction> ins)
        {
            var self = typeof(Patches); const BindingFlags bf = BindingFlags.Static | BindingFlags.NonPublic;
            var cat3 = AccessTools.Method(typeof(string), "Concat", new[] { typeof(string), typeof(string), typeof(string) });
            var cat2 = AccessTools.Method(typeof(string), "Concat", new[] { typeof(string), typeof(string) });
            var fmt = AccessTools.Method(typeof(string), "Format", new[] { typeof(string), typeof(object) });
            var name = AccessTools.PropertyGetter(typeof(UnityEngine.Object), "name");
            var list = new List<CodeInstruction>(ins);
            for (int i = 0; i < list.Count; i++)
            {
                var ci = list[i];
                if (ci.Calls(cat3)) { ci.opcode = OpCodes.Call; ci.operand = self.GetMethod("KcJoin3", bf); kcHits++; }
                else if (ci.Calls(cat2)) { ci.opcode = OpCodes.Call; ci.operand = self.GetMethod("KcJoin2", bf); kcHits++; }
                else if (ci.Calls(name)) { ci.opcode = OpCodes.Call; ci.operand = self.GetMethod("KcName", bf); kcHits++; }
                else if (ci.opcode == OpCodes.Box && (ci.operand as Type) == typeof(float) && i + 1 < list.Count && list[i + 1].Calls(fmt) && list[i + 1].labels.Count == 0)
                {
                    // the float stays on the stack unboxed and the helper takes it as a float
                    ci.opcode = OpCodes.Nop; ci.operand = null;
                    list[i + 1].opcode = OpCodes.Call; list[i + 1].operand = self.GetMethod("KcFormat", bf); kcHits++;
                }
                else if (ci.opcode == OpCodes.Call && ci.operand is MethodInfo)
                {
                    var m = (MethodInfo)ci.operand;
                    if (m.DeclaringType == typeof(DG.Tweening.ShortcutExtensions) && (m.Name == "DOMove" || m.Name == "DOLookAt") && typeof(DG.Tweening.Tween).IsAssignableFrom(m.ReturnType))
                    {
                        list.Insert(i + 1, new CodeInstruction(OpCodes.Dup));
                        list.Insert(i + 2, new CodeInstruction(OpCodes.Call, self.GetMethod("KcRecycle", bf)));
                        i += 2; kcHits++;
                    }
                }
            }
            return list;
        }

        // ---- WeaponNoInputCopy. Weapon.Awake___UserLogic starts with "playerControls = new PlayerControls()": a private field nothing in the
        // game reads. Building it parses the whole binding set from JSON. Only if the very next instruction stores it in that field is the
        // "new" swapped for a call that gives null while the tweak is on.
        static PlayerControls WeaponControls() { return TweaksPlugin.WeaponInputCopyOff ? null : new PlayerControls(); }
        static IEnumerable<CodeInstruction> WeaponControlsTr(IEnumerable<CodeInstruction> ins)
        {
            var ctor = AccessTools.Constructor(typeof(PlayerControls), Type.EmptyTypes);
            var field = AccessTools.Field(typeof(Weapon), "playerControls");
            var mine = typeof(Patches).GetMethod("WeaponControls", BindingFlags.Static | BindingFlags.NonPublic);
            var list = new List<CodeInstruction>(ins); bool done = false;
            for (int i = 0; i + 1 < list.Count && !done; i++)
                if (ctor != null && field != null && list[i].opcode == System.Reflection.Emit.OpCodes.Newobj && Equals(list[i].operand, ctor)
                    && list[i + 1].opcode == System.Reflection.Emit.OpCodes.Stfld && Equals(list[i + 1].operand, field))
                { list[i].opcode = System.Reflection.Emit.OpCodes.Call; list[i].operand = mine; done = true; }
            if (!done) TweaksPlugin.L.LogWarning("WeaponNoInputCopy: the copy was not found in Weapon.Awake (game update?); nothing changed");
            return list;
        }

        // ---- VoiceKeyOffThread: Steam opens or closes the microphone when the voice key goes down or up, and the call returns only when that
        // is done. One helper thread makes those calls instead, in the order they were asked for, so the game does not wait.
        static Action voiceStart, voiceStop; static readonly Queue<Action> voiceQ = new Queue<Action>(); static System.Threading.Thread voiceThread;
        static void VoiceDo(Action a)
        {
            lock (voiceQ)
            {
                voiceQ.Enqueue(a);
                if (voiceThread == null) { voiceThread = new System.Threading.Thread(VoiceLoop) { IsBackground = true, Name = "UT voice key" }; voiceThread.Start(); }
                System.Threading.Monitor.Pulse(voiceQ);
            }
        }
        static void VoiceLoop()
        {
            while (true)
            {
                Action a;
                lock (voiceQ) { while (voiceQ.Count == 0) System.Threading.Monitor.Wait(voiceQ); a = voiceQ.Dequeue(); }
                try { a(); } catch (Exception e) { TweaksPlugin.L.LogWarning("VoiceKeyOffThread: " + e.Message); }
            }
        }
        // Steam's own two calls are patched, so every caller is covered (the game's voice key, and the recorder restarting the microphone
        // by itself). On the helper thread the call goes through to Steam; from anywhere else it is handed to the helper thread.
        static bool VoiceStartPre() { if (!TweaksPlugin.VoiceOffThread || voiceStart == null || System.Threading.Thread.CurrentThread == voiceThread) return true; VoiceDo(voiceStart); return false; }
        static bool VoiceStopPre() { if (!TweaksPlugin.VoiceOffThread || voiceStop == null || System.Threading.Thread.CurrentThread == voiceThread) return true; VoiceDo(voiceStop); return false; }

        // ---- PreCompile: the game's code is compiled to machine code method by method, each one the first time it runs. The first shot
        // with a weapon type runs a lot of code for the first time, and the frame waits for the compiler (13 to 30 ms measured).
        // A helper thread compiles everything shortly after the game starts instead. Nothing is run, only compiled.
        static readonly string[] PreAssemblies = { "Assembly-CSharp", "Assembly-CSharp-firstpass", "FishNet.Runtime", "Heathen.Steamworks", "Heathen.Core", "DOTween", "ch.sycoforge.Decal", "LambdaTheDev.NetworkAudioSync", "LambdaTheDev.NetworkAudioSync.FishNet",
            /* added in 0.8.19: the grenade and launcher physics, and the network library's helpers */ "Heathen.PhysKit", "GameKit.Dependencies", "GameKit.Utilities" };
        static bool preStarted;
        // Is a class's static start-up code harmless to run early and on another thread? Yes if every method it calls and every
        // constructor it runs belongs to the base library (mscorlib, System, System.Core, netstandard), and every static field it
        // touches is its own. The one other case: the holder class the compiler makes for lambdas ("<>c"), whose start-up code only
        // creates the holder itself. Anything else, anything unreadable, any try block: not harmless, the class is left alone.
        static OpCode[] op1, op2; static bool[] has1, has2;
        // Two more ways a class with start-up code can be compiled ahead:
        //  - its start-up code only uses Unity's plain number types (Vector3.one, new Color(...)): those are ordinary
        // managed code;
        //  - its start-up code is run here first, on the main thread at game start, where it is allowed to create engine
        // objects. Only for classes listed by name whose start-up code was read: the decal library's MeshUtil (fills
        // tables, creates one Mesh) and EasyDecalManager (creates empty lists). Without them the first scorch mark of a
        // session compiles most of the decal library in the frame of the explosion (3.5 to 4.8 ms measured).
        static readonly string[] MainThreadInit = { "ch.sycoforge.Decal.MeshUtil", "ch.sycoforge.Decal.EasyDecalManager" };
        static readonly HashSet<Type> preInited = new HashSet<Type>();
        static volatile bool preInitReady;
        internal static void PreInitOnMainThread()
        {
            try
            {
                if (TweaksPlugin.PreCompileOn)
                    foreach (var name in MainThreadInit)
                    {
                        try
                        {
                            var t = AccessTools.TypeByName(name); if (t == null) continue;
                            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
                            preInited.Add(t);
                        }
                        catch (Exception e) { TweaksPlugin.L.LogWarning("PreCompile: start-up code of " + name + " could not be run ahead: " + e.Message); }
                    }
            }
            finally { preInitReady = true; }
        }
        static readonly HashSet<string> UnityNumbers = new HashSet<string> { "UnityEngine.Vector2", "UnityEngine.Vector3", "UnityEngine.Vector4", "UnityEngine.Color", "UnityEngine.Color32", "UnityEngine.Mathf", "UnityEngine.Vector2Int", "UnityEngine.Vector3Int" };
        static bool BaseLib(Type t)
        {
            if (t != null && t.FullName != null && UnityNumbers.Contains(t.FullName) && t.Assembly.GetName().Name == "UnityEngine.CoreModule") return true;
            if (t == null) return false;
            string n = t.Assembly.GetName().Name;
            return n == "mscorlib" || n == "System" || n == "System.Core" || n == "netstandard";
        }
        static bool HarmlessInit(Type t)
        {
            try
            {
                if (op1 == null)
                {
                    var a1 = new OpCode[256]; var a2 = new OpCode[256]; has1 = new bool[256]; has2 = new bool[256];
                    foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (f.FieldType != typeof(OpCode)) continue;
                        var oc = (OpCode)f.GetValue(null); int v = (ushort)oc.Value;
                        if (oc.Size == 1) { a1[v & 0xff] = oc; has1[v & 0xff] = true; } else { a2[v & 0xff] = oc; has2[v & 0xff] = true; }
                    }
                    op2 = a2; op1 = a1;
                }
                var init = t.TypeInitializer; if (init == null) return true;
                var body = init.GetMethodBody(); if (body == null || body.ExceptionHandlingClauses.Count > 0) return false;
                byte[] il = body.GetILAsByteArray(); if (il == null) return false;
                var mod = t.Module; Type[] ta = t.IsGenericType ? t.GetGenericArguments() : null;
                bool holder = t.Name == "<>c";
                int i = 0;
                while (i < il.Length)
                {
                    OpCode oc; int b = il[i++];
                    if (b == 0xFE) { if (i >= il.Length) return false; b = il[i++]; if (!has2[b]) return false; oc = op2[b]; }
                    else { if (!has1[b]) return false; oc = op1[b]; }
                    switch (oc.OperandType)
                    {
                        case OperandType.InlineNone: break;
                        case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: i += 1; break;
                        case OperandType.InlineVar: i += 2; break;
                        case OperandType.InlineI8: case OperandType.InlineR: i += 8; break;
                        case OperandType.InlineSwitch: { int n = BitConverter.ToInt32(il, i); i += 4 + 4 * n; break; }
                        case OperandType.InlineMethod:
                        {
                            var mb = mod.ResolveMethod(BitConverter.ToInt32(il, i), ta, null); i += 4;
                            var dt = mb.DeclaringType;
                            if (BaseLib(dt)) break;
                            if (holder && dt == t && mb.IsConstructor && !mb.IsStatic) break;
                            return false;
                        }
                        case OperandType.InlineField:
                        {
                            var fi = mod.ResolveField(BitConverter.ToInt32(il, i), ta, null); i += 4;
                            if (fi.DeclaringType == t || BaseLib(fi.DeclaringType)) break;
                            return false;
                        }
                        case OperandType.InlineTok: case OperandType.InlineType: case OperandType.InlineString: case OperandType.InlineSig:
                        case OperandType.InlineI: case OperandType.InlineBrTarget: case OperandType.ShortInlineR: i += 4; break;
                        default: return false;
                    }
                }
                return i == il.Length;
            }
            catch { return false; }
        }

        public static void PreCompile()
        {
            if (preStarted || !TweaksPlugin.PreCompileOn) return; preStarted = true;
            var th = new System.Threading.Thread(() =>
            {
                try
                {
                    System.Threading.Thread.Sleep(4000);   // let the game finish starting first
                    for (int w = 0; w < 200 && !preInitReady; w++) System.Threading.Thread.Sleep(100);   // and the main thread run the listed start-up code
                    var sw = System.Diagnostics.Stopwatch.StartNew(); int done = 0, skipped = 0, failed = 0;
                    const BindingFlags bf = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                    int withInit = 0, withHarmless = 0;
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (Array.IndexOf(PreAssemblies, a.GetName().Name) < 0) continue;
                        Type[] types; try { types = a.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); } catch { continue; }
                        foreach (var t in types)
                        {
                            if (t.IsGenericTypeDefinition || t.ContainsGenericParameters || t.IsInterface) { skipped++; continue; }
                            // On this runtime, compiling a method of a class runs that class's static start-up code
                            // first. That code must run where and when the game runs it (it may touch the engine), so
                            // classes that have any are left alone, unless that start-up code can be shown to touch
                            // nothing but the base library (it fills a table, makes a list): then it does not matter
                            // where or when it runs, and the class is compiled too.
                            bool init; try { init = t.TypeInitializer != null; } catch { init = true; }
                            if (init) { if (preInited.Contains(t) || HarmlessInit(t)) withHarmless++; else { withInit++; continue; } }
                            MethodBase[] ms; try { ms = t.GetMethods(bf).Cast<MethodBase>().Concat(t.GetConstructors(bf)).ToArray(); } catch { failed++; continue; }
                            foreach (var m in ms)
                            {
                                try
                                {
                                    if (m.IsAbstract || m.IsGenericMethodDefinition || m.ContainsGenericParameters || (m.Attributes & MethodAttributes.PinvokeImpl) != 0 || m.GetMethodBody() == null) { skipped++; continue; }
                                    m.MethodHandle.GetFunctionPointer(); done++;   // asks for the method's machine code, which compiles it
                                }
                                catch { failed++; }
                                if ((done & 255) == 255) System.Threading.Thread.Sleep(1);   // leave the processor to the game now and then
                            }
                        }
                    }
                    skipped += withInit;
                    TweaksPlugin.L.LogInfo("PreCompile: " + done + " methods compiled ahead of time in " + (sw.ElapsedMilliseconds / 1000.0).ToString("0.0") + " s on a helper thread (" + skipped + " methods or classes skipped: generic, without a body, or with start-up code that touches the engine or the game, " + failed + " could not be compiled; " + withHarmless + " classes with harmless start-up code included)");
                }
                catch (Exception e) { TweaksPlugin.L.LogWarning("PreCompile stopped: " + e.Message); }
            }) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = "UT precompile" };
            th.Start();
        }

        static void Try(string what, Action a) { try { a(); } catch (Exception e) { TweaksPlugin.L.LogWarning(what + " could not be patched (game update?): " + e.Message); } }

        // ---- CrosshairFix: the two writes to the crosshair image's 'enabled' are collected and the last one is applied once per frame.
        static IEnumerable<CodeInstruction> EnabledTr(IEnumerable<CodeInstruction> ins)
        {
            var set = AccessTools.PropertySetter(typeof(Behaviour), "enabled"); var rep = typeof(Patches).GetMethod("SetEnabled", BindingFlags.Static | BindingFlags.Public);
            foreach (var ci in ins)
            {
                if (ci.Calls(set)) { var n = new CodeInstruction(OpCodes.Call, rep); n.labels.AddRange(ci.labels); n.blocks.AddRange(ci.blocks); yield return n; }
                else yield return ci;
            }
        }
        public static void SetEnabled(Behaviour b, bool v)
        {
            if (!TweaksPlugin.CrosshairFix) { if (pend) FlushEnabled(); b.enabled = v; return; }
            if (pend && !ReferenceEquals(pendB, b)) FlushEnabled();
            pendB = b; pendV = v; pend = true;
        }
        public static void FlushEnabled()
        {
            if (!pend) return; pend = false;
            if (pendB && pendB.enabled != pendV) pendB.enabled = pendV;
            pendB = null;
        }

        static bool LobbyPre()
        {
            if (!TweaksPlugin.LobbyThrottle || TweaksPlugin.InMenu) return true;
            float t = Time.unscaledTime; if (t < lobbyNext) return false; lobbyNext = t + TweaksPlugin.LobbyInterval; return true;
        }
        static bool SteamCheckPre()
        {
            if (!TweaksPlugin.SteamCheckThrottle) return true;
            float t = Time.unscaledTime; if (t < steamCheckNext) return false; steamCheckNext = t + 0.5f; return true;
        }
    }
}

