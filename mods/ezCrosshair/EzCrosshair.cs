// ezCrosshair - a STRAFTAT mod (BepInEx 5). Dorsth was here.
//
// Your own crosshair from a set of small pixel crosshairs, drawn over the game in the color and size you pick. The
// game's crosshair is hidden while it's on. It hides while you look through a scope unless you choose to keep it.
// Everything is set in its own menu (F2 by default). Only changes what you see on your own screen.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using ComputerysModdingUtilities;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[assembly: AssemblyTitle("ezCrosshair")]
[assembly: AssemblyDescription("STRAFTAT: pick your own crosshair, its color and size.")]
[assembly: AssemblyVersion("1.2.3.0")]
[assembly: AssemblyFileVersion("1.2.1.0")]

// Only changes what you see on your own screen, so matchmaking with unmodded players still works.
[assembly: StraftatMod(isVanillaCompatible: true)]

namespace EzCrosshair
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(ModMenuGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class EzCrosshairPlugin : BaseUnityPlugin
    {
        public const string Guid = "dorsth.straftat.ezcrosshair";
        public const string Name = "ezCrosshair";
        public const string Version = "1.2.3";
        internal const string ModMenuGuid = "kestrel.straftat.modmenu";

        // in the order they're shown (Images.cs has each one's picture)
        internal static readonly string[] Names =
        {
            "dot", "square", "circle", "diamond", "dark diamond", "pinhole", "plus", "outlined plus", "chunky plus", "cross", "star", "brackets"
        };

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> On;
        internal static ConfigEntry<string> Pick;
        internal static ConfigEntry<Color> Tint;
        internal static ConfigEntry<bool> HitOn;
        internal static ConfigEntry<Color> HitTint;
        internal static ConfigEntry<float> Scale;
        internal static ConfigEntry<bool> ShowScoped;
        internal static ConfigEntry<KeyCode> MenuKey;
        internal static ConfigEntry<string> MenuSpot;

        private void Awake()
        {
            Log = Logger;
            Log.LogMessage("Dorsth was here");
            // BepInEx's manager object (every plugin's home) is a plain object the game destroys on the way into a lobby, unless something hides it (Mod Menu does): hidden here too, so this works without Mod Menu.
            gameObject.hideFlags = HideFlags.HideAndDontSave;

            On = Config.Bind("Crosshair", "Enabled", true, "Shows your crosshair instead of the game's.");
#if DORSTH_BUNDLE
            Custom.Read(Config);
            Pick = Config.Bind("Crosshair", "Crosshair", Array.IndexOf(Custom.All, Names[0]) >= 0 ? Names[0] : Custom.All[0], new ConfigDescription("Which crosshair: every PNG in " + Custom.Folder + ". Add your own there.", new AcceptableValueList<string>(Custom.All)));
#else
            Pick = Config.Bind("Crosshair", "Crosshair", Names[0], new ConfigDescription("Which crosshair.", new AcceptableValueList<string>(Names)));
#endif
            Tint = Config.Bind("Crosshair", "Color", Color.white, "Color of the crosshair. White keeps its own colors; the dark outline stays dark.");
            HitOn = Config.Bind("Crosshair", "Hit Color", false, "The crosshair turns the hit color for a moment when you hit someone.");
            HitTint = Config.Bind("Crosshair", "Hit Color Value", new Color(1f, 0.25f, 0.25f, 1f), "The color the crosshair turns when you hit someone.");
            Scale = Config.Bind("Crosshair", "Scale", 1f, new ConfigDescription(
                "Size of the crosshair: 1 is its own size on a 1080p screen.",
                new AcceptableValueRange<float>(0f, 2f)));
            ShowScoped = Config.Bind("Crosshair", "Show While Scoped", false, "Keeps the crosshair on while you look through a scope.");
            MenuKey = Config.Bind("Keys", "Menu", KeyCode.F2, "Opens the crosshair menu.");
            MenuSpot = Config.Bind("Keys", "Menu Window", "0,0", "Where the menu window is, from the middle of the screen. Drag its top bar to move it, double-click the bar to center it.");

            try
            {
                var harmony = new Harmony(Guid);
                harmony.Patch(AccessTools.Method(typeof(Crosshair), "Update"), null, new HarmonyMethod(typeof(EzCrosshairPlugin), nameof(GameCrosshairPostfix)));
#if !DORSTH_BUNDLE
                harmony.Patch(AccessTools.Method(typeof(PauseManager), "Update"), new HarmonyMethod(typeof(EzCrosshairPlugin), nameof(PauseUpdatePrefix)));
                // escape closes the menu: that press doesn't also open the game's pause menu (in a map) or its menu
                // panel (on the home screen)
                harmony.Patch(AccessTools.Method(typeof(PauseManager), "Menu"), new HarmonyMethod(typeof(EzCrosshairPlugin), nameof(EscapePrefix)));
                harmony.Patch(AccessTools.Method(typeof(WindowPositionTween), "Update"), new HarmonyMethod(typeof(EzCrosshairPlugin), nameof(EscapePrefix)));
#endif
            }
            catch (Exception e) { Log.LogError("Couldn't hide the game's crosshair: " + e); }

            // a changed setting is shown at once (the crosshair is otherwise only gone over when something changed)
            Config.SettingChanged += (sender, e) => { Overlay.Dirty = true; };

            // these run on this plugin's own object (the one BepInEx keeps for its mods)
            gameObject.AddComponent<Overlay>();
#if !DORSTH_BUNDLE
            gameObject.AddComponent<Menu>();

            if (Chainloader.PluginInfos.ContainsKey(ModMenuGuid))
            {
                try { ModMenuPage(Config); }
                catch (Exception e) { Log.LogWarning("Mod Menu page failed: " + e.Message); }
            }
#endif
        }

#if !DORSTH_BUNDLE
        // Everything is set in the crosshair menu, so Mod Menu only shows the key that opens it. Only touched when
        // Mod Menu is installed.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ModMenuPage(ConfigFile config)
        {
            var hide = new List<ConfigEntryBase>();
            foreach (var kv in config) if (kv.Value != MenuKey) hide.Add(kv.Value);
            ModMenu.Api.ModMenuCustomisation.HideEntries(hide);
        }

        // Menu open: the game stays paused, so your view doesn't turn with the mouse. Its pause script would set that
        // back and lock the cursor every frame, so it waits while the menu is open (like the STRAFTH menu does).
        private static bool PauseUpdatePrefix(PauseManager __instance)
        {
            if (!Menu.IsOpen) return true;
            __instance.pause = true;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
            return false;
        }

        private static bool EscapePrefix() { return !Menu.IsOpen && !Menu.SwallowEscape; }
#endif

        // After the game picks its crosshair for the frame: see-through while ours is on (its scope image stays).
        // The image itself stays switched on. The game's pause script switches it back on every frame, so switching it
        // off here meant off and on again each frame, and the whole HUD was laid out and redrawn each frame for it.
        private static bool _hidGame;

        private static void GameCrosshairPostfix(Crosshair __instance)
        {
            var image = __instance.image;
            if (image == null) return;
            CanvasRenderer r = image.canvasRenderer;
            if (On.Value)
            {
                _hidGame = true;
                if (r.GetAlpha() != 0f) r.SetAlpha(0f);
            }
            else if (_hidGame)
            {
                _hidGame = false;
                if (r.GetAlpha() == 0f) r.SetAlpha(1f);
            }
        }
    }

#if DORSTH_BUNDLE
    /// <summary>In MONOLITH the crosshairs are PNG files in a folder, each one a choice named after its file: the ones that
    /// come with it (written there once, from the pictures in the dll) and any you add. One picture pixel is one screen
    /// pixel on a 1080 high screen at size 1, and the color setting tints it (white keeps the picture's own colors).</summary>
    internal static class Custom
    {
        internal const int Largest = 256;
        internal static readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        internal static string[] All = EzCrosshairPlugin.Names;
        internal static string Folder { get { return System.IO.Path.Combine(Paths.PluginPath, "MONOLITH", "Crosshairs"); } }

        internal static void Read(ConfigFile config)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Folder);
                // once: the crosshairs that come with it become files like any other. One you delete afterwards stays gone.
                ConfigEntry<bool> written = config.Bind("Internal", "Crosshair Files Written", false, "Set once the crosshairs that come with MONOLITH have been written to its Crosshairs folder.");
                if (!written.Value)
                {
                    foreach (string n in EzCrosshairPlugin.Names)
                    {
                        string path = System.IO.Path.Combine(Folder, n + ".png");
                        if (!System.IO.File.Exists(path)) System.IO.File.WriteAllBytes(path, Convert.FromBase64String(Images.Get(n)));
                    }
                    written.Value = true;
                }
                string[] found = System.IO.Directory.GetFiles(Folder, "*.png");
                Array.Sort(found, StringComparer.OrdinalIgnoreCase);
                foreach (string f in found)
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (name.Length > 0) Files[name] = f;
                }
                // the ones that came with it first, in their own order; then yours by name
                var all = new List<string>();
                foreach (string n in EzCrosshairPlugin.Names) if (Files.ContainsKey(n)) all.Add(n);
                int own = 0;
                foreach (string f in found)
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (name.Length > 0 && !all.Contains(name)) { all.Add(name); own++; }
                }
                if (all.Count > 0) All = all.ToArray();   // an empty folder: the ones in the dll
                EzCrosshairPlugin.Log.LogInfo(all.Count + " crosshairs in " + Folder + (own > 0 ? " (" + own + " of your own)." : "."));
            }
            catch (Exception e) { EzCrosshairPlugin.Log.LogWarning("The crosshair folder was not read, the built-in ones are used: " + e.Message); }
        }
    }
#endif

    /// <summary>The crosshair pictures, loaded once each.</summary>
    internal static class Pictures
    {
        private static readonly Dictionary<string, Texture2D> Loaded = new Dictionary<string, Texture2D>();

        public static Texture2D Get(string name)
        {
            Texture2D tex;
            if (Loaded.TryGetValue(name, out tex)) return tex;
            tex = null;
            try
            {
                string data = Images.Get(name);
#if DORSTH_BUNDLE
                string file;
                if (Custom.Files.TryGetValue(name, out file))
                {
                    // exactly as the pictures in the dll are loaded: no smaller copies, sharp pixels. (0.8.0 to 0.9.12 gave a
                    // file's picture smaller copies and smoothing; the game's texture quality setting then drew it from
                    // a half-size copy, which is what broke the brackets.)
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ezCrosshair " + name };
                    if (!ImageConversion.LoadImage(tex, System.IO.File.ReadAllBytes(file)) || tex.width > Custom.Largest || tex.height > Custom.Largest)
                    {
                        EzCrosshairPlugin.Log.LogWarning("Crosshair " + name + " not used: not a picture this can read, or larger than " + Custom.Largest + " pixels.");
                        UnityEngine.Object.Destroy(tex); tex = null;
                    }
                    else { tex.filterMode = FilterMode.Point; UnityEngine.Object.DontDestroyOnLoad(tex); }
                    Loaded[name] = tex;
                    return tex;
                }
#endif
                if (data != null)
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ezCrosshair " + name };
                    if (!ImageConversion.LoadImage(tex, Convert.FromBase64String(data))) { UnityEngine.Object.Destroy(tex); tex = null; }
                    else { tex.filterMode = FilterMode.Point; UnityEngine.Object.DontDestroyOnLoad(tex); }
                }
                else EzCrosshairPlugin.Log.LogWarning("No crosshair called " + name);
            }
            catch (Exception e) { EzCrosshairPlugin.Log.LogError("Couldn't load crosshair " + name + ": " + e.Message); }
            Loaded[name] = tex;
            return tex;
        }
    }

    /// <summary>The crosshair itself: a canvas over the game with one image in the middle of the screen.</summary>
    internal sealed class Overlay : MonoBehaviour
    {
        private GameObject _root;
        private Canvas _canvas;
        private RawImage _image;
        private string _shown;
        private float _scale = -1f;
        private int _screenW, _screenH;

        private void Make()
        {
            _root = new GameObject("ezCrosshair");
            DontDestroyOnLoad(_root);
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.pixelPerfect = true;
            _canvas.sortingOrder = 1;
            var node = new GameObject("Crosshair", typeof(RectTransform));
            node.transform.SetParent(_root.transform, false);
            _image = node.AddComponent<RawImage>();
            _image.raycastTarget = false;
            var rt = _image.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            _image.enabled = false;
            _shown = null;
        }

        // Up to 1.2.2 every frame asked the engine about a dozen things again (the game canvas' order, the picture, the
        // color, the scale, the screen size): about 3 microseconds a frame, measured in a match. Now a frame only works out
        // whether the crosshair shows; the rest is done when something changed (a setting, the hit color going on or off,
        // the crosshair appearing) and looked at again 16 frames later at most (the screen size, the game canvas' order).
        internal static bool Dirty = true;
        private bool _showing, _flash;
        private int _tick;

        private void LateUpdate()
        {
            Hits.Watch();
            bool show = ShouldShow();
            if (_image == null)
            {
                if (!show) return;
                Make();
                _showing = false;
            }
            if (show != _showing)
            {
                _showing = show;
                _image.enabled = show;
                if (show) Dirty = true;
            }
            if (!show) return;
            bool flash = Hits.Flashing();
            if (flash != _flash) { _flash = flash; Dirty = true; }
            if ((++_tick & 15) == 0) Dirty = true;
            if (!Dirty) return;
            Dirty = false;

            // drawn just above the game's own crosshair, under any menu the game puts on top of it
            Canvas game = Crosshair.Instance.image.canvas;
            int order = game != null ? game.rootCanvas.sortingOrder : 0;
            if (_canvas.sortingOrder != order) _canvas.sortingOrder = order;

            Texture2D tex = Pictures.Get(EzCrosshairPlugin.Pick.Value);
            if (tex == null) { _image.enabled = false; _showing = false; return; }
            if (_image.texture != tex) _image.texture = tex;
            Color tint = flash ? EzCrosshairPlugin.HitTint.Value : EzCrosshairPlugin.Tint.Value;
            if (_image.color != tint) _image.color = tint;

            // the bottom left corner on a whole pixel, drawn without smoothing: the pixel art stays sharp
            float scale = Mathf.Clamp(EzCrosshairPlugin.Scale.Value, 0f, 2f);
            if (_shown != EzCrosshairPlugin.Pick.Value || scale != _scale || Screen.width != _screenW || Screen.height != _screenH)
            {
                _shown = EzCrosshairPlugin.Pick.Value;
                _scale = scale;
                _screenW = Screen.width;
                _screenH = Screen.height;
                Vector2Int size = ScreenSize(tex, scale);
                int w = size.x, h = size.y;
                var rt = _image.rectTransform;
                float factor = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
                rt.sizeDelta = new Vector2(w, h) / factor;
                rt.anchoredPosition = new Vector2(Mathf.Floor((Screen.width - w) / 2f), Mathf.Floor((Screen.height - h) / 2f)) / factor;
            }
        }

        // how many screen pixels the crosshair covers: its own size times the scale, on a 1080 high screen
        public static Vector2Int ScreenSize(Texture2D tex, float scale)
        {
            float f = scale * Screen.height / 1080f;
            return new Vector2Int(Mathf.RoundToInt(tex.width * f), Mathf.RoundToInt(tex.height * f));
        }

        private static bool ShouldShow() { return WhyHidden() == null; }

        // null when it shows, otherwise why not
        private static string WhyHidden()
        {
            if (!EzCrosshairPlugin.On.Value) return "turned off";
            Crosshair c = Crosshair.Instance;
            if (c == null || c.image == null) return "no game crosshair yet";
            if (!c.isActiveAndEnabled) return "the game's crosshair is switched off";
            if (c.player == null) return "no player";
            // the game's HUD switched off (its canvas or the crosshair's object): ours goes with it
            if (c.image.canvas == null || !c.image.canvas.isActiveAndEnabled) return "the game's HUD is switched off";
            if (InMainMenu()) return "main menu";
            // the game's own check: a main camera means you're dead or watching, no crosshair then
            if (Camera.main != null) return "dead or watching";
            // looking through a scope: the game swaps its crosshair for the scope then
            bool scoped = c.canScopeAim && c.aimCrosshair != null && c.player.isAiming && c.image.sprite == c.invisibleCrosshair;
            if (scoped && !EzCrosshairPlugin.ShowScoped.Value) return "scoped";
            return null;
        }

        // which scene is the active one is read when it changes (the engine says so), not asked every frame
        private static bool _sceneHooked, _mainMenu;

        public static bool InMainMenu()
        {
            if (!_sceneHooked)
            {
                _sceneHooked = true;
                _mainMenu = SceneManager.GetActiveScene().name == "MainMenu";
                SceneManager.activeSceneChanged += (from, to) => { _mainMenu = to.name == "MainMenu"; };
            }
            return _mainMenu;
        }
    }

    /// <summary>
    /// A hit on someone: the game puts its hit marker on the screen (a copy of the gun's hitMarker under the pause
    /// manager) for every weapon, so a new one there is a hit. The crosshair then shows the hit color for a moment.
    /// </summary>
    internal static class Hits
    {
        private static readonly Type[] Holders = { typeof(Weapon), typeof(PredictedProjectile), typeof(ProximityMine), typeof(ShrapnelBallistic) };

        private static void ReadNames()
        {
            foreach (var type in Holders)
            {
                var f = type.GetField("hitMarker", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (f == null) continue;
                foreach (var o in Resources.FindObjectsOfTypeAll(type))
                {
                    var g = o != null ? f.GetValue(o) as GameObject : null;
                    if (g != null) MarkerNames.Add(g.name + "(Clone)");
                }
            }
        }

        private const float Flash = 0.2f;
        private static float _until = -1f;
        private static readonly HashSet<string> MarkerNames = new HashSet<string>();
        private static readonly HashSet<string> NotMarkers = new HashSet<string>();
        private static bool _hooked;
        private static float _namesAt = -100f;
        private static int _lastId;

        public static bool Flashing() { return EzCrosshairPlugin.HitOn.Value && Time.unscaledTime < _until; }

        public static void Watch()
        {
            if (!EzCrosshairPlugin.HitOn.Value) return;
            try
            {
                var pm = PauseManager.Instance;
                if (pm == null) return;
                Transform t = pm.transform;
                int n = t.childCount;
                if (n == 0) return;
                // a new marker goes last
                Transform last = t.GetChild(n - 1);
                int id = last.GetInstanceID();
                if (id == _lastId) return;
                _lastId = id;
                string name = last.name;
                if (!name.EndsWith("(Clone)", StringComparison.Ordinal)) return;
                if (!_hooked) { _hooked = true; UnityEngine.SceneManagement.SceneManager.sceneLoaded += (sc, m) => NotMarkers.Clear(); }
                // the hit markers' names, from everything loaded that has one (guns, grenades, mines, rockets). Read again
                // when something new shows up that isn't one yet: a gun loaded after the last read (the list read in the
                // main menu had none, so the colour only came once something else read it again)
                // Up to 1.2.1 every new thing under the pause manager that was not a hit marker (a chat line, a kill line) started
                // that read again, and the read looks through everything loaded, four times: a few milliseconds each time.
                // Now a name that turned out not to be a marker is remembered until the next map.
                if (!MarkerNames.Contains(name))
                {
                    if (NotMarkers.Contains(name) || Time.unscaledTime - _namesAt <= 0.5f) return;
                    _namesAt = Time.unscaledTime;
                    ReadNames();
                    if (!MarkerNames.Contains(name)) { NotMarkers.Add(name); return; }
                }
                _until = Time.unscaledTime + Flash;
            }
            catch { }
        }
    }
}
