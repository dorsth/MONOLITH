// DorsthTweaks - a STRAFTAT mod (BepInEx 5). Dorsth was here.
//
// Client-side tweaks, each with its own setting in the in-game Mod Menu. Optional hooks are only attached while their
// setting is on, so a setting that's off costs nothing. Nothing is sent over the network.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using ComputerysModdingUtilities;
using DG.Tweening;
using FishNet.Object;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityStandardAssets.Effects;
using VLB;
using Water = UnityStandardAssets.Water.Water;
using PP = UnityEngine.Rendering.PostProcessing;
using WaterBasic = UnityStandardAssets.Water.WaterBasic;
using PlanarReflection = UnityStandardAssets.Water.PlanarReflection;

[assembly: AssemblyTitle("DorsthTweaks")]
[assembly: AssemblyDescription("Hitmarker, HUD, viewmodel and FPS tweaks for STRAFTAT.")]
[assembly: AssemblyVersion("1.3.1.0")]
[assembly: AssemblyFileVersion("1.2.7.0")]

// Only changes what you see on your own screen, so matchmaking with unmodded players still works.
[assembly: StraftatMod(isVanillaCompatible: true)]

namespace DorsthTweaks
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("kestrel.straftat.modmenu", BepInDependency.DependencyFlags.SoftDependency)]     // Mod Menu first, when it's there: MenuLinks hooks into it
    public sealed class DorsthTweaksPlugin : BaseUnityPlugin
    {
        public const string Guid = "dorsth.straftat.dorsthtweaks";
        public const string Name = "DorsthTweaks";
        public const string Version = "1.3.1";

        internal static ManualLogSource Log;
        internal static Harmony Harmony;

        internal static ConfigEntry<bool> StaticHitmarkers, HideHitmarkers, HeadPopLikeBody;
        internal static ConfigEntry<float> HitmarkerGap, HitmarkerLength, HitmarkerWidth, HitmarkerPop;
        internal enum LineEnds { Default, Squared, Rounded, Spiked }
        internal static ConfigEntry<LineEnds> HitmarkerEnds;
        internal static ColorSetting BodyshotColor, HeadshotColor, ShotgunHitmarkerColor, BulletTrailColor;
        internal static ConfigEntry<bool> NoViewmodelClimb;
        internal static ConfigEntry<bool> NoBulletCasings;
        internal static ConfigEntry<bool> HideMyTrails, HideOthersTrails;
        internal static ConfigEntry<float> TrailWidth, TrailOpacity;
        internal static ConfigEntry<bool> HideFps, HidePing, HideNearMissVignette;
        internal static ColorSetting MinimalHudColor;
        internal static ColorSetting WeaponHighlightColor, PromptColor;
        internal static ConfigEntry<bool> HidePickupPrompts, HideInteractionPrompts, HideKeyPrompt;
        internal static ConfigEntry<float> PromptX, PromptY, PromptScale;
        internal enum SlideTimerStyle { Off, Text, Bar }
        internal static ConfigEntry<SlideTimerStyle> SlideTimer;
        internal static ConfigEntry<bool> ReduceUIAnimations;
        internal static ConfigEntry<bool> DisableRain, StaticTextures, WarmUpEffects, ReduceEffects, FewerEffects, NoPostProcessing, UltimateTweaksOn;

        private static void FromPerformance()
        {
            bool on = FewerEffects.Value;
            // each fires its own change (and what it hooks) only if it changed
            if (DisableRain.Value != on) DisableRain.Value = on;
            if (StaticTextures.Value != on) StaticTextures.Value = on;
            if (WarmUpEffects.Value != on) WarmUpEffects.Value = on;
            if (ReduceEffects.Value != on) ReduceEffects.Value = on;
        }

        // Reduce VFX+ is applied 0.3 s after its last change, once: flicking it on and off in Mod Menu would otherwise
        // run the whole switch (a look over the map, every hook) for every click.
        private static float _fewerAt;
        private static bool _fewerWaiting;

        private static System.Collections.IEnumerator FewerLater()
        {
            while (Time.unscaledTime < _fewerAt) yield return null;
            _fewerWaiting = false;
            try { FromPerformance(); }
            catch (Exception e) { LogErrorOnce(e); }
        }

        internal static ConfigEntry<int> LowerTextures;
        internal static ConfigEntry<bool> HideDefaultPlaylists;
        internal static ConfigEntry<string> JoinMessage;

        // Awake runs before Unity has loaded a scene: only settings and hooks here. Anything that touches scenes, materials
        // or render settings waits for a scene to load (reading the skybox this early crashes the game).
#if !DORSTH_BUNDLE
        // Mod Menu (if installed) shows this next to the mod. Its own method, so nothing of Mod Menu is needed without it.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ModMenuDescription() { ModMenu.Api.ModMenuCustomisation.SetPluginDescription("Hitmarkers, bullet trails, HUD colors, prompts and viewmodel. More FPS: Reduce VFX+, No Post-Processing, Lower Textures and Ultimate Tweaks."); }
#endif

        private void Awake()
        {
            Log = Logger;
            Log.LogMessage("Dorsth was here");
            // BepInEx's manager object (every plugin's home) is a plain object the game destroys on the way into a lobby,
            // unless something hides it (Mod Menu does, so does BepInEx's HideManagerGameObject). Hidden here too.
            gameObject.hideFlags = HideFlags.HideAndDontSave;

            // The settings file is written once, after every setting is bound and old values are carried over, and not
            // once per setting. A file that can't be written (read-only, locked) must not stop the mod from starting.
            Config.SaveOnConfigSet = false;
            bool bound = false;
            try { BindSettings(); bound = true; }
            catch (Exception e) { Log.LogError("Settings: " + e); }
            if (bound)
            {
                try { ForgetOld(); Config.Save(); }
                catch (Exception e) { Log.LogWarning("Couldn't write the settings file (read-only or in use?): " + e.Message); }
            }
            Config.SaveOnConfigSet = true;

            Harmony = new Harmony(Guid);
            Install("effect prefabs", EffectPrefabs.Install);
            Install("hitmarkers", Hitmarkers.Install);
            Install("neck hitmarkers", NeckHits.Install);
            Install("log spam", QuietLog.Install);
            Install("bullet trails", BulletTrails.Install);
            Install("viewmodel climb", ViewmodelClimb.Install);
            Install("bullet casings", BulletCasings.Install);
            Install("FPS/ping", Stats.Install);
            Install("HUD and prompt colors", TextColors.Install);
            Install("key prompt", KeyPrompt.Install);
            Install("slide cooldown timer", SlideClock.Install);
            Install("weapon highlight", WeaponHighlight.Install);
            Install("near miss vignette", NearMiss.Install);
            Install("idle camera effect", IdleCameraEffect.Install);
            Install("reduce UI animations", StaticUI.Install);
            Install("rain and static textures", World.Install);
            Install("playlists", Playlists.Install);
            Install("join message", JoinLine.Install);
            Install("warm up effects", WarmUp.Install);
            Install("reuse effects", ShotEffects.Install);
            Install("reduce effects", Effects.Install);
            Install("quality settings", Quality.Install);
            Install("performance", Perf.Install);
            Install("mod menu links", MenuLinks.Install);
            Install("Ultimate Tweaks", () => UltimateTweaks.TweaksPlugin.Create(gameObject, UltimateTweaksOn, Logger));
            GameHook.RefreshAll();
            if (GameHook.Missing.Count > 0)
                Log.LogWarning("Not in this game version, skipped: " + string.Join(", ", GameHook.Missing.ToArray()));
#if !DORSTH_BUNDLE
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("kestrel.straftat.modmenu"))
            { try { ModMenuDescription(); } catch (Exception e) { Logger.LogInfo("Mod Menu description: " + e.Message); } }
#endif
        }

        // Sections in the order Mod Menu shows them (A to Z), settings in the order they're bound. A setting that was
        // renamed or moved takes its value from the old line in the settings file (Old, Carry).
        private void BindSettings()
        {
            const string Trails = "Bullet Trails", Hud = "HUD", Use = "Interaction", Menus = "Menus", View = "Viewmodel", Map = "World";

            HideMyTrails = Config.Bind(Trails, "Hide My Trails", false, "Hides the trails of your own shots.");
            HideOthersTrails = Config.Bind(Trails, "Hide Other Players Trails", false, "Hides the trails of other players' shots.");
            BulletTrailColor = new ColorSetting(Config, Trails, "Trail Color", new Color32(255, 255, 255, 255), "White keeps each gun's own.");
            TrailWidth = Config.Bind(Trails, "Trail Width", 1f,
                new ConfigDescription("Thickness. 1 is the game's.", new AcceptableValueRange<float>(0.2f, 3f)));
            TrailOpacity = Config.Bind(Trails, "Trail Opacity", 1f,
                new ConfigDescription("How solid. 1 is the game's, lower is fainter.", new AcceptableValueRange<float>(0.1f, 1f)));
            // an old No Bullet Trails setting hid everyone's trails
            if (IsTrue(Old(View, "No Bullet Trails"))) { HideMyTrails.Value = true; HideOthersTrails.Value = true; }
            Carry(BulletTrailColor.Entry, View, "Bullet Trail Color");

            HideHitmarkers = Config.Bind(Hud, "Hide Hitmarkers", false, "No hitmarkers.");
            string oldEnds = Old(Hud, "Hitmarker Shape");
            HitmarkerEnds = Config.Bind(Hud, "Hitmarker Shape", LineEnds.Default, "Line ends. Default is the game's own.");
            if (oldEnds != null && oldEnds.Trim() == "SpikedInward") HitmarkerEnds.Value = LineEnds.Spiked;
            if (!Enum.IsDefined(typeof(LineEnds), HitmarkerEnds.Value)) HitmarkerEnds.Value = LineEnds.Default;
            // an old length of 0 hid hitmarkers: that is Hide Hitmarkers, and the length starts at 0.1
            string oldLength = Old(Hud, "Hitmarker Length");
            HitmarkerLength = Config.Bind(Hud, "Hitmarker Length", 1f,
                new ConfigDescription("Line length. 1 is the game's.", new AcceptableValueRange<float>(0.1f, 3f)));
            if (oldLength != null && float.TryParse(oldLength, NumberStyles.Float, CultureInfo.InvariantCulture, out float length) && length < 0.1f)
            {
                HideHitmarkers.Value = true;
                HitmarkerLength.Value = 1f;
            }
            HitmarkerWidth = Config.Bind(Hud, "Hitmarker Width", 1f,
                new ConfigDescription("Line thickness. 1 is the game's.", new AcceptableValueRange<float>(0.2f, 3f)));
            HitmarkerGap = Config.Bind(Hud, "Hitmarker Gap", 1f,
                new ConfigDescription("Gap in the middle. 1 is the game's.", new AcceptableValueRange<float>(0f, 3f)));
            // an old Hitmarker Scale setting became Length and Width (a scale of 0 hid them)
            string oldScale = Old(Hud, "Hitmarker Scale");
            if (oldScale != null && float.TryParse(oldScale, NumberStyles.Float, CultureInfo.InvariantCulture, out float was)
                && !float.IsNaN(was) && !float.IsInfinity(was))
            {
                if (was <= 0.001f) HideHitmarkers.Value = true;
                else { HitmarkerLength.Value = Mathf.Clamp(was, 0.1f, 3f); HitmarkerWidth.Value = Mathf.Clamp(was, 0.2f, 3f); }
            }
            StaticHitmarkers = Config.Bind(Hud, "Static Hitmarkers", false, "No animation: hitmarkers stay still and fade out.");
            // a pop of (nearly) 0 was "no animation": that's Static Hitmarkers
            string oldPop = Old(Hud, "Hitmarker Animation") ?? Old(Hud, "Hitmarker Pop");
            HitmarkerPop = Config.Bind(Hud, "Hitmarker Animation", 1f,
                new ConfigDescription("How far hitmarkers pop. 1 is the game's.", new AcceptableValueRange<float>(0.1f, 1f)));
            if (oldPop != null && float.TryParse(oldPop, NumberStyles.Float, CultureInfo.InvariantCulture, out float pop) && !float.IsNaN(pop))
            {
                if (pop < 0.05f) { StaticHitmarkers.Value = true; HitmarkerPop.Value = 1f; }
                else HitmarkerPop.Value = Mathf.Clamp(pop, 0.1f, 1f);
            }
            HeadPopLikeBody = Config.Bind(Hud, "Same Headshot Animation", false, "Headshots animate like bodyshots (a smaller pop).");
            Carry(HeadPopLikeBody, Hud, "Small Headshot Pop");
            BodyshotColor = new ColorSetting(Config, Hud, "Hitmarker Bodyshot Color", new Color32(255, 255, 255, 255), "Bodyshot hitmarkers. The default is the game's.");
            HeadshotColor = new ColorSetting(Config, Hud, "Hitmarker Headshot Color", new Color32(255, 0, 0, 255), "Headshot hitmarkers. The default is the game's.");
            ShotgunHitmarkerColor = new ColorSetting(Config, Hud, "Hitmarker Shotgun Color", new Color32(0, 0, 0, 255),
                "The black bodyshot hitmarker of the shotgun and a few other guns. The default is the game's.");
            MinimalHudColor = new ColorSetting(Config, Hud, "Minimal HUD Color", new Color32(255, 255, 255, 255), "Minimal HUD, speedometer and slide cooldown timer.");
            HideFps = Config.Bind(Hud, "Hide FPS", false, "Hides the FPS counter.");
            HidePing = Config.Bind(Hud, "Hide Ping", false, "Hides the ping counter.");
            HideNearMissVignette = Config.Bind(Hud, "Hide Near Miss Vignette", false, "Hides the dark edges when shots land near you.");
            SlideTimer = Config.Bind(Hud, "Slide Cooldown Timer", SlideTimerStyle.Off, "Time until you can slide again, under the crosshair: as text or a bar.");
            if (!Enum.IsDefined(typeof(SlideTimerStyle), SlideTimer.Value)) SlideTimer.Value = SlideTimerStyle.Off;

            HidePickupPrompts = Config.Bind(Use, "Hide Pickup Prompts", false, "Hides prompts on weapons.");
            HideInteractionPrompts = Config.Bind(Use, "Hide Interaction Prompts", false, "Hides prompts on doors and buttons.");
            HideKeyPrompt = Config.Bind(Use, "Hide Key Prompt", false, "Hides the key, like [f], after a prompt.");
            PromptColor = new ColorSetting(Config, Use, "Prompt Color", new Color32(255, 141, 0, 255), "Prompts on weapons, doors and buttons. The default is the game's.");
            PromptScale = Config.Bind(Use, "Prompt Scale", 1f, new ConfigDescription("Prompt size. 1 is the game's.", new AcceptableValueRange<float>(0.5f, 2f)));
            PromptX = Config.Bind(Use, "Prompt X Offset", 0f, new ConfigDescription("Moves prompts left or right.", new AcceptableValueRange<float>(-600f, 600f)));
            PromptY = Config.Bind(Use, "Prompt Y Offset", 0f, new ConfigDescription("Moves prompts down or up.", new AcceptableValueRange<float>(-400f, 400f)));
            WeaponHighlightColor = new ColorSetting(Config, Use, "Weapon Highlight Color", new Color32(255, 209, 109, 255), "Outline of the weapon you look at. The default is the game's.");

            JoinMessage = Config.Bind(Menus, "Join Message", "",
                "Replaces your 'joined the lobby' line, for everyone in the lobby. {name} is your name. Empty uses the game's.");
            Carry(JoinMessage, "Lobby", "Join Message");
            HideDefaultPlaylists = Config.Bind(Menus, "Hide Default Playlists", false, "Hides the game's playlists.");
#if DORSTH_BUNDLE
            // in MONOLITH this also ends the engine's logo screen at start (this runs while it shows; nothing happens if it is over)
            ReduceUIAnimations = Config.Bind(Menus, "Reduce UI Animations", false, "No animations in the menus and on the HUD, no logo screen when the game starts.");
            if (ReduceUIAnimations.Value) { try { UnityEngine.Rendering.SplashScreen.Stop(UnityEngine.Rendering.SplashScreen.StopBehavior.StopImmediate); } catch { } }
#else
            ReduceUIAnimations = Config.Bind(Menus, "Reduce UI Animations", false, "No animations in the menus and on the HUD.");
#endif

            NoViewmodelClimb = Config.Bind(View, "No Viewmodel Climb", false, "Your gun stays in place while spraying.");
            NoBulletCasings = Config.Bind(View, "No Bullet Casings", false, "No casings or their sound.");

            // Reduce VFX+ stands for four older settings (Disable Rain, Static Textures, Warm Up Effects, Reduce
            // Effects). The code still reads those four; they live in a settings file of their own that is never written
            // and are set from Reduce VFX+.
            FewerEffects = Config.Bind(Map, "Reduce VFX+", false,
                "More FPS. No shadows, rain, light rays, anti-aliasing, screen effects (color grading stays) or kill grey-out. Fewer lights and particles, still water, simpler far models, no stutter on your first hit.");
            // forward rendering and the soft fill light come with this setting (Ultimate Tweaks' Visual part)
            NoPostProcessing = Config.Bind(Map, "No Post-Processing", false,
                "More FPS. Every screen effect is off (color grading, bloom, ambient occlusion, vignette, anti-aliasing), the menus' blur too, rendering is plainer, and a soft light takes the place of the brightness they gave. The game's brightness slider does nothing then.");
            // Ultimate Tweaks: every fix in it is this one setting
            UltimateTweaksOn = Config.Bind(Map, "Ultimate Tweaks", true,
                "More FPS and fewer stutters: about 30 fixes to the game's own code (garbage, needless work every frame, the voice key freeze, code compiled at start, fewer worker threads). Nothing you see, hear or send changes.");
            if (IsTrue(Old(Map, "Reduce Effects")) || IsTrue(Old(Map, "Performance Mode"))
                || IsTrue(Old(Map, "Fewer Effects")) || IsTrue(Old(Map, "Reuse Effects"))) FewerEffects.Value = true;
            var inside = new ConfigFile(Path.Combine(Paths.CachePath, Guid + ".inside.cfg"), false) { SaveOnConfigSet = false };
            DisableRain = inside.Bind(Map, "Disable Rain", false, "");
            StaticTextures = inside.Bind(Map, "Static Textures", false, "");
            WarmUpEffects = inside.Bind(Map, "Warm Up Effects", false, "");
            ReduceEffects = inside.Bind(Map, "Reduce Effects", false, "");
            FromPerformance();
            Host.Recreated += () => _fewerWaiting = false;      // the object the wait ran on is gone, and the wait with it
            FewerEffects.SettingChanged += (sender, args) =>
            {
                _fewerAt = Time.unscaledTime + 0.3f;
                if (!_fewerWaiting)
                {
                    _fewerWaiting = true;
                    Host.Run(FewerLater());
                }
            };
            LowerTextures = Config.Bind(Map, "Lower Textures", 0, new ConfigDescription(
                "Smaller textures on maps, guns and players. 0 is off, 3 is the smallest.", new AcceptableValueRange<int>(0, 3)));

            // names these settings had in test builds
            Carry(HeadPopLikeBody, Hud, "Headshots Pop Like Bodyshots");
            Carry(HideMyTrails, View, "Hide My Bullet Trails");
            Carry(HideOthersTrails, View, "Hide Other Players Bullet Trails");
            Carry(TrailWidth, View, "Bullet Trail Width");
            Carry(TrailOpacity, View, "Bullet Trail Opacity");
        }

        private static bool IsTrue(string v)
        {
            return string.Equals(v != null ? v.Trim() : null, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Gives a setting the value its old line in the settings file had, if there is one.</summary>
        private void Carry(ConfigEntryBase now, string section, string key)
        {
            string was = Old(section, key);
            if (was == null) return;
            try { now.SetSerializedValue(was); }
            catch { }
        }

        private static void Install(string feature, Action install)
        {
            try { install(); }
            catch (Exception e) { Log.LogWarning(feature + " is off: it doesn't fit this game version. (" + e.Message + ")"); }
        }

        // Every place that can fail logs its first error, with where it was: one feature's error doesn't hide another's,
        // and an error that comes back every frame is one line in the log.
        private static readonly HashSet<string> ErrorsLogged = new HashSet<string>();

        internal static void LogErrorOnce(Exception e, [CallerMemberName] string where = "", [CallerLineNumber] int line = 0)
        {
            if (ErrorsLogged.Count > 64 || !ErrorsLogged.Add(where + ":" + line)) return;
            Log.LogError("Error in " + where + " (logged once): " + e);
        }

        /// <summary>When a setting changes: update the hooks first, then apply the change.</summary>
        internal static void OnChange(ConfigEntry<bool> setting, Action apply)
        {
            setting.SettingChanged += (sender, args) =>
            {
                GameHook.RefreshAll();
                try { apply(); }
                catch (Exception e) { LogErrorOnce(e); }
            };
        }

        private static Dictionary<ConfigDefinition, string> _old;

        /// <summary>Lines in the settings file no setting claims (what's left of an older layout). BepInEx keeps them.</summary>
        private Dictionary<ConfigDefinition, string> Leftovers()
        {
            if (_old == null)
            {
                try { _old = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(Config, null) as Dictionary<ConfigDefinition, string>; }
                catch (Exception e) { Log.LogWarning("Couldn't read the old settings: " + e.Message); }
            }
            return _old;
        }

        /// <summary>A setting's value from an older layout, or null.</summary>
        internal string Old(string section, string key)
        {
            var left = Leftovers();
            string value;
            return left != null && left.TryGetValue(new ConfigDefinition(section, key), out value) ? value : null;
        }

        /// <summary>Drops the old layout's lines (and removed settings) from the settings file. Awake saves it.</summary>
        private void ForgetOld()
        {
            var left = Leftovers();
            if (left == null || left.Count == 0) return;
            left.Clear();
        }


        internal static MethodInfo Target(Type type, string name)
        {
            MethodInfo m = AccessTools.Method(type, name);
            if (m == null) throw new MissingMethodException(type.Name, name);
            return m;
        }

        internal static HarmonyMethod Patch(Type type, string name)
        {
            return new HarmonyMethod(Target(type, name));
        }
    }

    /// <summary>A color setting (a hex box in Mod Menu). At its default the game's own color is left alone.</summary>
    internal sealed class ColorSetting
    {
        private readonly ConfigEntry<Color> _entry;
        private readonly Color32 _default;

        internal ColorSetting(ConfigFile config, string section, string key, Color32 defaultValue, string description)
        {
            _default = defaultValue;
            _entry = config.Bind(section, key, (Color)defaultValue, description);
            Read();
            _entry.SettingChanged += (sender, args) =>
            {
                Read();
                Action changed = Changed;
                if (changed == null) return;
                try { changed(); }
                catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
            };
        }

        internal event Action Changed;
        internal ConfigEntryBase Entry { get { return _entry; } }
        internal Color Color { get; private set; }

        internal bool IsDefault { get; private set; }

        private void Read()
        {
            Color = _entry.Value;
            Color32 c = Color;
            IsDefault = c.r == _default.r && c.g == _default.g && c.b == _default.b && c.a == _default.a;
        }

        /// <summary>This color in place of one the game drew: just as bright (HDR glow), with its opacity times this one's.</summary>
        internal Color Like(Color original)
        {
            float boost = Mathf.Max(1f, Mathf.Max(original.r, Mathf.Max(original.g, original.b)));
            Color c = Color;
            return new Color(c.r * boost, c.g * boost, c.b * boost, c.a * original.a);
        }
    }

    /// <summary>A hook that's only attached while its setting is on. A method missing in this game version is skipped.</summary>
    internal sealed class GameHook
    {
        private static readonly List<GameHook> All = new List<GameHook>();
        internal static readonly List<string> Missing = new List<string>();

        private readonly MethodBase _target;
        private readonly HarmonyMethod _prefix, _postfix, _finalizer;
        private readonly Func<bool> _needed;
        private bool _on;

        private GameHook(MethodBase target, HarmonyMethod prefix, HarmonyMethod postfix, HarmonyMethod finalizer, Func<bool> needed)
        {
            _target = target;
            _prefix = prefix;
            _postfix = postfix;
            _finalizer = finalizer;
            _needed = needed;
        }

        internal static void Add(Type type, string method, Type[] args, Type patches, string prefix, string postfix, Func<bool> needed,
            int prefixPriority = -1, int postfixPriority = -1, string finalizer = null)
        {
            MethodInfo target = args == null ? AccessTools.Method(type, method) : AccessTools.Method(type, method, args);
            if (target == null)
            {
                Missing.Add(type.Name + "." + method);
                return;
            }
            HarmonyMethod pre = prefix == null ? null : DorsthTweaksPlugin.Patch(patches, prefix);
            HarmonyMethod post = postfix == null ? null : DorsthTweaksPlugin.Patch(patches, postfix);
            if (pre != null && prefixPriority >= 0) pre.priority = prefixPriority;
            if (post != null && postfixPriority >= 0) post.priority = postfixPriority;
            HarmonyMethod last = finalizer == null ? null : DorsthTweaksPlugin.Patch(patches, finalizer);
            All.Add(new GameHook(target, pre, post, last, needed));
        }

        internal static void RefreshAll()
        {
            foreach (GameHook hook in All) hook.Refresh();
        }

        private void Refresh()
        {
            bool want = _needed();
            if (want == _on) return;
            try
            {
                if (want) DorsthTweaksPlugin.Harmony.Patch(_target, _prefix, _postfix, null, _finalizer, null);
                else
                {
                    if (_prefix != null) DorsthTweaksPlugin.Harmony.Unpatch(_target, _prefix.method);
                    if (_postfix != null) DorsthTweaksPlugin.Harmony.Unpatch(_target, _postfix.method);
                    if (_finalizer != null) DorsthTweaksPlugin.Harmony.Unpatch(_target, _finalizer.method);
                }
                _on = want;
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }
    }

    /// <summary>
    /// Where DorsthTweaks' clocks and coroutines live: its own hidden object, kept across scenes. Not the plugin's own
    /// object: without Mod Menu the game destroys BepInEx's manager object on the way into a lobby, and everything on
    /// it stopped (a coroutine started then even threw inside the game's scene loading).
    /// </summary>
    /// <summary>
    /// Every script the game has in memory (in the map and the prefabs behind it), looked up once per frame and sorted by
    /// kind. After a map loads, six parts of this mod each asked the engine for "every Weapon" (or player, or item) on
    /// their own, and each such question walks through everything in memory (1.2 to 1.8 ms each, about 20 of them per
    /// map, measured). Now they all read from this one list. Nothing is kept past the frame.
    /// </summary>
    internal static class Found
    {
        private static MonoBehaviour[] _all;
        private static Dictionary<Type, List<MonoBehaviour>> _byType;
        private static int _frame = -1;
        private static bool _forgetQueued;

        internal static MonoBehaviour[] All()
        {
            int frame = Time.frameCount;
            if (_all == null || frame != _frame)
            {
                _all = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
                _byType = null;
                _frame = frame;
                if (!_forgetQueued) { _forgetQueued = true; try { Host.Run(ForgetLater()); } catch { _forgetQueued = false; } }
            }
            return _all;
        }

        private static System.Collections.IEnumerator ForgetLater()
        {
            // a held list would keep a gone map's scripts in memory
            yield return null;
            yield return null;
            _forgetQueued = false;
            if (Time.frameCount != _frame) { _all = null; _byType = null; }
        }

        private static Dictionary<Type, List<MonoBehaviour>> ByType()
        {
            MonoBehaviour[] all = All();
            if (_byType != null) return _byType;
            var map = new Dictionary<Type, List<MonoBehaviour>>();
            foreach (MonoBehaviour b in all)
            {
                if ((object)b == null) continue;
                Type t = b.GetType();
                List<MonoBehaviour> list;
                if (!map.TryGetValue(t, out list)) map[t] = list = new List<MonoBehaviour>();
                list.Add(b);
            }
            return _byType = map;
        }

        /// <summary>Every loaded script of this kind or a kind made from it, the same set Resources.FindObjectsOfTypeAll gives.</summary>
        internal static List<T> Of<T>() where T : MonoBehaviour
        {
            var result = new List<T>();
            foreach (var kv in ByType())
            {
                if (!typeof(T).IsAssignableFrom(kv.Key)) continue;
                foreach (MonoBehaviour b in kv.Value) if (b != null) result.Add((T)b);
            }
            return result;
        }

        internal static List<MonoBehaviour> Of(Type kind)
        {
            var result = new List<MonoBehaviour>();
            foreach (var kv in ByType())
            {
                if (!kind.IsAssignableFrom(kv.Key)) continue;
                foreach (MonoBehaviour b in kv.Value) if (b != null) result.Add(b);
            }
            return result;
        }
    }

    internal static class Host
    {
        private sealed class Runner : MonoBehaviour { }
        private static GameObject _go;
        private static Runner _run;
        // what was added to the object, to put back if something destroys it; Recreated tells whoever had a coroutine on it
        private static readonly List<Type> Added = new List<Type>();
        internal static event Action Recreated;

        private static GameObject Go()
        {
            if (_go == null)
            {
                _go = new GameObject("DorsthTweaks");
                _go.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(_go);
                _run = _go.AddComponent<Runner>();
                if (Added.Count > 0)
                {
                    foreach (Type type in Added) _go.AddComponent(type);
                    Action recreated = Recreated;
                    if (recreated != null)
                    {
                        try { recreated(); }
                        catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
                    }
                }
            }
            return _go;
        }

        internal static Coroutine Run(System.Collections.IEnumerator routine) { Go(); return _run.StartCoroutine(routine); }

        internal static T Add<T>() where T : Component
        {
            GameObject go = Go();
            T have = go.GetComponent<T>();
            if (!Added.Contains(typeof(T))) Added.Add(typeof(T));
            return have != null ? have : go.AddComponent<T>();
        }
    }

    internal static class Scenes
    {
        internal const string MainMenu = "MainMenu";
        // asked for every scene, many times a frame: reading a scene's name makes a new string each time
        private static readonly Dictionary<int, bool> MenuScenes = new Dictionary<int, bool>();

        /// <summary>
        /// Every DorsthTweaks scene loaded handler goes through here, each in its own try: one that throws inside
        /// Unity's sceneLoaded skips every handler after it, the game's and FishNet's map setup included.
        /// </summary>
        internal static event Action<Scene, LoadSceneMode> Loaded;

        static Scenes() { SceneManager.sceneLoaded += Dispatch; }

        private static void Dispatch(Scene scene, LoadSceneMode mode)
        {
            Action<Scene, LoadSceneMode> all = Loaded;
            if (all == null) return;
            foreach (Delegate d in all.GetInvocationList())
            {
                try { ((Action<Scene, LoadSceneMode>)d)(scene, mode); }
                catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
            }
        }

        /// <summary>The main menu, or DontDestroyOnLoad where the game keeps its pause menu, options and HUD.</summary>
        internal static bool IsMenu(Scene scene)
        {
            int handle = scene.handle;
            bool menu;
            if (MenuScenes.TryGetValue(handle, out menu)) return menu;
            string name = scene.name;
            menu = name == MainMenu || name == "DontDestroyOnLoad";
            if (MenuScenes.Count > 64) MenuScenes.Clear();
            MenuScenes[handle] = menu;
            return menu;
        }

        internal static bool InMainMenu()
        {
            PauseManager pm = PauseManager.Instance;
            return pm != null && pm.inMainMenu;
        }

        /// <summary>Every object of a type in the loaded scenes, inactive ones too. Only run on a scene load or a setting change.</summary>
        internal static UnityEngine.Object[] FindAll(Type type)
        {
            return UnityEngine.Object.FindObjectsOfType(type, true);
        }

        /// <summary>Forgets saved objects that were destroyed.</summary>
        internal static void PruneGone<TKey, TValue>(Dictionary<TKey, TValue> saved) where TKey : UnityEngine.Object
        {
            var gone = new List<TKey>();
            foreach (TKey key in saved.Keys)
                if (key == null) gone.Add(key);
            foreach (TKey key in gone) saved.Remove(key);
        }
    }

    /// <summary>
    /// In Mod Menu, hides the settings that do nothing while another one is on (the hitmarker settings while hitmarkers
    /// are hidden, the trail color while both kinds of trails are). Mod Menu has no way to ask for this, so its option
    /// list is read by reflection: every part is looked up by name, and if one is missing nothing is hidden.
    /// </summary>
    internal static class MenuLinks
    {
        private struct Rule { public Func<bool> When; public ConfigEntryBase[] Hides; }
        private static readonly List<Rule> Rules = new List<Rule>();

        private static Type _panel, _boxed;
        private static FieldInfo _cache, _current, _source, _search;
        private static PropertyInfo _entry;
        // the setting behind each row of the list (rows are made once and kept by Mod Menu)
        private static readonly Dictionary<int, ConfigEntryBase> RowEntry = new Dictionary<int, ConfigEntryBase>();

        internal static void Install()
        {
            _panel = AccessTools.TypeByName("ModMenu.Behaviours.OptionList.OptionListPanel");
            if (_panel == null) return;     // no Mod Menu
            _boxed = AccessTools.TypeByName("ModMenu.Behaviours.OptionList.ValueControllers.BoxedValueController");
            Type option = AccessTools.TypeByName("ModMenu.Options.Option");
            _cache = AccessTools.Field(_panel, "m_optionCache");
            _current = AccessTools.Field(_panel, "m_currentEnabledGuid");
            _search = AccessTools.Field(_panel, "searchBar");
            _source = _boxed != null ? AccessTools.Field(_boxed, "sourceOption") : null;
            _entry = option != null ? AccessTools.Property(option, "BaseEntry") : null;
            if (_boxed == null || _cache == null || _current == null || _source == null || _entry == null) return;

            ConfigEntryBase[] hitmarkers =
            {
                DorsthTweaksPlugin.HitmarkerEnds, DorsthTweaksPlugin.HitmarkerLength, DorsthTweaksPlugin.HitmarkerWidth,
                DorsthTweaksPlugin.HitmarkerGap, DorsthTweaksPlugin.StaticHitmarkers, DorsthTweaksPlugin.HitmarkerPop,
                DorsthTweaksPlugin.HeadPopLikeBody, DorsthTweaksPlugin.BodyshotColor.Entry, DorsthTweaksPlugin.HeadshotColor.Entry,
                DorsthTweaksPlugin.ShotgunHitmarkerColor.Entry,
            };
            Add(() => DorsthTweaksPlugin.HideHitmarkers.Value, hitmarkers);
            Add(() => DorsthTweaksPlugin.StaticHitmarkers.Value, DorsthTweaksPlugin.HitmarkerPop, DorsthTweaksPlugin.HeadPopLikeBody);
            Add(() => DorsthTweaksPlugin.HideMyTrails.Value && DorsthTweaksPlugin.HideOthersTrails.Value,
                DorsthTweaksPlugin.BulletTrailColor.Entry, DorsthTweaksPlugin.TrailWidth, DorsthTweaksPlugin.TrailOpacity);

            // the list is filtered again when one of the settings that hide others changes
            ConfigEntryBase[] switches =
            {
                DorsthTweaksPlugin.HideHitmarkers, DorsthTweaksPlugin.StaticHitmarkers,
                DorsthTweaksPlugin.HideMyTrails, DorsthTweaksPlugin.HideOthersTrails,
            };
            foreach (ConfigEntryBase e in switches)
                e.ConfigFile.SettingChanged += (sender, args) => { if (args.ChangedSetting == e) Refresh(); };

            HarmonyMethod shown = DorsthTweaksPlugin.Patch(typeof(MenuLinks), nameof(ShownPostfix));
            DorsthTweaksPlugin.Harmony.Patch(AccessTools.Method(_panel, "ShowListFor"), postfix: shown);
            DorsthTweaksPlugin.Harmony.Patch(AccessTools.Method(_panel, "FilterOptionList"), postfix: shown);
        }

        private static void Add(Func<bool> when, params ConfigEntryBase[] hides)
        {
            Rules.Add(new Rule { When = when, Hides = hides });
        }

        private static bool Hidden(ConfigEntryBase e)
        {
            foreach (Rule rule in Rules)
                if (rule.When() && Array.IndexOf(rule.Hides, e) >= 0) return true;
            return false;
        }

        /// <summary>After Mod Menu has shown or filtered DorsthTweaks' list: hides the rows of settings that do nothing now.</summary>
        private static void ShownPostfix(object __instance)
        {
            try
            {
                // only DorsthTweaks' own list, and only if Mod Menu has made its rows
                if (!(_current.GetValue(__instance) as string == DorsthTweaksPlugin.Guid)) return;
                GameObject[] rows;
                if (!(_cache.GetValue(__instance) is Dictionary<string, GameObject[]> cache) || !cache.TryGetValue(DorsthTweaksPlugin.Guid, out rows)) return;
                foreach (GameObject row in rows)
                {
                    if (row == null || !row.activeSelf) continue;
                    int id = row.GetInstanceID();
                    ConfigEntryBase entry;
                    if (!RowEntry.TryGetValue(id, out entry))
                    {
                        Component boxed = row.GetComponent(_boxed);
                        object option = boxed != null ? _source.GetValue(boxed) : null;
                        RowEntry[id] = entry = option != null ? _entry.GetValue(option, null) as ConfigEntryBase : null;
                    }
                    if (entry != null && Hidden(entry)) row.SetActive(false);
                }
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        /// <summary>Has Mod Menu filter its list again (with what's in its search box), which shows every row and then runs ShownPostfix.</summary>
        private static void Refresh()
        {
            try
            {
                foreach (UnityEngine.Object panel in UnityEngine.Object.FindObjectsOfType(_panel))
                {
                    if (_current.GetValue(panel) as string == DorsthTweaksPlugin.Guid)
                    {
                        string search = "";
                        if (_search != null && _search.GetValue(panel) is TMP_InputField box && box != null) search = box.text ?? "";
                        AccessTools.Method(_panel, "FilterOptionList").Invoke(panel, new object[] { search });
                    }
                }
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }
    }

    // ==================================================================== hitmarkers

    /// <summary>
    /// Every weapon makes its hitmarker under the PauseManager and punches its scale: 2.5 for a headshot, 1 for a body shot.
    /// The game turns a headshot marker red after that, so the color is set just before the frame is drawn.
    /// Hitmarker Length, Width and Gap change the length and thickness of the marker's lines and the gap in the middle, apart. The game's
    /// hitmarkers are one picture each (an X): a copy of the picture is redrawn with each line's inner end moved by the gap
    /// and the line sized from there, made once per size and gap, then used for every hit.
    /// </summary>
    internal static class Hitmarkers
    {
        private struct Marker
        {
            public Graphic Graphic;
            public ColorSetting Color;
        }

        private static readonly List<Marker> Waiting = new List<Marker>();
        private static bool _listening;

        internal static void Install()
        {
            // only hooked while a hitmarker setting is changed from its default
            GameHook.Add(typeof(ShortcutExtensions), "DOPunchScale",
                new[] { typeof(Transform), typeof(Vector3), typeof(float), typeof(int), typeof(float) },
                typeof(Hitmarkers), null, "PunchPostfix", Needed);
            DorsthTweaksPlugin.HitmarkerWidth.SettingChanged += (sender, args) => GameHook.RefreshAll();
            // a new look is drawn a moment after the sliders stop moving, not on the next hit
            foreach (ConfigEntryBase e in new ConfigEntryBase[] { DorsthTweaksPlugin.HitmarkerWidth, DorsthTweaksPlugin.HitmarkerLength, DorsthTweaksPlugin.HitmarkerGap, DorsthTweaksPlugin.HitmarkerEnds })
                e.ConfigFile.SettingChanged += (sender, args) => { if (args.ChangedSetting == e) _prepareAt = Time.unscaledTime + 0.3f; };
            Host.Add<PrepareClock>();
            DorsthTweaksPlugin.HitmarkerPop.SettingChanged += (sender, args) => GameHook.RefreshAll();
            DorsthTweaksPlugin.StaticHitmarkers.SettingChanged += (sender, args) => GameHook.RefreshAll();
            DorsthTweaksPlugin.HeadPopLikeBody.SettingChanged += (sender, args) => GameHook.RefreshAll();
            DorsthTweaksPlugin.HideHitmarkers.SettingChanged += (sender, args) => GameHook.RefreshAll();
            DorsthTweaksPlugin.HitmarkerGap.SettingChanged += (sender, args) => GameHook.RefreshAll();
            DorsthTweaksPlugin.HitmarkerLength.SettingChanged += (sender, args) => GameHook.RefreshAll();
            DorsthTweaksPlugin.HitmarkerEnds.SettingChanged += (sender, args) => GameHook.RefreshAll();
            DorsthTweaksPlugin.BodyshotColor.Changed += GameHook.RefreshAll;
            DorsthTweaksPlugin.HeadshotColor.Changed += GameHook.RefreshAll;
            DorsthTweaksPlugin.ShotgunHitmarkerColor.Changed += GameHook.RefreshAll;
        }

        // A headshot with the shotgun (or another gun with a black hitmarker) gets the headshot color, which the game
        // fails to do, so this hook is on even with every setting at its default.
        private static bool Needed() { return true; }

        private static float _prepareAt = -1f;

        /// <summary>The hitmarker pictures' pixels are in: the pictures are made on the next frame.</summary>
        internal static void PrepareSoon() { _prepareAt = Time.unscaledTime; }

        private sealed class PrepareClock : MonoBehaviour
        {
            private void Update()
            {
                // pictures a new look replaced, once nothing on screen can still be showing them
                if (GapKept.AnyDoomed) GapKept.DropOld();
                if (_prepareAt < 0f || Time.unscaledTime < _prepareAt) return;
                _prepareAt = -1f;
                try { Prepare(); } catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
            }
        }

        internal static bool IsHitmarker(Transform t)
        {
            if (t == null) return false;
            PauseManager pm = PauseManager.Instance;
            return pm != null && t.parent == pm.transform;
        }

        private static bool _repunching;

        private static void PunchPostfix(Transform target, Vector3 punch, float duration, int vibrato, float elasticity, object __result)
        {
            try
            {
                if (_repunching || !IsHitmarker(target)) return;
                if (Hidden)
                {
                    Tween tween = __result as Tween;
                    if (tween != null) tween.Kill();
                    target.gameObject.SetActive(false);
                    return;
                }
                if (Still)
                {
                    // no pop: the marker stays at its own size for 0.2 s, then fades out over 0.1 s (the game's own 0.3 s
                    // life); the game's own removal after that finds it gone
                    Tween pop = __result as Tween;
                    if (pop != null) pop.Kill();
                    if (target.GetComponent<FadeOut>() == null) target.gameObject.AddComponent<FadeOut>();
                }
                bool head = punch.x > 2f;
                // the shotgun and a few other guns have their own black hitmarker
                bool shotgun = IsDarkMarker(target);
                ColorSetting color = head ? DorsthTweaksPlugin.HeadshotColor
                    : shotgun ? DorsthTweaksPlugin.ShotgunHitmarkerColor : DorsthTweaksPlugin.BodyshotColor;
                // Static hitmarkers look the same on every gun. A shotgun color needs the common picture too: the black
                // one can't be tinted. The game can't tint it for a headshot either: it sets the black picture's color
                // to red, which stays black, so a shotgun headshot looks like a bodyshot. A headshot with such a gun
                // gets the common picture, and with it the headshot color.
                bool darkHead = head && shotgun;
                if (shotgun && (Still || darkHead || (!head && !color.IsDefault))) MatchCommon(target);
                // how far it pops: the Hitmarker Animation setting (1 is the game's)
                float popBy = Pop;
                // Same Headshot Animation: the game pops a headshot 2.5 times as far as a bodyshot
                bool headAsBody = head && DorsthTweaksPlugin.HeadPopLikeBody.Value;
                if (headAsBody) punch /= 2.5f;
                // a size or gap of its own: the redrawn picture (at the game's size and gap it's the game's own picture)
                if (Resized())
                {
                    Image own = target.GetComponent<Image>();
                    GapKept.Made made = own != null && own.sprite != null ? GapKept.Apply(own, Width, Gap, Length, ScreenPx(target.parent, ((RectTransform)target).sizeDelta.x * target.localScale.x)) : null;
                    // smaller or bigger, so the lines move as far as with the normal gap
                    if (made != null) popBy *= made.Pop;
                }
                // the pop made over at its new size (the setting's share, and the gap's)
                if ((Mathf.Abs(popBy - 1f) > 0.01f || headAsBody) && !Still)
                {
                    Tween pop = __result as Tween;
                    // killed before it ever ran, so the marker is still at its own size
                    if (pop != null) pop.Kill();
                    _repunching = true;
                    try { target.DOPunchScale(punch * popBy, duration, vibrato, elasticity); }
                    finally { _repunching = false; }
                }
                // (the game's own red is enough for a headshot, except on the swapped picture: MatchCommon carried the black over)
                if (color.IsDefault && !darkHead) return;
                Graphic graphic = target.GetComponent<Graphic>();
                if (graphic == null) return;
                Waiting.Add(new Marker { Graphic = graphic, Color = color });
                if (_listening) return;
                Canvas.preWillRenderCanvases += PaintNow;
                _listening = true;
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        // A static hitmarker's end: full for 0.2 s, faded out over 0.1 s, then removed. Only how see-through it is
        // changes, so its color (body, head, shotgun) stays.
        private sealed class FadeOut : MonoBehaviour
        {
            private const float Hold = 0.2f, Fade = 0.1f;
            private float born;
            private CanvasRenderer cr;

            private void Awake()
            {
                born = Time.time;
                cr = GetComponent<CanvasRenderer>();
            }

            private void Update()
            {
                float t = Time.time - born;
                if (t >= Hold + Fade) { Destroy(gameObject); return; }
                if (t > Hold && cr != null) cr.SetAlpha(1f - (t - Hold) / Fade);
            }
        }

        private static GameObject _common;
        private static float _commonScanAt = -999f;
        // grenades, rockets and mines show a hitmarker of their own, kept in a field the game doesn't show
        private static readonly Dictionary<Type, FieldInfo> ProjectileMarkers = new Dictionary<Type, FieldInfo>
        {
            { typeof(PredictedProjectile), AccessTools.Field(typeof(PredictedProjectile), "hitMarker") },
            { typeof(ShrapnelBallistic), AccessTools.Field(typeof(ShrapnelBallistic), "hitMarker") },
            { typeof(ProximityMine), AccessTools.Field(typeof(ProximityMine), "hitMarker") },
        };

        /// <summary>The hitmarker most guns use. Some guns (the shotgun) have their own, with another size or gap.</summary>
        internal static GameObject Common()
        {
            if (_common != null || Time.unscaledTime - _commonScanAt < 5f) return _common;
            _commonScanAt = Time.unscaledTime;
            // the hitmarker most kinds of gun use
            var users = new Dictionary<GameObject, HashSet<string>>();
            foreach (Weapon w in Found.Of<Weapon>())
            {
                if (w == null || w.hitMarker == null) continue;
                if (!users.TryGetValue(w.hitMarker, out HashSet<string> names)) users[w.hitMarker] = names = new HashSet<string>();
                names.Add(w.gameObject.name.Replace("(Clone)", "").Trim());
            }
            int most = 0;
            foreach (var kv in users)
                if (kv.Value.Count > most) { most = kv.Value.Count; _common = kv.Key; }
            return _common;
        }

        /// <summary>
        /// A gun's own dark hitmarker (the shotgun's black one), not the one most guns use. A projectile's own
        /// hitmarker is light like the common one and keeps the bodyshot color.
        /// </summary>
        private static bool IsDarkMarker(Transform marker)
        {
            GameObject common = Common();
            if (common == null) return false;
            Image img = marker.GetComponent<Image>(), from = common.GetComponent<Image>();
            if (img == null || from == null || img.sprite == null || img.sprite == from.sprite) return false;
            Color line = GapKept.LineColor(img.sprite);
            return line.r + line.g + line.b < 0.9f;
        }


        /// <summary>Static Hitmarkers: a gun's own hitmarker takes the shape, size and scale of the common one and keeps its color.</summary>
        private static void MatchCommon(Transform marker)
        {
            GameObject common = Common();
            if (common == null) return;
            Image img = marker.GetComponent<Image>(), from = common.GetComponent<Image>();
            RectTransform rt = marker as RectTransform, fromRt = common.transform as RectTransform;
            if (img == null || from == null || rt == null || fromRt == null) return;
            if (img.sprite == from.sprite && rt.sizeDelta == fromRt.sizeDelta && marker.localScale == common.transform.localScale) return;
            // keep this gun's own color (the shotgun's is black): its picture's color goes into the tint
            Color mine = GapKept.LineColor(img.sprite), theirs = GapKept.LineColor(from.sprite);
            Color tint = img.color;
            tint.r *= theirs.r > 0.01f ? mine.r / theirs.r : mine.r;
            tint.g *= theirs.g > 0.01f ? mine.g / theirs.g : mine.g;
            tint.b *= theirs.b > 0.01f ? mine.b / theirs.b : mine.b;
            img.color = tint;
            img.sprite = from.sprite;
            img.type = from.type;
            img.preserveAspect = from.preserveAspect;
            rt.sizeDelta = fromRt.sizeDelta;
            marker.localScale = common.transform.localScale;
        }

        /// <summary>
        /// While a map loads: every hitmarker picture redrawn for the current size and gap now, and the colors of
        /// their lines read, instead of on the first hit. A picture has to come back from the graphics card first:
        /// that is asked for here without waiting (GapKept.Request), and this runs again when the answers are in.
        /// Made once per game and size, then reused.
        /// </summary>
        internal static int Prepare()
        {
            if (!Needed() || Hidden) return 0;
            bool resize = Resized();
            float gap = Gap;
            Common();
            // every hitmarker a gun or a projectile can show
            var markers = new HashSet<GameObject>();
            foreach (Weapon w in Found.Of<Weapon>())
                if (w != null && w.hitMarker != null) markers.Add(w.hitMarker);
            foreach (var kind in ProjectileMarkers)
            {
                if (kind.Value == null) continue;
                foreach (UnityEngine.Object thrown in typeof(MonoBehaviour).IsAssignableFrom(kind.Key) ? (System.Collections.IEnumerable)Found.Of(kind.Key) : Resources.FindObjectsOfTypeAll(kind.Key))
                {
                    GameObject marker = null;
                    try { marker = kind.Value.GetValue(thrown) as GameObject; }
                    catch { }
                    if (marker != null) markers.Add(marker);
                }
            }
            PauseManager pm = PauseManager.Instance;
            // one picture per sprite and size on screen
            var done = new HashSet<long>();
            int n = 0;
            foreach (GameObject marker in markers)
            {
                Image img = marker.GetComponent<Image>();
                var rt = marker.transform as RectTransform;
                if (img == null || img.sprite == null || rt == null) continue;
                float px = ScreenPx(pm != null ? pm.transform : null, rt.sizeDelta.x * marker.transform.localScale.x);
                if (!done.Add(((long)img.sprite.GetInstanceID() << 16) ^ Mathf.RoundToInt(px))) continue;
                // the color of its lines is read now too (asks for the picture if it isn't in yet)
                GapKept.LineColor(img.sprite);
                if (resize && GapKept.Get(img.sprite, Width, gap, Length, px) != null) n++;
            }
            return n;
        }

        private static float _lastPx;
        // one delegate, made once: "+= Paint" makes a new one every time
        private static readonly Canvas.WillRenderCanvases PaintNow = Paint;

        /// <summary>Hide Hitmarkers.</summary>
        internal static bool Hidden { get { return DorsthTweaksPlugin.HideHitmarkers.Value; } }

        /// <summary>
        /// How many pixels wide on screen something this many UI units wide is, under this parent. The redrawn
        /// hitmarker is drawn for exactly that, so its edges stay sharp.
        /// </summary>
        internal static float ScreenPx(Transform parent, float units)
        {
            float px = 0f;
            try
            {
                if (parent != null)
                {
                    Canvas c = parent.GetComponentInParent<Canvas>();
                    Canvas root = c != null ? c.rootCanvas : null;
                    Camera cam = root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
                    Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, parent.TransformPoint(Vector3.zero));
                    Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, parent.TransformPoint(new Vector3(units, 0f, 0f)));
                    px = Vector2.Distance(a, b);
                }
            }
            catch { px = 0f; }
            if (px >= 4f && px < 4000f) { _lastPx = px / Mathf.Max(units, 0.01f); return px; }
            // not on screen yet: the last one seen, or a guess from the screen's height
            return (_lastPx > 0f ? _lastPx : Screen.height / 1080f) * units;
        }

        /// <summary>The gap in the middle, times the normal one (the Hitmarker Gap setting).</summary>
        internal static float Gap { get { return Mathf.Clamp(DorsthTweaksPlugin.HitmarkerGap.Value, 0f, 3f); } }

        /// <summary>How far hitmarkers pop, times the game's own (the Hitmarker Animation setting).</summary>
        private static float Pop { get { return Mathf.Clamp(DorsthTweaksPlugin.HitmarkerPop.Value, 0.1f, 1f); } }

        /// <summary>Static Hitmarkers: no pop, they stay still and fade out.</summary>
        internal static bool Still { get { return DorsthTweaksPlugin.StaticHitmarkers.Value; } }

        /// <summary>Each line's length, times the game's own (the Hitmarker Length setting).</summary>
        internal static float Length { get { return Mathf.Clamp(DorsthTweaksPlugin.HitmarkerLength.Value, 0.1f, 3f); } }

        /// <summary>Each line's thickness, times the game's own (the Hitmarker Width setting).</summary>
        internal static float Width { get { return Mathf.Clamp(DorsthTweaksPlugin.HitmarkerWidth.Value, 0.2f, 3f); } }

        /// <summary>A size or gap other than the game's own.</summary>
        internal static bool Resized()
        {
            // the game's own picture as it is, unless a drawn shape is picked or its lines are resized or moved
            return DorsthTweaksPlugin.HitmarkerEnds.Value != DorsthTweaksPlugin.LineEnds.Default
                || Mathf.Abs(Length - 1f) > 0.001f || Mathf.Abs(Width - 1f) > 0.001f || Mathf.Abs(Gap - 1f) > 0.001f;
        }

        private static void Paint()
        {
            try
            {
                foreach (Marker m in Waiting)
                {
                    if (m.Graphic == null) continue; // already replaced by a newer hitmarker
                    Color c = m.Color.Color;
                    m.Graphic.color = new Color(c.r, c.g, c.b, c.a * m.Graphic.color.a);
                }
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
            finally
            {
                Waiting.Clear();
                Canvas.preWillRenderCanvases -= PaintNow;
                _listening = false;
            }
        }
    }

    /// <summary>
    /// A hitmarker picture redrawn with its lines grown or shrunk and its gap set apart: the picture is split in four
    /// around its middle, each quarter (one line of an X) has its inner end moved by the gap and is sized from there.
    /// </summary>
    internal static class GapKept
    {
        internal sealed class Made
        {
            public float Width, Gap, Length, Px;
            public DorsthTweaksPlugin.LineEnds Ends;
            public Sprite Sprite;
            public float GrowX, GrowY;
            // how much smaller the pop has to be for the lines to move as far as with the normal gap (1 at the normal gap)
            public float Pop = 1f;
        }

        // The pictures made of each of the game's, newest last: one per size on screen (a hitmarker can be on the HUD
        // and in the kill cam at two sizes), four at most.
        private static readonly Dictionary<Sprite, List<Made>> Cache = new Dictionary<Sprite, List<Made>>();
        // Pictures that were replaced, and when each can go: a hitmarker on screen may still be showing one (it blinked
        // when its picture was destroyed under it), and no hitmarker lives a second.
        private static readonly List<KeyValuePair<float, Made>> Doomed = new List<KeyValuePair<float, Made>>();
        private static readonly HashSet<Sprite> Failed = new HashSet<Sprite>();

        internal static Made Apply(Image image, float width, float gap, float length, float px)
        {
            Made made = Get(image.sprite, width, gap, length, px);
            if (made == null) return null;
            image.sprite = made.Sprite;
            // a bigger picture for longer lines: the box grows with it, to a whole number of screen pixels, so each
            // two of the picture's pixels land on one of the screen's
            Vector2 size = image.rectTransform.sizeDelta;
            image.rectTransform.sizeDelta = new Vector2(size.x * made.GrowX, size.y * made.GrowY);
            return made;
        }

        /// <summary>The redrawn picture for this size, made now if it isn't yet. Null when it can't be made.</summary>
        internal static Made Get(Sprite src, float width, float gap, float length, float px)
        {
            if (src == null || Failed.Contains(src)) return null;
            // its pixels aren't in yet (asked for now if they weren't): the game's own picture until they are
            if (!Ready(src)) return null;
            List<Made> kept;
            if (!Cache.TryGetValue(src, out kept)) Cache[src] = kept = new List<Made>();
            DorsthTweaksPlugin.LineEnds ends = DorsthTweaksPlugin.HitmarkerEnds.Value;
            Made made = null;
            for (int i = kept.Count - 1; i >= 0; i--)
            {
                Made was = kept[i];
                // another look than the settings ask for now (or its picture is gone): it goes, a second from now
                bool fits = was.Sprite != null && Mathf.Abs(was.Width - width) < 0.0001f && Mathf.Abs(was.Gap - gap) < 0.0001f
                    && Mathf.Abs(was.Length - length) < 0.0001f && was.Ends == ends;
                if (!fits)
                {
                    kept.RemoveAt(i);
                    Doomed.Add(new KeyValuePair<float, Made>(Time.unscaledTime + 1f, was));
                }
                else if (Mathf.Abs(was.Px - px) <= 0.5f) made = was;
            }
            if (made != null) return made;
            try { made = Make(src, width, gap, length, px); }
            catch (Exception e)
            {
                Failed.Add(src);
                DorsthTweaksPlugin.Log.LogWarning("Couldn't resize the hitmarker picture, it keeps its size. (" + e.Message + ")");
                return null;
            }
            kept.Add(made);
            if (kept.Count > 4)
            {
                Doomed.Add(new KeyValuePair<float, Made>(Time.unscaledTime + 1f, kept[0]));
                kept.RemoveAt(0);
            }
            return made;
        }

        // drawn at twice the screen's resolution for the hitmarker's size on screen: each 2 by 2 pixels make one on
        // screen, so the edges are sharp and smooth, about one screen pixel soft
        private const int Fine = 2;

        private struct Line
        {
            public Vector2 U, V;          // along the line (outward) and across it
            public float Inner, Outer;    // where it starts and ends, from the middle
            public float Half, Off;       // half its thickness, and how far its middle sits off the middle across
        }

        /// <summary>
        /// The game's picture measured, then drawn again fresh: each of the four lines' direction (the way its pixels
        /// line up, so it stays perfectly straight), where it starts and ends, how thick it is and its fill color (the
        /// middle of the line, not its darker edge). The new picture has the same lines, moved out by the gap, as long
        /// and as thick as the settings say, squared, rounded or spiked, in the fill color with no outline.
        /// </summary>
        // a game hitmarker picture measured: done once per picture, reading it back from the graphics card is
        // what takes the time; drawing it new for other settings only uses these numbers
        private sealed class Measured
        {
            public int W, H;
            public Line[] Lines;
            public Color Fill;
            public float Alpha;
        }

        private static readonly Dictionary<Sprite, Measured> Measures = new Dictionary<Sprite, Measured>();

        private static Made Make(Sprite src, float width, float gap, float length, float px)
        {
            if (!Measures.TryGetValue(src, out Measured m)) Measures[src] = m = Measure(src);
            return Draw(src, m, width, gap, length, px);
        }

        private static Measured Measure(Sprite src)
        {
            Rect r = src.textureRect;
            int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            Color[] px = Read(src, w, h);
            float cx = w / 2f, cy = h / 2f;

            // each quarter's pixels: their weighted middle and spread
            var sw = new float[4]; var sx = new float[4]; var sy = new float[4];
            float ca = 0f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color p = px[y * w + x];
                    if (p.a < 0.5f) continue;
                    float fx = x + 0.5f, fy = y + 0.5f;
                    int q = Quarter(fx, fy, cx, cy);
                    sw[q] += p.a; sx[q] += fx * p.a; sy[q] += fy * p.a;
                    ca = Mathf.Max(ca, p.a);
                }
            var lines = new Line[4];
            Color fillSum = Color.clear;
            for (int q = 0; q < 4; q++)
            {
                if (sw[q] <= 0f) throw new Exception("a quarter of the hitmarker is empty");
                float mx = sx[q] / sw[q], my = sy[q] / sw[q], sxx = 0f, syy = 0f, sxy = 0f;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        Color p = px[y * w + x];
                        if (p.a < 0.5f) continue;
                        float fx = x + 0.5f, fy = y + 0.5f;
                        if (Quarter(fx, fy, cx, cy) != q) continue;
                        sxx += (fx - mx) * (fx - mx) * p.a; syy += (fy - my) * (fy - my) * p.a; sxy += (fx - mx) * (fy - my) * p.a;
                    }
                float ang = 0.5f * Mathf.Atan2(2f * sxy, sxx - syy);
                var u = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                if ((mx - cx) * u.x + (my - cy) * u.y < 0f) u = -u;
                var v = new Vector2(-u.y, u.x);
                float a0 = float.MaxValue, a1 = float.MinValue, c0 = float.MaxValue, c1 = float.MinValue;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (px[y * w + x].a < 0.5f) continue;
                        float fx = x + 0.5f, fy = y + 0.5f;
                        if (Quarter(fx, fy, cx, cy) != q) continue;
                        float al = (fx - cx) * u.x + (fy - cy) * u.y, ac = (fx - mx) * v.x + (fy - my) * v.y;
                        a0 = Mathf.Min(a0, al); a1 = Mathf.Max(a1, al); c0 = Mathf.Min(c0, ac); c1 = Mathf.Max(c1, ac);
                    }
                lines[q] = new Line { U = u, V = v, Inner = a0 - 0.5f, Outer = a1 + 0.5f, Half = (c1 - c0 + 1f) / 2f, Off = (mx - cx) * v.x + (my - cy) * v.y };

                // the fill: the color in the middle of the line (its edge may be darker), away from its ends
                float halfQ = lines[q].Half;
                var bins = new Color[16];
                var binW = new float[16];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        Color p = px[y * w + x];
                        if (p.a < 0.5f) continue;
                        float fx = x + 0.5f, fy = y + 0.5f;
                        if (Quarter(fx, fy, cx, cy) != q) continue;
                        float al = (fx - cx) * u.x + (fy - cy) * u.y, ac = (fx - mx) * v.x + (fy - my) * v.y;
                        if (al < a0 + halfQ + 1f || al > a1 - halfQ - 1f) continue;
                        int k = Mathf.Min(15, (int)(Mathf.Abs(ac) + 0.5f));
                        bins[k] += p * p.a;
                        binW[k] += p.a;
                    }
                for (int k = 0; k < 16; k++)
                    if (binW[k] > 0f) { fillSum += bins[k] / binW[k]; break; }
            }
            Color fillColor = fillSum / 4f;
            fillColor.a = ca;
            return new Measured { W = w, H = h, Lines = lines, Fill = fillColor, Alpha = ca };
        }

        private static Made Draw(Sprite src, Measured m, float width, float gap, float length, float px)
        {
            int w = m.W, h = m.H;
            Line[] lines = m.Lines;
            Color fillColor = m.Fill;
            float ca = m.Alpha;

            // The pop grows the whole picture from the middle, so a line further out moves further. The middle of a line
            // sits at (inner end * gap + line length * length / 2) from the middle; the pop is made smaller by how much
            // further out that is than with the normal gap, so the lines move as far as they do with the normal gap.
            float ri = 0f, len = 0f;
            foreach (Line l in lines) { ri += l.Inner / 4f; len += (l.Outer - l.Inner) / 4f; }
            float normal = ri + len / 2f, now = ri * gap + len * length / 2f;
            float pop = now > 0.01f ? Mathf.Clamp(normal / now, 0.2f, 5f) : 1f;

            DorsthTweaksPlugin.LineEnds ends = DorsthTweaksPlugin.HitmarkerEnds.Value;
            float grow = Mathf.Max(1f, Mathf.Max(length, gap)) * (1f + 0.15f * Mathf.Max(0f, width - 1f));
            // screen pixels per pixel of the game's picture, and the new picture's size: a whole, even number of
            // screen pixels (centered on the screen, its pixels line up with the screen's)
            float k = Mathf.Clamp(px / w, 0.05f, 8f);
            // never more than 500 screen pixels a side: a huge screen or UI scale took long to draw (every pixel is worked out)
            k = Mathf.Min(k, 500f / (Mathf.Max(w, h) * grow));
            float scale = k * Fine;
            int sw = 2 * Mathf.CeilToInt(w * grow * k / 2f), sh = 2 * Mathf.CeilToInt(h * grow * k / 2f);
            int W = sw * Fine, H = sh * Fine;
            float hx = W / (2f * scale), hy = H / (2f * scale);
            if (ends == DorsthTweaksPlugin.LineEnds.Default)
            {
                Color32[] warped = Warp(src, m, width, gap, length, W, H, scale, hx, hy);
                return Finish(src, warped, W, H, scale, width, gap, length, px, ends, sw / (w * k), sh / (h * k), pop);
            }

            // every pixel in the line's fill color (so the soft edge blends in that color, not black), only its
            // opacity changes: how much of the pixel the nearest line covers
            Color32 fill = new Color(fillColor.r, fillColor.g, fillColor.b, 1f);
            var outPx = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float fx = (x + 0.5f) / scale - hx, fy = (y + 0.5f) / scale - hy;
                    float best = -1e9f;
                    foreach (Line l in lines)
                    {
                        float a = fx * l.U.x + fy * l.U.y, c = fx * l.V.x + fy * l.V.y - l.Off;
                        float s0 = l.Inner * gap, s1 = s0 + (l.Outer - l.Inner) * length, half = l.Half * width;
                        best = Mathf.Max(best, Inside(ends, a, c, s0, s1, half));
                    }
                    fill.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(best * scale + 0.5f) * ca * 255f);
                    outPx[y * W + x] = fill;
                }

            return Finish(src, outPx, W, H, scale, width, gap, length, px, ends, sw / (w * k), sh / (h * k), pop);
        }

        private static Made Finish(Sprite src, Color32[] outPx, int W, int H, float scale, float width, float gap, float length, float px,
            DorsthTweaksPlugin.LineEnds ends, float growX, float growY, float pop)
        {
            // no smaller versions (they blur it), left out of the game's texture quality limit
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = src.name + " (DorsthTweaks)" };
            try { tex.ignoreMipmapLimit = true; } catch { }
            tex.SetPixels32(outPx);
            tex.Apply(false, true);
            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, W, H), new Vector2(0.5f, 0.5f), src.pixelsPerUnit * scale);
            sprite.name = tex.name;
            return new Made { Width = width, Gap = gap, Length = length, Px = px, Ends = ends, Sprite = sprite, GrowX = growX, GrowY = growY, Pop = pop };
        }

        /// <summary>
        /// The game's own picture, each of its four lines moved out by the gap and stretched along and across by the
        /// length and width: every new pixel is read from where it was in the game's picture, so it keeps its exact
        /// look (fill, edge and all). Pixels much smaller on screen than the game's are averaged, so it stays smooth.
        /// </summary>
        private static Color32[] Warp(Sprite src, Measured m, float width, float gap, float length, int W, int H, float scale, float hx, float hy)
        {
            int w = m.W, h = m.H;
            Color[] px = Read(src, w, h);
            float cx = w / 2f, cy = h / 2f;
            int ss = Mathf.Clamp(Mathf.CeilToInt(1f / scale), 1, 4);
            var outPx = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                        {
                            float fx = (x + (sx + 0.5f) / ss) / scale - hx, fy = (y + (sy + 0.5f) / ss) / scale - hy;
                            // the line this point belongs to: the one whose stretched picture is most solid here
                            Color best = Color.clear;
                            for (int q = 0; q < 4; q++)
                            {
                                Line l = m.Lines[q];
                                float al = fx * l.U.x + fy * l.U.y, ac = fx * l.V.x + fy * l.V.y - l.Off;
                                float from = l.Inner + (al - l.Inner * gap) / length, across = ac / width;
                                float ox = l.U.x * from + l.V.x * (across + l.Off) + cx, oy = l.U.y * from + l.V.y * (across + l.Off) + cy;
                                if (Quarter(ox, oy, cx, cy) != q) continue;
                                Color c = Bilinear(px, w, h, ox, oy);
                                if (c.a > best.a) best = c;
                            }
                            r += best.r * best.a; g += best.g * best.a; b += best.b * best.a; a += best.a;
                        }
                    // colors weighed by their opacity, so the see-through edge doesn't darken them
                    float n = ss * ss;
                    outPx[y * W + x] = a > 0f ? new Color(r / a, g / a, b / a, a / n) : new Color(0f, 0f, 0f, 0f);
                }
            return outPx;
        }

        // the picture at a point (in pixels), from the four pixels around it, colors weighed by opacity
        private static Color Bilinear(Color[] px, int w, int h, float x, float y)
        {
            x -= 0.5f; y -= 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            float r = 0f, g = 0f, b = 0f, a = 0f;
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                {
                    int xi = x0 + i, yi = y0 + j;
                    if (xi < 0 || yi < 0 || xi >= w || yi >= h) continue;
                    float wt = (i == 0 ? 1f - tx : tx) * (j == 0 ? 1f - ty : ty);
                    Color p = px[yi * w + xi];
                    float pa = p.a * wt;
                    r += p.r * pa; g += p.g * pa; b += p.b * pa; a += pa;
                }
            return a > 0.0001f ? new Color(r / a, g / a, b / a, a) : Color.clear;
        }

        /// <summary>
        /// How far a point is inside a line (negative outside), in the game picture's pixels. along: from the middle
        /// along the line, across: from the line's middle, start and end along it, half its thickness. Rounded ends
        /// are half circles; spiked is a nail like Overwatch's hitmarker: a thin triangle, flat toward the crosshair,
        /// its point outward. Every shape keeps the line's length.
        /// </summary>
        private static float Inside(DorsthTweaksPlugin.LineEnds ends, float along, float across, float start, float end, float half)
        {
            across = Mathf.Abs(across);
            float length = end - start;
            if (ends == DorsthTweaksPlugin.LineEnds.Rounded)
            {
                float r = Mathf.Min(half, length / 2f);
                float on = Mathf.Clamp(along, start + r, end - r);
                return r - Mathf.Sqrt((along - on) * (along - on) + across * across);
            }
            if (ends == DorsthTweaksPlugin.LineEnds.Spiked)
            {
                // a nail, like Overwatch's: a long thin triangle, its flat end toward the crosshair and its straight
                // sides narrowing to a sharp point outward
                if (length <= 0.01f) return -1f;
                float baseHalf = half * 1.4f, slope = baseHalf / length;
                float wide = baseHalf * (end - along) / length;
                return Mathf.Min(along - start, (wide - across) / Mathf.Sqrt(1f + slope * slope));
            }
            return Mathf.Min(half - across, Mathf.Min(along - start, end - along));
        }

        private static int Quarter(float x, float y, float cx, float cy)
        {
            return (x >= cx ? 1 : 0) + (y >= cy ? 2 : 0);
        }

        private static readonly Dictionary<Sprite, Color> Tints = new Dictionary<Sprite, Color>();

        /// <summary>The average color of a picture's lines, so a black hitmarker stays black on another picture.</summary>
        internal static Color LineColor(Sprite sprite)
        {
            Color c;
            if (sprite == null) return Color.white;
            if (Tints.TryGetValue(sprite, out c)) return c;
            c = Color.white;
            try
            {
                Rect r = sprite.textureRect;
                int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
                Color[] px = Read(sprite, w, h);
                // not in yet: white for now, and asked again next time
                if (px == null) return Failed.Contains(sprite) ? (Tints[sprite] = c) : c;
                float sr = 0f, sg = 0f, sb = 0f, n = 0f;
                foreach (Color p in px)
                {
                    if (p.a < 0.5f) continue;
                    sr += p.r; sg += p.g; sb += p.b; n += 1f;
                }
                if (n > 0f) c = new Color(sr / n, sg / n, sb / n, 1f);
            }
            catch (Exception e) { DorsthTweaksPlugin.Log.LogWarning("Couldn't read a hitmarker's color: " + e.Message); }
            Tints[sprite] = c;
            return c;
        }

        /// <summary>The picture's pixels. Game pictures usually can't be read directly, so it's drawn into a copy first.</summary>
        private static readonly Dictionary<Sprite, Color[]> Pixels = new Dictionary<Sprite, Color[]>();

        internal static bool AnyDoomed { get { return Doomed.Count > 0; } }

        /// <summary>Destroys the replaced pictures whose second is up.</summary>
        internal static void DropOld()
        {
            for (int i = Doomed.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < Doomed[i].Key) continue;
                Made old = Doomed[i].Value;
                Doomed.RemoveAt(i);
                if (old.Sprite != null)
                {
                    UnityEngine.Object.Destroy(old.Sprite.texture);
                    UnityEngine.Object.Destroy(old.Sprite);
                }
            }
        }

        // A picture's pixels are read once, for its color and its shape. Game pictures usually can't be read directly:
        // the picture is drawn into a copy and the copy is read back from the graphics card. Asking for that and waiting
        // (ReadPixels) holds the game up until the graphics card has done everything it had queued, which right after a
        // map loaded was most of a 185 ms frame (measured). So it is asked for without waiting: the answer comes a few
        // frames later, and until then Read gives null (the hitmarker is the game's own for that moment). Nothing here
        // ever waits for the graphics card, also not on a hit.
        private static readonly HashSet<Sprite> Asked = new HashSet<Sprite>();
        private struct Got { public Sprite Sprite; public Color32[] Data; public int W, H; }
        // answers that came in before it was known which way up the graphics card hands rows back
        private static readonly List<Got> Waiting = new List<Got>();
        private static int _rows = -1;       // -1 not known yet, 0 bottom row first (like GetPixels), 1 top row first
        private static int _curve;           // 0 colors come back as they are, 1 they come back darker (linear), 2 lighter: put right in Take
        private static bool _probeAsked;

        /// <summary>The picture's pixels are in.</summary>
        internal static bool Ready(Sprite sprite)
        {
            if (sprite == null) return false;
            if (Pixels.ContainsKey(sprite)) return true;
            Request(sprite);
            return Pixels.ContainsKey(sprite);
        }

        /// <summary>The picture's pixels, or null while they aren't in yet (they're asked for).</summary>
        private static Color[] Read(Sprite sprite, int w, int h)
        {
            Color[] px;
            if (Pixels.TryGetValue(sprite, out px)) return px;
            Request(sprite);
            return Pixels.TryGetValue(sprite, out px) ? px : null;
        }

        /// <summary>Asks for a picture's pixels, once. A readable picture is read on the spot (that's no trip to the graphics card).</summary>
        internal static void Request(Sprite sprite)
        {
            if (sprite == null || Pixels.ContainsKey(sprite) || Asked.Contains(sprite) || Failed.Contains(sprite)) return;
            RenderTexture before = RenderTexture.active;
            try
            {
                Texture2D tex = sprite.texture;
                Rect r = sprite.textureRect;
                int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
                if (tex == null || w <= 0 || h <= 0) { Failed.Add(sprite); return; }
                if (tex.isReadable) { Pixels[sprite] = tex.GetPixels(Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y), w, h); return; }
                // a graphics card that can't answer later: the old way, waiting
                if (!SystemInfo.supportsAsyncGPUReadback) { Pixels[sprite] = ReadNow(tex, r, w, h); return; }
                AskRows();
                // the picture's own part of its texture, pixel for pixel, into a copy of just that size
                RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt, new Vector2(r.width / tex.width, r.height / tex.height), new Vector2(r.x / tex.width, r.y / tex.height));
                Asked.Add(sprite);
                AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, answer =>
                {
                    try
                    {
                        Color32[] data = null;
                        if (!answer.hasError)
                        {
                            var raw = answer.GetData<Color32>();
                            if (raw.Length == w * h) data = raw.ToArray();
                        }
                        RenderTexture.ReleaseTemporary(rt);
                        if (data == null)
                        {
                            Asked.Remove(sprite);
                            Failed.Add(sprite);
                            DorsthTweaksPlugin.Log.LogWarning("Couldn't read a hitmarker picture, it stays the game's own.");
                        }
                        else if (_rows < 0) Waiting.Add(new Got { Sprite = sprite, Data = data, W = w, H = h });
                        else Take(sprite, data, w, h);
                    }
                    catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
                });
            }
            catch (Exception e)
            {
                Asked.Remove(sprite);
                Failed.Add(sprite);
                DorsthTweaksPlugin.Log.LogWarning("Couldn't read a hitmarker picture, it stays the game's own. (" + e.Message + ")");
            }
            finally { RenderTexture.active = before; }
        }

        /// <summary>
        /// Which way up the answers come, and whether their colors come back unchanged: a picture of two pixels, half
        /// red under half green, goes the same way as every hitmarker picture (drawn into a copy, read back later). If
        /// its first pixel comes back green, rows come top first and every answer is turned over. If the half comes
        /// back darker or lighter than it went in, the graphics card's color curve was applied on the way and every
        /// answer has it taken out again. Asked once a game, before the first picture.
        /// </summary>
        private static void AskRows()
        {
            if (_probeAsked) return;
            _probeAsked = true;
            var two = new Texture2D(1, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            two.SetPixels32(new[] { new Color32(128, 0, 0, 255), new Color32(0, 128, 0, 255) });
            two.Apply(false, false);
            RenderTexture rt = RenderTexture.GetTemporary(1, 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(two, rt, new Vector2(1f, 1f), new Vector2(0f, 0f));
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, answer =>
            {
                try
                {
                    int rows = 0, curve = 0;
                    if (!answer.hasError)
                    {
                        var raw = answer.GetData<Color32>();
                        if (raw.Length == 2)
                        {
                            if (raw[0].g > raw[0].r) rows = 1;
                            int half = Mathf.Max(Mathf.Max(raw[0].r, raw[0].g), Mathf.Max(raw[1].r, raw[1].g));   // went in as 128
                            if (half > 30 && half < 92) curve = 1;            // 55: came back linear
                            else if (half > 160 && half < 215) curve = 2;     // 188: the curve was applied twice
                            if (rows != 0 || curve != 0) DorsthTweaksPlugin.Log.LogInfo("Hitmarker pictures come back from the graphics card " + (rows != 0 ? "top row first" : "bottom row first") + (curve == 1 ? ", darker (put right)" : curve == 2 ? ", lighter (put right)" : "") + ".");
                        }
                    }
                    RenderTexture.ReleaseTemporary(rt);
                    UnityEngine.Object.Destroy(two);
                    _curve = curve;
                    _rows = rows;
                    Got[] waited = Waiting.ToArray();
                    Waiting.Clear();
                    foreach (Got g in waited) Take(g.Sprite, g.Data, g.W, g.H);
                }
                catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
            });
        }

        /// <summary>An answer is in: kept the way GetPixels gives pixels (bottom row first), and when it was the last one asked for, the pictures are made.</summary>
        private static void Take(Sprite sprite, Color32[] data, int w, int h)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                int from = (_rows == 1 ? h - 1 - y : y) * w, to = y * w;
                for (int x = 0; x < w; x++) px[to + x] = data[from + x];
            }
            if (_curve != 0)
                for (int i = 0; i < px.Length; i++)
                {
                    Color c = px[i];
                    px[i] = _curve == 1
                        ? new Color(Mathf.LinearToGammaSpace(c.r), Mathf.LinearToGammaSpace(c.g), Mathf.LinearToGammaSpace(c.b), c.a)
                        : new Color(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b), c.a);
                }
            Asked.Remove(sprite);
            if (sprite == null) return;       // gone meanwhile
            Pixels[sprite] = px;
            if (Asked.Count == 0) Hitmarkers.PrepareSoon();
        }

        /// <summary>The old way, waiting for the graphics card. Only where it can't answer later.</summary>
        private static Color[] ReadNow(Texture2D tex, Rect r, int w, int h)
        {
            RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture before = RenderTexture.active;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
                copy.Apply();
                Color[] px = copy.GetPixels();
                UnityEngine.Object.Destroy(copy);
                return px;
            }
            finally
            {
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }

    // ==================================================================== log spam

    /// <summary>
    /// The game writes the same few warnings to its log over and over, about 3 a second in a match (1,650 of
    /// "server is not active" in 10 minutes of play, measured), and every one costs a little time: Unity takes a
    /// stack trace and writes it to the log file and the BepInEx console. These are dropped before Unity does any of
    /// that, along with the game's leftover debug lines (bare numbers, "reload2"). Errors and everything else are
    /// kept. Always on.
    /// </summary>
    internal sealed class QuietLog : ILogHandler
    {
        private static readonly string[] DroppedWarnings =
        {
            "Cannot complete action because server is not active",
            "Cannot complete operation as server when server is not active",
            "Cannot complete action because client is not active",
            "Cannot complete action because you are not the owner of this object",
            "Spawned NetworkObject was expected to exist but does not",
            "PlayOneShot was called with a null AudioClip",
            "Ownership cannot be given for object",
            "Can not play a disabled audio source",
            "NetworkAudioClips instance has not been assigned",
        };

        private readonly ILogHandler _next;
        private QuietLog(ILogHandler next) { _next = next; }
        internal static long Dropped;

        internal static void Install()
        {
            ILogHandler current = Debug.unityLogger.logHandler;
            if (current == null || current is QuietLog) return;
            Debug.unityLogger.logHandler = new QuietLog(current);
        }

        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            if ((logType == LogType.Warning || logType == LogType.Log) && Drop(logType, format, args)) { Dropped++; return; }
            _next.LogFormat(logType, context, format, args);
        }

        public void LogException(Exception exception, UnityEngine.Object context) { _next.LogException(exception, context); }

        private static bool Drop(LogType type, string format, object[] args)
        {
            string text = format == "{0}" && args != null && args.Length > 0 ? args[0] as string : format;
            if (string.IsNullOrEmpty(text)) return false;
            if (type == LogType.Warning)
            {
                foreach (string d in DroppedWarnings)
                    if (text.StartsWith(d, StringComparison.Ordinal)) return true;
                return false;
            }
            // the game's leftover debug lines: a bare number, "reload" with a number
            if (text.Length <= 8)
            {
                string t = text.StartsWith("reload", StringComparison.Ordinal) ? text.Substring(6) : text;
                if (t.Length == 0) return text == "reload";
                foreach (char c in t) if (c < '0' || c > '9') return false;
                return true;
            }
            return false;
        }
    }

    // ==================================================================== neck hitmarkers

    /// <summary>
    /// A hit on the neck counts as a headshot (the game's own guns give it headshot damage: "Head_Col" or
    /// "Neck_1_Col"), but the same guns pick the hitmarker (red, the big pop) and sometimes the sound by "Head_Col"
    /// alone, so a neck hit did headshot damage with a bodyshot's marker. In each method of the game that already
    /// counts the neck as the head, every "is it the head" check now counts the neck too, so marker and sound match the
    /// damage. Methods that don't count the neck as the head (their damage too) are left alone, so no damage changes.
    /// Always on.
    /// </summary>
    internal static class NeckHits
    {
        internal static void Install()
        {
            var eq = AccessTools.Method(typeof(string), "op_Equality", new[] { typeof(string), typeof(string) });
            var tr = new HarmonyMethod(AccessTools.Method(typeof(NeckHits), nameof(Transpiler)));
            int patched = 0;
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(Weapon).Assembly))
            {
                if (!typeof(Weapon).IsAssignableFrom(type)) continue;
                foreach (MethodInfo m in AccessTools.GetDeclaredMethods(type))
                {
                    if (m.IsAbstract || m.GetMethodBody() == null) continue;
                    List<CodeInstruction> code;
                    try { code = PatchProcessor.GetOriginalInstructions(m); } catch { continue; }
                    bool neck = false, head = false;
                    for (int i = 0; i < code.Count; i++)
                    {
                        if (code[i].opcode != OpCodes.Ldstr) continue;
                        string str = code[i].operand as string;
                        if (str == "Neck_1_Col") neck = true;
                        if (str == "Head_Col" && i + 1 < code.Count && code[i + 1].Calls(eq)) head = true;
                    }
                    if (!neck || !head) continue;
                    DorsthTweaksPlugin.Harmony.Patch(m, transpiler: tr);
                    patched++;
                }
            }
            DorsthTweaksPlugin.Log.LogInfo("Neck hitmarkers: " + patched + " shot methods");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var eq = AccessTools.Method(typeof(string), "op_Equality", new[] { typeof(string), typeof(string) });
            var mine = AccessTools.Method(typeof(NeckHits), nameof(HeadOrNeck));
            var list = new List<CodeInstruction>(instructions);
            for (int i = 0; i + 1 < list.Count; i++)
                if (list[i].opcode == OpCodes.Ldstr && (list[i].operand as string) == "Head_Col" && list[i + 1].Calls(eq))
                    list[i + 1] = new CodeInstruction(OpCodes.Call, mine) { labels = list[i + 1].labels, blocks = list[i + 1].blocks };
            return list;
        }

        // name == "Head_Col", the neck counted as the head
        public static bool HeadOrNeck(string name, string head)
        {
            return name == head || (head == "Head_Col" && name == "Neck_1_Col");
        }
    }

    // ==================================================================== bullet trails

    /// <summary>
    /// Hide My Trails and Hide Other Players Trails: every gun spawns its trail in its own SpawnBulletTrail RPC, and the
    /// trail is all that method makes, so for a hidden kind of shot it isn't run. Only hooked while one is on.
    ///
    /// Trail Color: each gun's trail is a copy of the gun's own trail, a line whose color the game's LineFade
    /// fades out. Just before a gun makes one, its trail gets the color (as bright as the gun's own, so glowing trails
    /// still glow), so every copy has it. The gun's own colors are kept and put back when the setting goes back to white.
    /// </summary>
    internal static class BulletTrails
    {
        internal static void Install()
        {
            // each gun has its own SpawnBulletTrail RPC, "RpcLogic___SpawnBulletTrail_<hash>": that's what runs on your client
            int found = 0;
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(Weapon).Assembly))
            {
                if (!typeof(Weapon).IsAssignableFrom(type)) continue;
                foreach (MethodInfo m in AccessTools.GetDeclaredMethods(type))
                {
                    if (!m.Name.StartsWith("RpcLogic___SpawnBulletTrail_", StringComparison.Ordinal)) continue;
                    GameHook.Add(type, m.Name, null, typeof(BulletTrails), "SkipPrefix", null, AnyHidden, Priority.First);
                    GameHook.Add(type, m.Name, null, typeof(BulletTrails), "TintPrefix", null,
                        () => !DorsthTweaksPlugin.BulletTrailColor.IsDefault && !AllHidden());
                    // always, after the color: one straight trail per bullet, at the width and opacity set (see StraightPrefix)
                    GameHook.Add(type, m.Name, null, typeof(BulletTrails), "StraightPrefix", null,
                        () => !AllHidden(), Priority.Low);
                    found++;
                }
            }
            if (found == 0) throw new MissingMethodException("Weapon", "SpawnBulletTrail");
            DorsthTweaksPlugin.OnChange(DorsthTweaksPlugin.HideMyTrails, () => { });
            DorsthTweaksPlugin.OnChange(DorsthTweaksPlugin.HideOthersTrails, () => { });
            DorsthTweaksPlugin.BulletTrailColor.Changed += () => { GameHook.RefreshAll(); Repaint(); };
        }

        private static bool AnyHidden() { return DorsthTweaksPlugin.HideMyTrails.Value || DorsthTweaksPlugin.HideOthersTrails.Value; }
        private static bool AllHidden() { return DorsthTweaksPlugin.HideMyTrails.Value && DorsthTweaksPlugin.HideOthersTrails.Value; }

        /// <summary>Hide My Trails and Hide Other Players Trails: false (no trail) for a shot whose kind is hidden.</summary>
        private static bool SkipPrefix(Weapon __instance)
        {
            if (__instance == null) return true;
            return !(__instance.IsOwner ? DorsthTweaksPlugin.HideMyTrails.Value : DorsthTweaksPlugin.HideOthersTrails.Value);
        }

        // Minimal Viewmodels' own Hide Bullet Trails (it hides yours by skipping the game's method, which StraightPrefix
        // replaces): read once, by name, and honoured for your own shots.
        private static Func<bool> _mvrHides;

        private static bool MvrHides()
        {
            if (_mvrHides == null)
            {
                _mvrHides = () => false;
                try
                {
                    PluginInfo mvr;
                    if (Chainloader.PluginInfos.TryGetValue("kestrel.straftat.minimalviewmodels", out mvr) && mvr.Instance != null)
                    {
                        PropertyInfo setting = AccessTools.Property(mvr.Instance.GetType(), "HideBulletTrails");
                        if (setting != null && setting.GetValue(null, null) is ConfigEntry<bool> hide) _mvrHides = () => hide.Value;
                    }
                }
                catch (Exception e) { DorsthTweaksPlugin.Log.LogWarning("Couldn't read Minimal Viewmodels' Hide Bullet Trails: " + e.Message); }
            }
            return _mvrHides();
        }

        // Straight Bullet Trails. A bullet goes on through players, glass and other things it can pass (the game's own
        // list of layers) until something solid stops it, and the game draws a trail from the gun to every one of those
        // hits, all in the same moment. The bullet flies from your eye but the trails start at the gun, well below and
        // beside it, so they fan out from the gun in a V and look bent or doubled. Here all of one shot's hits (the
        // same gun, the same frame, other players' too: their hits of one shot come in together) are one straight trail from
        // the gun to the last of them, the point that stopped it. A shotgun's pellets each have their own: for a
        // shotgun only a hit straight behind the last one (seen from the gun) joins its trail. Two shots of one gun in
        // the same frame (a fast gun at a low frame rate, a laggy player) each keep their own: a hit nearer than the
        // trail's end starts no new trail and doesn't shorten it. Trail Width and Trail Opacity are applied here too
        // (at 1 the trail is the game's). Always on.
        private struct Last { public float At; public Vector3 From, End; public LineRenderer Line; }
        // this frame's trails by gun, emptied when a new frame starts
        private static readonly Dictionary<int, Last> LastTrail = new Dictionary<int, Last>();
        private static int _trailFrame = -1;

        private static bool StraightPrefix(Weapon __instance, Vector3 hitPoint)
        {
            try
            {
                if (__instance == null || __instance.bulletTrailLocal == null || __instance.shootPoint == null) return true;
                // a hidden kind of trail (the skip hook is off while only one kind is hidden and this one runs first)
                if (!SkipPrefix(__instance) || (__instance.IsOwner && MvrHides())) return false;
                Vector3 from = __instance.shootPoint.position;
                int key = __instance.GetInstanceID();
                // (the same frame only: a fast gun's next shot can come a few hundredths of a second later; another
                // player's hits of one shot come in together, in one message)
                if (_trailFrame != Time.frameCount)
                {
                    LastTrail.Clear();
                    _trailFrame = Time.frameCount;
                }
                Last last;
                if (LastTrail.TryGetValue(key, out last) && last.Line != null)
                {
                    Vector3 a = last.End - last.From, b = hitPoint - last.From;
                    bool same = !(__instance is Shotgun) || (b.sqrMagnitude > a.sqrMagnitude && Vector3.Angle(a, b) < 3f);
                    if (same)
                    {
                        if (b.sqrMagnitude > a.sqrMagnitude)
                        {
                            last.Line.SetPosition(1, hitPoint);
                            last.End = hitPoint;
                        }
                        last.At = Time.unscaledTime;
                        LastTrail[key] = last;
                        return false;
                    }
                }
                GameObject obj = UnityEngine.Object.Instantiate(__instance.bulletTrailLocal.gameObject, from, Quaternion.identity);
                LineRenderer line = obj.GetComponent<LineRenderer>();
                line.positionCount = 2;
                line.SetPosition(0, from);
                line.SetPosition(1, hitPoint);
                float width = Mathf.Clamp(DorsthTweaksPlugin.TrailWidth.Value, 0.2f, 3f);
                float opacity = Mathf.Clamp(DorsthTweaksPlugin.TrailOpacity.Value, 0.1f, 1f);
                if (Mathf.Abs(width - 1f) > 0.001f) line.widthMultiplier *= width;
                if (opacity < 0.999f)
                {
                    Color c0 = line.startColor, c1 = line.endColor;
                    c0.a *= opacity; c1.a *= opacity;
                    line.startColor = c0;
                    line.endColor = c1;
                    LineFade fade = obj.GetComponent<LineFade>();
                    if (fade != null) { Color f = fade.color; f.a *= opacity; fade.color = f; }
                }
                UnityEngine.Object.Destroy(obj, __instance is BeamGun ? 1.2f : 0.4f);
                LastTrail[key] = new Last { At = Time.unscaledTime, From = from, End = hitPoint, Line = line };
                return false;
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); return true; }
        }

        private sealed class Own
        {
            public LineRenderer Line;
            public LineFade Fade;
            public Color Start, End, FadeColor;
            public Material Mat;
            public bool Glow;
        }

        private static readonly Dictionary<Material, Material> Plains = new Dictionary<Material, Material>();

        // a normal see-through line with the glow's picture, for a dark color on a glowing trail
        private static Material Plain(Material glow)
        {
            if (glow == null) return null;
            if (Plains.TryGetValue(glow, out Material made) && made != null) return made;
            Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (sh == null) return null;
            made = new Material(sh) { name = glow.name + " (DorsthTweaks)", mainTexture = glow.mainTexture };
            UnityEngine.Object.DontDestroyOnLoad(made);
            Plains[glow] = made;
            return made;
        }

        // drawn by adding light (a glow): dark colors don't show on it
        private static bool Adds(Material m)
        {
            if (m == null) return false;
            if (m.HasProperty("_DstBlend") && Mathf.RoundToInt(m.GetFloat("_DstBlend")) == (int)UnityEngine.Rendering.BlendMode.One) return true;
            string n = m.shader != null ? m.shader.name : "";
            return n.IndexOf("Add", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // each gun's trail and its own colors, by the trail's id
        private static readonly Dictionary<int, Own> Trails = new Dictionary<int, Own>();

        private static void TintPrefix(Weapon __instance)
        {
            try
            {
                LineRenderer line = __instance != null ? __instance.bulletTrailLocal : null;
                if (line == null) return;
                int id = line.GetInstanceID();
                if (!Trails.TryGetValue(id, out Own own))
                {
                    LineFade fade = line.GetComponent<LineFade>();
                    Material mat = line.sharedMaterial;
                    Trails[id] = own = new Own { Line = line, Fade = fade, Start = line.startColor, End = line.endColor, FadeColor = fade != null ? fade.color : Color.white, Mat = mat, Glow = Adds(mat) };
                    DorsthTweaksPlugin.Log.LogInfo("Bullet trail of " + __instance.gameObject.name.Replace("(Clone)", "").Trim() + ": " +
                        (mat != null && mat.shader != null ? mat.shader.name : "no material") + (own.Glow ? " (glow)" : ""));
                }
                Paint(own);
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static void Paint(Own own)
        {
            if (own.Line == null) return;
            ColorSetting c = DorsthTweaksPlugin.BulletTrailColor;
            bool back = c.IsDefault;
            own.Line.startColor = back ? own.Start : c.Like(own.Start);
            own.Line.endColor = back ? own.End : c.Like(own.End);
            if (own.Fade != null) own.Fade.color = back ? own.FadeColor : c.Like(own.FadeColor);
            // a glow can't be darker than nothing: with a dark color the trail is drawn as a normal line instead
            Color k = c.Color;
            Material plain = own.Glow && !back && Mathf.Max(k.r, Mathf.Max(k.g, k.b)) < 0.5f ? Plain(own.Mat) : null;
            own.Line.sharedMaterial = plain != null ? plain : own.Mat;
        }

        // a new color on every trail already seen; white puts the guns' own colors back
        private static void Repaint()
        {
            var gone = new List<int>();
            foreach (var kv in Trails)
            {
                if (kv.Value.Line == null) { gone.Add(kv.Key); continue; }
                Paint(kv.Value);
            }
            foreach (int id in gone) Trails.Remove(id);
            if (DorsthTweaksPlugin.BulletTrailColor.IsDefault) Trails.Clear();
        }
    }

    // ==================================================================== viewmodel climb

    /// <summary>
    /// Every shot kicks your gun from wherever it is right then. On fast guns (like the SMG) the next shot comes before the
    /// gun is back, so the kicks pile up and the gun climbs up your screen. The game has its own fix for that, which some
    /// guns use: put the gun back in place right before each shot's kick (instantComebackOnFire). With the setting on,
    /// every gun of yours uses it. Guns that are already back before the next shot look the same.
    /// </summary>
    internal static class ViewmodelClimb
    {
        private static AccessTools.FieldRef<Weapon, bool> _comeback;

        internal static void Install()
        {
            _comeback = AccessTools.FieldRefAccess<Weapon, bool>("instantComebackOnFire");
            // a finalizer and not a postfix: it also runs when the game's method throws, so the gun's flag is always put back
            GameHook.Add(typeof(Weapon), "WeaponAnimation", null, typeof(ViewmodelClimb), "AnimationPrefix", null,
                () => DorsthTweaksPlugin.NoViewmodelClimb.Value, finalizer: "AnimationFinalizer");
            // switched in game: the hook comes on or off right away
            DorsthTweaksPlugin.OnChange(DorsthTweaksPlugin.NoViewmodelClimb, () => { });
        }

        private static void AnimationPrefix(Weapon __instance, out bool __state)
        {
            __state = false;
            try
            {
                // only ever called for your own shots
                if (_comeback(__instance) || __instance.behaviour == null) return;
                _comeback(__instance) = true;
                __state = true;
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static Exception AnimationFinalizer(Weapon __instance, bool __state, Exception __exception)
        {
            try { if (__state && __instance != null) _comeback(__instance) = false; }
            catch { }
            return __exception;
        }

    }

    // ==================================================================== bullet casings

    /// <summary>
    /// Every shot throws out a casing (EjectedCaseScript), which plays its clink when it lands. With No Bullet Casings
    /// on, a new casing is removed right away and its landing sound is skipped. Only hooked while it's on.
    /// </summary>
    internal static class BulletCasings
    {
        internal static void Install()
        {
            Func<bool> on = () => DorsthTweaksPlugin.NoBulletCasings.Value;
            GameHook.Add(typeof(EjectedCaseScript), "Awake", null, typeof(BulletCasings), null, "MadePostfix", on);
            GameHook.Add(typeof(EjectedCaseScript), "OnParticleCollision", null, typeof(BulletCasings), "LandedPrefix", null, on);
            DorsthTweaksPlugin.OnChange(DorsthTweaksPlugin.NoBulletCasings, () => { });
        }

        private static void MadePostfix(EjectedCaseScript __instance)
        {
            try { if (__instance != null) UnityEngine.Object.Destroy(__instance.gameObject); }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static bool LandedPrefix() { return false; }
    }

    // ==================================================================== FPS and ping counters

    /// <summary>
    /// Hides the minimal HUD's FPS/ping texts and the normal HUD's counters (HUD components flagged fpsDisplay/pingDisplay).
    /// A hidden counter is switched off, so it also stops rebuilding its text every frame. Only what was hidden here is shown again.
    /// </summary>
    internal static class Stats
    {
        private static readonly List<HUD> Counters = new List<HUD>();
        private static readonly HashSet<Behaviour> Hidden = new HashSet<Behaviour>();
        private static AccessTools.FieldRef<HUD, bool> _fps, _ping;

        internal static void Install()
        {
            _fps = AccessTools.FieldRefAccess<HUD, bool>("fpsDisplay");
            _ping = AccessTools.FieldRefAccess<HUD, bool>("pingDisplay");
            DorsthTweaksPlugin.Harmony.Patch(DorsthTweaksPlugin.Target(typeof(HUD), "Awake"), null, DorsthTweaksPlugin.Patch(typeof(Stats), "HudAwakePostfix"));
            DorsthTweaksPlugin.HideFps.SettingChanged += (sender, args) => ApplyAll();
            DorsthTweaksPlugin.HidePing.SettingChanged += (sender, args) => ApplyAll();
        }

        private static void HudAwakePostfix(HUD __instance)
        {
            try
            {
                if (!_fps(__instance) && !_ping(__instance)) return;
                Counters.RemoveAll(c => c == null);
                Hidden.RemoveWhere(b => b == null);
                Counters.Add(__instance);
                Apply(__instance);
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        internal static void ApplyAll()
        {
            try
            {
                Counters.RemoveAll(c => c == null);
                foreach (HUD counter in Counters) Apply(counter);
                PauseManager pm = PauseManager.Instance;
                if (pm == null) return;
                Show(pm.minimalFpsText, !DorsthTweaksPlugin.HideFps.Value);
                Show(pm.minimalPingText, !DorsthTweaksPlugin.HidePing.Value);
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static void Apply(HUD counter)
        {
            bool show = !(_fps(counter) ? DorsthTweaksPlugin.HideFps.Value : DorsthTweaksPlugin.HidePing.Value);
            Show(counter.GetComponent<Graphic>(), show);
            Show(counter, show);
            // a counter paired with a plain label (like "FPS") hides that label too
            Transform parent = counter.transform.parent;
            if (parent == null || parent.childCount != 2) return;
            Transform other = parent.GetChild(0) == counter.transform ? parent.GetChild(1) : parent.GetChild(0);
            if (other.childCount != 0 || other.GetComponent<HUD>() != null) return;
            Show(other.GetComponent<Graphic>(), show);
        }

        private static void Show(Behaviour part, bool show)
        {
            if (part == null) return;
            if (!show)
            {
                if (part.enabled && Hidden.Add(part)) part.enabled = false;
            }
            else if (Hidden.Remove(part)) part.enabled = true;
        }
    }

    // ==================================================================== minimal HUD and prompt colors

    /// <summary>
    /// The minimal HUD texts and interaction prompts live on the PauseManager, which is made once and kept. Their colors are
    /// set when it's made and when a setting changes. A prompt's glow is its material's outline, changed on a copy of the material.
    /// </summary>
    internal static class TextColors
    {
        private static readonly Dictionary<TMP_Text, Color> Originals = new Dictionary<TMP_Text, Color>();
        private static readonly Dictionary<TMP_Text, Material> GameMaterials = new Dictionary<TMP_Text, Material>();
        private static readonly Dictionary<TMP_Text, Material> Copies = new Dictionary<TMP_Text, Material>();

        internal static void Install()
        {
            DorsthTweaksPlugin.Harmony.Patch(DorsthTweaksPlugin.Target(typeof(PauseManager), "Awake"), null, DorsthTweaksPlugin.Patch(typeof(TextColors), "PauseManagerPostfix"));
            DorsthTweaksPlugin.Harmony.Patch(DorsthTweaksPlugin.Target(typeof(FirstPersonController), "Awake___UserLogic"), null, DorsthTweaksPlugin.Patch(typeof(TextColors), "PlayerPostfix"));
            DorsthTweaksPlugin.MinimalHudColor.Changed += ApplyHud;
            DorsthTweaksPlugin.PromptColor.Changed += ApplyPrompts;
            DorsthTweaksPlugin.HidePickupPrompts.SettingChanged += (s, e) => ApplyPrompts();
            DorsthTweaksPlugin.HideInteractionPrompts.SettingChanged += (s, e) => ApplyPrompts();
            DorsthTweaksPlugin.PromptX.SettingChanged += (s, e) => ApplyPrompts();
            DorsthTweaksPlugin.PromptY.SettingChanged += (s, e) => ApplyPrompts();
            DorsthTweaksPlugin.PromptScale.SettingChanged += (s, e) => ApplyPrompts();
        }

        // The speedometer is in each map, not with the rest of the minimal HUD: the player finds it when it's made.
        private static readonly AccessTools.FieldRef<FirstPersonController, TextMeshProUGUI> SpeedometerOf =
            AccessTools.FieldRefAccess<FirstPersonController, TextMeshProUGUI>("speedometer");
        private static TMP_Text _speedometer;

        private static void PlayerPostfix(FirstPersonController __instance)
        {
            try
            {
                TMP_Text speedometer = SpeedometerOf(__instance);
                if (speedometer == null || speedometer == _speedometer) return;
                // the last map's speedometer is gone: forget its color
                if ((object)_speedometer != null) Originals.Remove(_speedometer);
                _speedometer = speedometer;
                Apply(speedometer, DorsthTweaksPlugin.MinimalHudColor);
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static void PauseManagerPostfix(PauseManager __instance)
        {
            if (PauseManager.Instance != __instance) return; // a duplicate the game deletes right away
            ApplyHud();
            ApplyPrompts();
            Stats.ApplyAll();
        }

        private static void ApplyHud()
        {
            try
            {
                PauseManager pm = PauseManager.Instance;
                if (pm == null) return;
                var texts = new HashSet<TMP_Text>();
                if (pm.minimalistUi != null) texts.UnionWith(pm.minimalistUi.GetComponentsInChildren<TMP_Text>(true));
                foreach (TMP_Text t in new TMP_Text[] { pm.minimalistHealthText, pm.minimalFpsText, pm.minimalPingText, pm.rightGunAmmo, pm.leftGunAmmo, pm.rightGunAmmoReload, pm.leftGunAmmoReload })
                    if (t != null) texts.Add(t);
                if (_speedometer != null) texts.Add(_speedometer);
                foreach (TMP_Text t in texts) Apply(t, DorsthTweaksPlugin.MinimalHudColor);
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static void ApplyPrompts()
        {
            try
            {
                PauseManager pm = PauseManager.Instance;
                if (pm == null) return;
                ApplyGlow(pm.grabPopup, DorsthTweaksPlugin.PromptColor);
                ApplyGlow(pm.interactPopup, DorsthTweaksPlugin.PromptColor);
                Show(pm.grabPopup, !DorsthTweaksPlugin.HidePickupPrompts.Value);
                Show(pm.interactPopup, !DorsthTweaksPlugin.HideInteractionPrompts.Value);
                Place(pm.grabPopup);
                Place(pm.interactPopup);
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        // where each prompt was and its size, before any offset (the game never moves them itself)
        private static readonly Dictionary<RectTransform, KeyValuePair<Vector2, Vector3>> Placed = new Dictionary<RectTransform, KeyValuePair<Vector2, Vector3>>();

        /// <summary>Prompt X/Y Offset and Prompt Scale: from where the game put the prompt.</summary>
        private static void Place(TMP_Text text)
        {
            if (text == null) return;
            RectTransform rt = text.rectTransform;
            if (!Placed.TryGetValue(rt, out var was))
            {
                was = new KeyValuePair<Vector2, Vector3>(rt.anchoredPosition, rt.localScale);
                Placed[rt] = was;
            }
            rt.anchoredPosition = was.Key + new Vector2(DorsthTweaksPlugin.PromptX.Value, DorsthTweaksPlugin.PromptY.Value);
            // (0.5 is drawn at 0.51: at exactly half size the game's font draws the prompt brighter)
            rt.localScale = was.Value * Mathf.Max(0.51f, DorsthTweaksPlugin.PromptScale.Value);
        }

        /// <summary>
        /// The game switches a prompt on and off itself whenever you look at something, so a hidden one is made fully
        /// see-through instead (a CanvasGroup also covers the extra letter meshes of a fallback font).
        /// </summary>
        private static void Show(TMP_Text text, bool show)
        {
            if (text == null) return;
            CanvasGroup group = text.GetComponent<CanvasGroup>();
            if (group == null)
            {
                if (show) return;
                group = text.gameObject.AddComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;
            }
            group.alpha = show ? 1f : 0f;
        }

        private static void ApplyGlow(TMP_Text text, ColorSetting setting)
        {
            if (text == null) return;
            Material game;
            bool changedBefore = GameMaterials.TryGetValue(text, out game);
            if (!changedBefore) game = text.fontSharedMaterial;
            if (game == null || !game.HasProperty(ShaderUtilities.ID_OutlineColor) || game.GetFloat(ShaderUtilities.ID_OutlineWidth) <= 0f)
            {
                Apply(text, setting); // no glow: color the letters
                return;
            }
            // a small prompt: the game's glow is a pixel or two at full size and thins out to nothing when scaled down,
            // so the letters take on half the glow's color by 0.6 (what white letters in the glow look like at full size) (a wider glow shows boxes)
            float scale = DorsthTweaksPlugin.PromptScale.Value, tint = 0.5f * Mathf.InverseLerp(1f, 0.6f, scale); // half way: white letters in a colored glow read as about that
            Color face;
            if (!Originals.TryGetValue(text, out face)) Originals[text] = face = text.color;
            Color glowNow = setting.IsDefault ? game.GetColor(ShaderUtilities.ID_OutlineColor) : setting.Like(game.GetColor(ShaderUtilities.ID_OutlineColor));
            text.color = Color.Lerp(face, new Color(glowNow.r, glowNow.g, glowNow.b, face.a), tint);
            float thicker = 1f;
            if (setting.IsDefault && thicker == 1f)
            {
                if (!changedBefore) return;
                Material copy = Copies[text];
                text.fontSharedMaterial = game;
                GameMaterials.Remove(text);
                Copies.Remove(text);
                if (copy != null) UnityEngine.Object.Destroy(copy);
                return;
            }
            Material mine;
            if (!Copies.TryGetValue(text, out mine) || mine == null)
            {
                mine = new Material(game) { name = game.name + " (DorsthTweaks)" };
                GameMaterials[text] = game;
                Copies[text] = mine;
            }
            Color glow = game.GetColor(ShaderUtilities.ID_OutlineColor);
            mine.SetColor(ShaderUtilities.ID_OutlineColor, setting.IsDefault ? glow : setting.Like(glow));
            mine.SetFloat(ShaderUtilities.ID_OutlineWidth, game.GetFloat(ShaderUtilities.ID_OutlineWidth));
            if (text.fontSharedMaterial != mine) text.fontSharedMaterial = mine;
            text.UpdateMeshPadding();
        }

        private static void Apply(TMP_Text text, ColorSetting setting)
        {
            if (text == null) return;
            Color original;
            bool changedBefore = Originals.TryGetValue(text, out original);
            if (setting.IsDefault)
            {
                if (changedBefore) text.color = original;
                return;
            }
            if (!changedBefore) Originals[text] = original = text.color;
            Color c = setting.Color;
            text.color = new Color(c.r, c.g, c.b, c.a * original.a);
        }
    }

    // ==================================================================== weapon highlight

    /// <summary>
    /// While you look at a weapon the game calls its OnFocus every frame and turns the outline on. The outline color is set
    /// right after (last, after other mods). At the default color nothing is done; switching back restores it.
    /// </summary>
    internal static class WeaponHighlight
    {
        // by material id: every weapon has its own copies of its materials, and a kept reference would keep them all in memory
        private static readonly Dictionary<int, Color> Originals = new Dictionary<int, Color>();
        private static AccessTools.FieldRef<ItemBehaviour, List<Material>> _materials;
        private static int _outlineColor;

        internal static void Install()
        {
            _materials = AccessTools.FieldRefAccess<ItemBehaviour, List<Material>>("hoveredObjectMat");
            _outlineColor = Shader.PropertyToID("_Color_Outline");
            // only hooked while the color isn't the default. Back to default: the outlines get their own color back at once
            GameHook.Add(typeof(ItemBehaviour), "OnFocus", null, typeof(WeaponHighlight), null, "FocusPostfix",
                () => !DorsthTweaksPlugin.WeaponHighlightColor.IsDefault, -1, Priority.Last);
            DorsthTweaksPlugin.WeaponHighlightColor.Changed += () =>
            {
                if (DorsthTweaksPlugin.WeaponHighlightColor.IsDefault) Restore();
                GameHook.RefreshAll();
            };
            // weapons of the last map are gone with it: only the colors of materials that still exist are kept
            Scenes.Loaded += (scene, mode) =>
            {
                if (DorsthTweaksPlugin.WeaponHighlightColor.IsDefault || Originals.Count == 0) { Originals.Clear(); return; }
                var alive = new HashSet<int>();
                foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                    if (m != null) alive.Add(m.GetInstanceID());
                var gone = new List<int>();
                foreach (int id in Originals.Keys)
                    if (!alive.Contains(id)) gone.Add(id);
                foreach (int id in gone) Originals.Remove(id);
            };
        }

        private static void Restore()
        {
            if (Originals.Count == 0) return;
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                Color c;
                if (m != null && Originals.TryGetValue(m.GetInstanceID(), out c)) m.SetColor(_outlineColor, c);
            }
            Originals.Clear();
        }

        private static void FocusPostfix(ItemBehaviour __instance)
        {
            try
            {
                ColorSetting setting = DorsthTweaksPlugin.WeaponHighlightColor;
                if (setting.IsDefault && Originals.Count == 0) return;
                List<Material> materials = _materials(__instance);
                if (materials == null) return;
                foreach (Material m in materials)
                {
                    if (m == null) continue;
                    int id = m.GetInstanceID();
                    Color original;
                    if (!Originals.TryGetValue(id, out original))
                    {
                        if (setting.IsDefault || !m.HasProperty(_outlineColor)) continue;
                        Originals[id] = original = m.GetColor(_outlineColor);
                    }
                    else if (setting.IsDefault)
                    {
                        m.SetColor(_outlineColor, original);
                        Originals.Remove(id);
                        continue;
                    }
                    m.SetColor(_outlineColor, setting.Like(original));
                }
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }
    }

    // ==================================================================== join message

    /// <summary>
    /// Join Message. When you join a lobby your game writes "&lt;name&gt; joined the lobby" to the match log, and the host
    /// sends it on to everyone. That one line, and only when it's yours, is swapped for your own text on its way out, so
    /// everyone sees it, with or without DorsthTweaks. Your name keeps its color.
    /// </summary>
    internal static class JoinLine
    {
        private const string GameEnding = " joined the lobby";
        private const int MaxLength = 120;

        internal static void Install()
        {
            // only hooked while there's a message
            GameHook.Add(typeof(PauseManager), "WriteLog", new[] { typeof(string) }, typeof(JoinLine), "WriteLogPrefix", null,
                () => !string.IsNullOrWhiteSpace(DorsthTweaksPlugin.JoinMessage.Value));
            DorsthTweaksPlugin.JoinMessage.SettingChanged += (sender, args) => GameHook.RefreshAll();
        }

        private static void WriteLogPrefix(ref string text)
        {
            try
            {
                if (text == null || !text.EndsWith(GameEnding, StringComparison.Ordinal)) return;
                var names = PlayerNameGradientManager.Instance;
                string me = names != null ? names.GetPersonaName() : null;
                if (string.IsNullOrEmpty(me) || text != me + GameEnding) return;
                string line = Build(DorsthTweaksPlugin.JoinMessage.Value, me);
                if (line != null) text = line;
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        // the message with the name in it ({name}, or {USER} like StraftTweaks), on one line and not too long
        private static string Build(string template, string me)
        {
            if (string.IsNullOrWhiteSpace(template)) return null;
            string t = template.Replace("\r", " ").Replace("\n", " ").Trim();
            // everyone in the lobby sees it: tags that change its size or place are taken out (color, bold, italic,
            // underline and strikethrough stay), again until nothing changes, so a tag can't be hidden inside another
            for (int i = 0; i < 8; i++)
            {
                string before = t;
                t = Regex.Replace(t, @"</?(?!(?:color|b|i|u|s)(?=[\s=>])|#)[^<>]*>", "");
                if (t == before) break;
            }
            t = t.Trim();
            if (t.Length > MaxLength) t = t.Substring(0, MaxLength);
            return t.Replace("{name}", me).Replace("{NAME}", me).Replace("{USER}", me).Replace("{user}", me);
        }
    }

    // ==================================================================== near miss vignette

    /// <summary>Shots landing near you call CameraEffect.TrigSup (the vignette). The near-miss sound is separate and still plays.</summary>
    internal static class NearMiss
    {
        internal static void Install()
        {
            // only hooked while it's on
            GameHook.Add(typeof(CameraEffect), "TrigSup", null, typeof(NearMiss), "VignettePrefix", null,
                () => DorsthTweaksPlugin.HideNearMissVignette.Value);
            DorsthTweaksPlugin.HideNearMissVignette.SettingChanged += (sender, args) => GameHook.RefreshAll();
        }

        private static bool VignettePrefix() { return false; }
    }

    /// <summary>
    /// The game's CameraEffect (the red damage flash and the dark near-miss edges) is an image effect: while it's
    /// enabled Unity copies the whole screen through its shader every frame, also when both are at zero and the
    /// picture comes out the same. Here it's switched off while there's nothing to show and on again the moment you're
    /// hit (TakeHit) or shot near (TrigSup). Same picture, one full-screen pass less per frame. Always on.
    /// </summary>
    internal static class IdleCameraEffect
    {
        private static readonly int Damage = Shader.PropertyToID("_Damage");
        private static readonly int Suppression = Shader.PropertyToID("_Suppression");

        internal static void Install()
        {
            Type me = typeof(IdleCameraEffect);
            DorsthTweaksPlugin.Harmony.Patch(DorsthTweaksPlugin.Target(typeof(CameraEffect), "Awake"), postfix: DorsthTweaksPlugin.Patch(me, nameof(SleepPostfix)));
            DorsthTweaksPlugin.Harmony.Patch(DorsthTweaksPlugin.Target(typeof(CameraEffect), "TakeHit"), postfix: DorsthTweaksPlugin.Patch(me, nameof(WakePostfix)));
            DorsthTweaksPlugin.Harmony.Patch(DorsthTweaksPlugin.Target(typeof(CameraEffect), "TrigSup"), postfix: DorsthTweaksPlugin.Patch(me, nameof(WakePostfix)));
            DorsthTweaksPlugin.Harmony.Patch(DorsthTweaksPlugin.Target(typeof(CameraEffect), "OnRenderImage"), postfix: DorsthTweaksPlugin.Patch(me, nameof(DonePostfix)));
        }

        private static void SleepPostfix(CameraEffect __instance)
        {
            try { if (__instance != null && __instance.material != null) __instance.enabled = false; }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static void WakePostfix(CameraEffect __instance)
        {
            try { if (__instance != null && !__instance.enabled) __instance.enabled = true; }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        /// <summary>After it has drawn a frame: off again once both the flash and the edges have faded to nothing.</summary>
        private static void DonePostfix(CameraEffect __instance)
        {
            try
            {
                Material m = __instance != null ? __instance.material : null;
                if (m != null && m.GetFloat(Damage) <= 0f && m.GetFloat(Suppression) <= 0f) __instance.enabled = false;
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }
    }

    // ==================================================================== reduce UI animations

    /// <summary>
    /// Reduce UI Animations. Everything changed here is put back when it's turned off.
    ///  - Buttons don't grow on hover, 3D menu buttons don't push in, nothing pulses, blinks or spins.
    ///  - UI tweens (sliding panels, banners, map tiles) jump to their end before the frame is drawn. Hitmarkers keep their punch.
    ///  - Fades on buttons, toggles, dropdowns and the text caret are instant.
    ///  - Menu particles stop, animated menu materials and the menu sky stand still.
    ///  - HUD: no ammo bounce (the game starts a new tween for it every frame), no heartbeat, no slide-in.
    /// </summary>
    internal static class StaticUI
    {
        private static readonly Func<bool> On = () => DorsthTweaksPlugin.ReduceUIAnimations.Value;

        private static AccessTools.FieldRef<HUD, TMP_Text> _hudText;
        private static AccessTools.FieldRef<HUD, PauseManager> _hudPause;
        private static AccessTools.FieldRef<HUD, bool> _ammoRight, _ammoLeft;
        private static AccessTools.FieldRef<HealthTween, TMP_Text> _healthText;
        private static AccessTools.FieldRef<HealthTween, Color> _firstColor, _lastColor;
        private static AccessTools.FieldRef<HUDTween, Transform> _hudUp, _hudDown;
        private static AccessTools.FieldRef<HUDTween, bool> _slidIn;
        private static AccessTools.FieldRef<HUDTween, ClientInstance> _client;
        private static int _healthShownBy, _healthShown = int.MinValue;

        private static AccessTools.FieldRef<ButtonSizeTween, Vector3> _restScale;
        private static AccessTools.FieldRef<ButtonSizeTween, bool> _scaleTextOnly, _scaleParent;
        private static AccessTools.FieldRef<ButtonSizeTween, TextMeshProUGUI> _scaledText;
        private static AccessTools.FieldRef<ModelButtonTween, Vector3> _modelRest;
        private static AccessTools.FieldRef<ScaleTweenLoop, bool> _loopOnStart;
        private static AccessTools.FieldRef<AlphaYoyo, TextMeshProUGUI> _yoyoText;
        private static AccessTools.FieldRef<AlphaYoyo, Color> _yoyoColor;
        private static AccessTools.FieldRef<AboubiPreviewLobby, bool> _runningIn;
        private static MethodInfo _loopStart;

        private struct ParticleState
        {
            public bool Emission, Playing;
        }

        private static readonly Dictionary<Selectable, float> Fades = new Dictionary<Selectable, float>();
        private static readonly Dictionary<Toggle, Toggle.ToggleTransition> ToggleFades = new Dictionary<Toggle, Toggle.ToggleTransition>();
        private static readonly Dictionary<Selectable, float> ListFades = new Dictionary<Selectable, float>();
        private static readonly Dictionary<TMP_InputField, float> Carets = new Dictionary<TMP_InputField, float>();
        private static readonly Dictionary<ParticleSystem, ParticleState> Particles = new Dictionary<ParticleSystem, ParticleState>();
        private static readonly HashSet<ScaleTweenLoop> StoppedLoops = new HashSet<ScaleTweenLoop>();

        internal static void Install()
        {
            _hudText = AccessTools.FieldRefAccess<HUD, TMP_Text>("text");
            _hudPause = AccessTools.FieldRefAccess<HUD, PauseManager>("pauseManager");
            _ammoRight = AccessTools.FieldRefAccess<HUD, bool>("ammoRight");
            _ammoLeft = AccessTools.FieldRefAccess<HUD, bool>("ammoLeft");
            _healthText = AccessTools.FieldRefAccess<HealthTween, TMP_Text>("text");
            _firstColor = AccessTools.FieldRefAccess<HealthTween, Color>("firstColor");
            _lastColor = AccessTools.FieldRefAccess<HealthTween, Color>("lastColor");
            _hudUp = AccessTools.FieldRefAccess<HUDTween, Transform>("hudUp");
            _hudDown = AccessTools.FieldRefAccess<HUDTween, Transform>("hudDown");
            _slidIn = AccessTools.FieldRefAccess<HUDTween, bool>("trigger");
            _client = AccessTools.FieldRefAccess<HUDTween, ClientInstance>("clientScript");
            _restScale = AccessTools.FieldRefAccess<ButtonSizeTween, Vector3>("initScale");
            _scaleTextOnly = AccessTools.FieldRefAccess<ButtonSizeTween, bool>("scaleTextOnly");
            _scaleParent = AccessTools.FieldRefAccess<ButtonSizeTween, bool>("parentScale");
            _scaledText = AccessTools.FieldRefAccess<ButtonSizeTween, TextMeshProUGUI>("textChild");
            _modelRest = AccessTools.FieldRefAccess<ModelButtonTween, Vector3>("initialPosition");
            _loopOnStart = AccessTools.FieldRefAccess<ScaleTweenLoop, bool>("loopOnStart");
            _yoyoText = AccessTools.FieldRefAccess<AlphaYoyo, TextMeshProUGUI>("text");
            _yoyoColor = AccessTools.FieldRefAccess<AlphaYoyo, Color>("initialColor");
            _runningIn = AccessTools.FieldRefAccess<AboubiPreviewLobby, bool>("isRunning");
            _loopStart = DorsthTweaksPlugin.Target(typeof(ScaleTweenLoop), "Start");

            Type me = typeof(StaticUI);
            GameHook.Add(typeof(HUD), "Update", null, me, "AmmoPrefix", null, On);
            GameHook.Add(typeof(HealthTween), "Update", null, me, "HeartbeatPrefix", null, On);
            GameHook.Add(typeof(HealthTween), "ChangeState", null, me, "HealthChangePrefix", null, On);
            GameHook.Add(typeof(HUDTween), "Update", null, me, "SlideInPrefix", null, On);
            GameHook.Add(typeof(HUDTween), "MoveUp", null, me, "MoveUpPrefix", null, On);
            // hover and press
            foreach (string m in new[] { "OnPointerEnter", "OnPointerExit", "OnSelect", "OnDeselect" })
                GameHook.Add(typeof(ButtonSizeTween), m, null, me, null, "StayPostfix", On);
            foreach (string m in new[] { "Hover", "Leave", "Press", "Release" })
                GameHook.Add(typeof(ModelButtonTween), m, null, me, "Skip", null, On);
            // pulsing, blinking, spinning
            GameHook.Add(typeof(ScaleTweenLoop), "Start", null, me, "LoopStartPrefix", null, On);
            GameHook.Add(typeof(ScaleTweenLoop), "TempTween", null, me, "Skip", null, On);
            GameHook.Add(typeof(AlphaYoyo), "Update", null, me, "Skip", null, On);
            GameHook.Add(typeof(AboubiPreview), "Update", null, me, "Skip", null, On);
            GameHook.Add(typeof(AboubiPreviewLobby), "Update", null, me, null, "LobbyPostfix", On);
            GameHook.Add(typeof(MenuAnimation), "Update", null, me, "Skip", null, On);
            GameHook.Add(typeof(Rotate), "Update", null, me, "SkipInMenu", null, On);
            // sliding panels
            foreach (string m in new[] { "ChangeState", "SetEnabled", "SetDisabled" })
                GameHook.Add(typeof(MenuHUDTween), m, null, me, null, "FinishPostfix", On);
            foreach (string m in new[] { "ChangeState", "ChooseState" })
                GameHook.Add(typeof(MoveUIObject), m, null, me, null, "FinishPostfix", On);
            // UI made later (map lists, playlists, cosmetics)
            GameHook.Add(typeof(Selectable), "OnEnable", null, me, null, "SelectableEnablePostfix", On);
            // any tween on a UI object
            Type dt = typeof(ShortcutExtensions);
            GameHook.Add(dt, "DOMove", new[] { typeof(Transform), typeof(Vector3), typeof(float), typeof(bool) }, me, null, "TweenPostfix", On);
            GameHook.Add(dt, "DOLocalMove", new[] { typeof(Transform), typeof(Vector3), typeof(float), typeof(bool) }, me, null, "TweenPostfix", On);
            GameHook.Add(dt, "DOScale", new[] { typeof(Transform), typeof(Vector3), typeof(float) }, me, null, "TweenPostfix", On);
            GameHook.Add(dt, "DOScale", new[] { typeof(Transform), typeof(float), typeof(float) }, me, null, "TweenPostfix", On);
            GameHook.Add(dt, "DOPunchScale", new[] { typeof(Transform), typeof(Vector3), typeof(float), typeof(int), typeof(float) }, me, null, "TweenPostfix", On);
            GameHook.Add(typeof(DOTweenModuleUI), "DOColor", new[] { typeof(Image), typeof(Color), typeof(float) }, me, null, "ImageTweenPostfix", On);

            DorsthTweaksPlugin.OnChange(DorsthTweaksPlugin.ReduceUIAnimations, Apply);
            Scenes.Loaded += OnSceneLoaded;
        }

        private static void Apply()
        {
            if (On()) Still();
            else Restore();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // What's remembered of buttons, toggles and lists that are gone is dropped at every scene load, not only in the
            // main menu: the HUD makes new ones all through a match, and remembering the destroyed ones kept them (and
            // everything they point to) in memory for the whole session.
            if (Fades.Count > 0 || ToggleFades.Count > 0 || ListFades.Count > 0 || Carets.Count > 0 || Particles.Count > 0)
            {
                Scenes.PruneGone(Fades);
                Scenes.PruneGone(ToggleFades);
                Scenes.PruneGone(ListFades);
                Scenes.PruneGone(Carets);
                Scenes.PruneGone(Particles);
            }
            if (!On() || scene.name != Scenes.MainMenu) return;
            try { Still(); }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static bool InMenu(Component c) { return c != null && Scenes.IsMenu(c.gameObject.scene); }

        private static bool InMainMenuScene(Component c) { return c != null && c.gameObject.scene.name == Scenes.MainMenu; }

        /// <summary>One pass over the menus (when turned on, and when the main menu loads).</summary>
        private static void Still()
        {
            Scenes.PruneGone(Fades);
            Scenes.PruneGone(ToggleFades);
            Scenes.PruneGone(ListFades);
            Scenes.PruneGone(Carets);
            Scenes.PruneGone(Particles);
            StoppedLoops.RemoveWhere(loop => loop == null);
            foreach (UnityEngine.Object o in Scenes.FindAll(typeof(Selectable)))
            {
                var s = (Selectable)o;
                if (InMenu(s)) StillSelectable(s);
            }
            foreach (UnityEngine.Object o in Scenes.FindAll(typeof(ParticleSystem)))
            {
                var ps = (ParticleSystem)o;
                if (InMainMenuScene(ps)) StillParticles(ps);
            }
            foreach (UnityEngine.Object o in Scenes.FindAll(typeof(Renderer)))
            {
                var r = (Renderer)o;
                if (r is ParticleSystemRenderer || !InMainMenuScene(r)) continue;
                foreach (Material m in r.sharedMaterials) Stills.Freeze(m, Stills.Menu);
            }
            foreach (UnityEngine.Object o in Scenes.FindAll(typeof(Graphic)))
            {
                var g = (Graphic)o;
                if (g is TMP_Text || g is TMP_SubMeshUI || !InMenu(g)) continue; // their material getter would make copies
                Stills.Freeze(g.material, Stills.Menu);
            }
            World.StillSky();

            // things caught mid-move
            foreach (UnityEngine.Object o in Scenes.FindAll(typeof(ButtonSizeTween)))
            {
                var b = (ButtonSizeTween)o;
                if (b.isActiveAndEnabled) Stay(b);
            }
            foreach (UnityEngine.Object o in Scenes.FindAll(typeof(ModelButtonTween)))
            {
                var b = (ModelButtonTween)o;
                Vector3 rest = _modelRest(b);
                if (rest == Vector3.zero) continue; // not started yet
                b.transform.DOKill();
                b.transform.localPosition = rest;
            }
            foreach (UnityEngine.Object o in Scenes.FindAll(typeof(ScaleTweenLoop)))
            {
                var loop = (ScaleTweenLoop)o;
                Transform t = loop.transform;
                if (!DOTween.IsTweening(t)) continue;
                t.DORewind();
                t.DOKill();
                if (_loopOnStart(loop)) StoppedLoops.Add(loop);
            }
            foreach (UnityEngine.Object o in Scenes.FindAll(typeof(AlphaYoyo)))
            {
                var y = (AlphaYoyo)o;
                TextMeshProUGUI text = _yoyoText(y);
                if (text != null) text.color = _yoyoColor(y);
            }
        }

        private static void SelectableEnablePostfix(Selectable __instance)
        {
            try
            {
                if (InMenu(__instance)) StillSelectable(__instance);
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static void StillSelectable(Selectable s)
        {
            ColorBlock colors = s.colors;
            if (colors.fadeDuration > 0f && !Fades.ContainsKey(s))
            {
                Fades[s] = colors.fadeDuration;
                colors.fadeDuration = 0f;
                s.colors = colors;
            }
            var toggle = s as Toggle;
            if (toggle != null && toggle.toggleTransition != Toggle.ToggleTransition.None && !ToggleFades.ContainsKey(toggle))
            {
                ToggleFades[toggle] = toggle.toggleTransition;
                toggle.toggleTransition = Toggle.ToggleTransition.None;
            }
            var dropdown = s as Dropdown;
            if (dropdown != null && dropdown.alphaFadeSpeed > 0f && !ListFades.ContainsKey(s))
            {
                ListFades[s] = dropdown.alphaFadeSpeed;
                dropdown.alphaFadeSpeed = 0f;
            }
            var tmpDropdown = s as TMP_Dropdown;
            if (tmpDropdown != null && tmpDropdown.alphaFadeSpeed > 0f && !ListFades.ContainsKey(s))
            {
                ListFades[s] = tmpDropdown.alphaFadeSpeed;
                tmpDropdown.alphaFadeSpeed = 0f;
            }
            var input = s as TMP_InputField;
            if (input != null && input.caretBlinkRate > 0f && !Carets.ContainsKey(input))
            {
                Carets[input] = input.caretBlinkRate;
                input.caretBlinkRate = 0f; // always shown
            }
        }

        private static void StillParticles(ParticleSystem ps)
        {
            if (Particles.ContainsKey(ps)) return;
            ParticleSystem.EmissionModule emission = ps.emission;
            Particles[ps] = new ParticleState { Emission = emission.enabled, Playing = ps.isPlaying };
            emission.enabled = false;
            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static void Restore()
        {
            foreach (KeyValuePair<Selectable, float> kv in Fades)
            {
                if (kv.Key == null) continue;
                ColorBlock colors = kv.Key.colors;
                colors.fadeDuration = kv.Value;
                kv.Key.colors = colors;
            }
            Fades.Clear();
            foreach (KeyValuePair<Toggle, Toggle.ToggleTransition> kv in ToggleFades)
                if (kv.Key != null) kv.Key.toggleTransition = kv.Value;
            ToggleFades.Clear();
            foreach (KeyValuePair<Selectable, float> kv in ListFades)
            {
                var dropdown = kv.Key as Dropdown;
                if (dropdown != null) dropdown.alphaFadeSpeed = kv.Value;
                var tmpDropdown = kv.Key as TMP_Dropdown;
                if (tmpDropdown != null) tmpDropdown.alphaFadeSpeed = kv.Value;
            }
            ListFades.Clear();
            foreach (KeyValuePair<TMP_InputField, float> kv in Carets)
                if (kv.Key != null) kv.Key.caretBlinkRate = kv.Value;
            Carets.Clear();
            foreach (KeyValuePair<ParticleSystem, ParticleState> kv in Particles)
            {
                ParticleSystem ps = kv.Key;
                if (ps == null) continue;
                ParticleSystem.EmissionModule emission = ps.emission;
                emission.enabled = kv.Value.Emission;
                if (kv.Value.Playing) ps.Play(false);
            }
            Particles.Clear();
            Stills.Release(Stills.Menu);
            foreach (ScaleTweenLoop loop in StoppedLoops)
                if (loop != null && loop.isActiveAndEnabled && !DOTween.IsTweening(loop.transform)) _loopStart.Invoke(loop, null);
            StoppedLoops.Clear();
        }

        private static bool Skip() { return false; }

        private static bool SkipInMenu() { return !Scenes.InMainMenu(); }

        private static void StayPostfix(ButtonSizeTween __instance)
        {
            try { Stay(__instance); }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        /// <summary>The button stays its normal size (hover sounds and tooltips still happen).</summary>
        private static void Stay(ButtonSizeTween b)
        {
            Transform t;
            if (_scaleTextOnly(b))
            {
                TextMeshProUGUI text = _scaledText(b);
                t = text != null ? text.transform : null;
            }
            else t = _scaleParent(b) ? b.transform.parent : b.transform;
            Vector3 rest = _restScale(b);
            // scale 0 is how the game hides some buttons: leave those hidden
            if (t == null || rest == Vector3.zero || t.localScale == Vector3.zero) return;
            t.DOKill();
            t.localScale = rest;
        }

        /// <summary>Lobby player previews stop turning and face the front.</summary>
        private static void LobbyPostfix(AboubiPreviewLobby __instance)
        {
            if (!_runningIn(__instance)) __instance.transform.forward = -Vector3.forward;
        }

        /// <summary>A button that pulses for attention doesn't start; it's noted so it can start when this is turned off.</summary>
        private static bool LoopStartPrefix(ScaleTweenLoop __instance)
        {
            if (_loopOnStart(__instance)) StoppedLoops.Add(__instance);
            return false;
        }

        private static void FinishPostfix(MonoBehaviour __instance)
        {
            try { __instance.transform.DOComplete(); }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static void TweenPostfix(Transform target, object __result)
        {
            try
            {
                if (!(target is RectTransform) || Hitmarkers.IsHitmarker(target)) return;
                Queue(__result as Tween, target);
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static void ImageTweenPostfix(Image target, object __result)
        {
            try { Queue(__result as Tween, target); }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private struct NewTween
        {
            public Tween Tween;
            public object Target;
        }

        private static readonly List<NewTween> NewTweens = new List<NewTween>();
        private static bool _finishing;

        private static void Queue(Tween tween, object target)
        {
            if (tween == null) return;
            NewTweens.Add(new NewTween { Tween = tween, Target = target });
            if (_finishing) return;
            Canvas.preWillRenderCanvases += FinishTweens;
            _finishing = true;
        }

        /// <summary>
        /// Before the UI is drawn, on frames that started UI tweens: each jumps to its end with its callbacks (by now the game
        /// has set them up). An endless one goes back to its start and stops.
        /// </summary>
        private static void FinishTweens()
        {
            try
            {
                for (int i = 0; i < NewTweens.Count && i < 1000; i++) // a callback can start another tween: that one too
                {
                    Tween t = NewTweens[i].Tween;
                    if (!t.IsActive() || !ReferenceEquals(t.target, NewTweens[i].Target)) continue; // stopped, or reused
                    if (t.Loops() == -1)
                    {
                        t.Rewind();
                        t.Kill();
                    }
                    else t.Complete(true);
                }
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
            finally
            {
                NewTweens.Clear();
                Canvas.preWillRenderCanvases -= FinishTweens;
                _finishing = false;
            }
        }

        /// <summary>HUD.Update for the ammo counters: copy the ammo text, no tweens. Other counters run as normal.</summary>
        private static bool AmmoPrefix(HUD __instance)
        {
            try
            {
                bool right = _ammoRight(__instance), left = _ammoLeft(__instance);
                if (!right && !left) return true;
                PauseManager pm = _hudPause(__instance);
                TMP_Text text = _hudText(__instance);
                if (pm == null || text == null) return true;
                // every frame: only written when it changed, so the text isn't rebuilt for nothing
                string ammo = right ? pm.rightGunAmmo.text : pm.leftGunAmmo.text;
                if (text.text != ammo) text.text = ammo;
                return false;
            }
            catch (Exception e)
            {
                DorsthTweaksPlugin.LogErrorOnce(e);
                return true;
            }
        }

        /// <summary>HealthTween.Update: the health number without the heartbeat, only rewritten when it changes.</summary>
        private static bool HeartbeatPrefix(HealthTween __instance)
        {
            try
            {
                TMP_Text text = _healthText(__instance);
                if (text == null) return true;
                int value = (int)Mathf.Ceil(__instance.health / 4f * 100f);
                int id = __instance.GetInstanceID();
                if (value != _healthShown || id != _healthShownBy)
                {
                    text.text = value.ToString(CultureInfo.InvariantCulture);
                    _healthShown = value;
                    _healthShownBy = id;
                }
                return false;
            }
            catch (Exception e)
            {
                DorsthTweaksPlugin.LogErrorOnce(e);
                return true;
            }
        }

        /// <summary>HealthTween.ChangeState: the health color change without the bounce.</summary>
        private static bool HealthChangePrefix(HealthTween __instance)
        {
            try
            {
                TMP_Text text = _healthText(__instance);
                if (text == null) return true;
                __instance.transform.DOKill();
                __instance.transform.localScale = Vector3.one;
                Color color = Color.Lerp(_firstColor(__instance), _lastColor(__instance), 1f - __instance.health / 4f);
                text.color = color;
                text.fontSharedMaterial.SetColor(ShaderUtilities.ID_GlowColor, color);
                text.outlineColor = color;
                _healthShown = int.MinValue; // rewrite the number next frame
                return false;
            }
            catch (Exception e)
            {
                DorsthTweaksPlugin.LogErrorOnce(e);
                return true;
            }
        }

        /// <summary>HUDTween.Update: the HUD appears in place at round start instead of sliding in.</summary>
        private static bool SlideInPrefix(HUDTween __instance)
        {
            try
            {
                ClientInstance client = _client(__instance);
                if (client != null && client.nonSteamworksTransport) Snap(__instance);
                if (!_slidIn(__instance) && PauseManager.Instance != null && PauseManager.Instance.onStartRoundScreen)
                {
                    _slidIn(__instance) = true;
                    Snap(__instance);
                }
                return false;
            }
            catch (Exception e)
            {
                DorsthTweaksPlugin.LogErrorOnce(e);
                return true;
            }
        }

        private static bool MoveUpPrefix(HUDTween __instance)
        {
            try
            {
                Snap(__instance);
                return false;
            }
            catch (Exception e)
            {
                DorsthTweaksPlugin.LogErrorOnce(e);
                return true;
            }
        }

        // called every frame offline: nothing made, and only a part not already in place is touched
        private static void Snap(HUDTween tween)
        {
            SnapPart(_hudUp(tween));
            SnapPart(_hudDown(tween));
        }

        private static void SnapPart(Transform t)
        {
            if (t == null || t.localPosition == Vector3.zero) return;
            t.DOKill();
            t.localPosition = Vector3.zero;
        }
    }

    // ==================================================================== long sounds kept

    /// <summary>
    /// Long map sounds (wind, city hum, rain: minutes of sound each) are stored compressed and decoded when a map with
    /// one loads. The decoding happens on the engine's loading thread while it holds the lock the main thread needs to
    /// bring the new map in, so the game stands still for as long as it takes: 313 ms for a 330 second sound, 220 ms for
    /// a 254 second one (measured, at map changes). Freeing the last map's leftovers (WarmUp.Run) also freed these
    /// sounds, so the same sound was decoded again at every later map that has it.
    ///
    /// So they are kept: at every new map, just before the leftovers are freed, every decoded sound of half a minute or
    /// longer is held on to here, which is what stops the engine from freeing it. Each is then decoded once a game.
    /// Up to Budget megabytes; beyond that the ones no map has played for longest are let go. Always on.
    /// </summary>
    internal static class KeptSounds
    {
        private const float Budget = 256f;      // MB of decoded sound held on to
        private const float MinSeconds = 30f;

        private sealed class One { public AudioClip Clip; public float Mb; public int LastMap; }
        private static readonly Dictionary<int, One> Kept = new Dictionary<int, One>();
        private static readonly List<int> Gone = new List<int>();
        private static int _map;

        internal static void Keep()
        {
            _map++;
            Gone.Clear();
            foreach (var kv in Kept) if (kv.Value.Clip == null) Gone.Add(kv.Key);
            foreach (int id in Gone) Kept.Remove(id);
            // every long decoded sound in memory now: this map's, and the last map's (not freed yet)
            foreach (AudioClip c in Resources.FindObjectsOfTypeAll<AudioClip>())
            {
                if (c == null || c.loadType != AudioClipLoadType.DecompressOnLoad || c.loadState != AudioDataLoadState.Loaded || c.length < MinSeconds) continue;
                int id = c.GetInstanceID();
                if (!Kept.ContainsKey(id)) Kept[id] = new One { Clip = c, Mb = (float)c.samples * c.channels * 2f / 1048576f, LastMap = _map - 1 };
            }
            // the ones this map plays
            foreach (AudioSource a in UnityEngine.Object.FindObjectsOfType<AudioSource>())
            {
                AudioClip c = a != null ? a.clip : null;
                One one;
                if (c != null && Kept.TryGetValue(c.GetInstanceID(), out one)) one.LastMap = _map;
            }
            float total = 0f;
            foreach (var kv in Kept) total += kv.Value.Mb;
            while (total > Budget && Kept.Count > 0)
            {
                int oldest = 0, last = int.MaxValue;
                foreach (var kv in Kept) if (kv.Value.LastMap < last) { last = kv.Value.LastMap; oldest = kv.Key; }
                if (last >= _map) break;      // only this map's are left
                total -= Kept[oldest].Mb;
                Kept.Remove(oldest);
            }
        }
    }

    // ==================================================================== warm up effects

    /// <summary>
    /// Warm Up Effects. The first time the game shows an effect (a hit's impact, blood, the hitmarker) Unity loads its
    /// textures and the graphics card compiles its shaders, all in the frame of your first hit. Here that happens while a
    /// map loads: every gun's effect prefabs, the player's death effects and every gun and player model are drawn once on
    /// a small hidden camera far from the map (nothing shows on your screen, nothing is spawned in the game), each only
    /// once a game. (Their sounds are already loaded by the game.) DorsthTweaks' own one-time hitmarker work is done then too.
    /// </summary>
    internal static class WarmUp
    {
        // by id: a kept reference would keep a gone map's materials in memory
        private static readonly HashSet<int> Warmed = new HashSet<int>();
        private static string _lastMap;
        private static int _sameRounds;
        /// <summary>False from the moment a map's scene has loaded until Run has done its work on it.</summary>
        internal static bool Settled = true;

        /// <summary>True for a scene Run follows (a map): not the empty scene between rounds, not the end screen.</summary>
        internal static bool RunsFor(string scene) { return scene != "EmptyScene" && scene != "VictoryScene"; }

        internal static void Install()
        {
            Scenes.Loaded += (scene, mode) =>
            {
                if (Scenes.IsMenu(scene)) return;
                string name = scene.name;
                // FishNet's holder scene loads along with every map: not a map
                if (name == "MovedObjectsHolder") return;
                // the empty scene between rounds and the end screen: nothing to do, and nothing to free yet
                if (!RunsFor(name)) { Settled = true; return; }
                Settled = false;
                _sameRounds = name == _lastMap ? _sameRounds + 1 : 0;
                // memory is freed on a new map, and every fourth round on the same one
                Host.Run(Run(name != _lastMap || _sameRounds % 4 == 3));
                _lastMap = name;
            };
        }

        private static System.Collections.IEnumerator Run(bool newMap)
        {
            // the map's own objects settle first
            yield return null;
            yield return null;
            // and the scene before it is gone (up to a second)
            for (int i = 0; i < 60 && SceneManager.sceneCount > 1; i++) yield return null;
            if (newMap)
            {
                // the last map's leftovers out of memory: the game never lets go of a map's textures, meshes and sounds once
                // it's left (measured: about 50 MB more after every map, never given back, 700 to 920 MB over five maps),
                // and its scripts' leftovers make each garbage collection longer. While the map is still settling, so the
                // moment it takes is part of loading. Not on every round: on the same map there's little to free and it
                // froze the game for up to a few hundred ms, and no garbage collection of its own on top. Always on.
                try { Quality.BeforeUnload(); }
                catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
                try { Effects.ReleasePrefabLights(); }
                catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
                EffectPrefabs.Forget();
                // the long sounds stay (see KeptSounds): freed here, the game decodes them again at a later map
                try { KeptSounds.Keep(); }
                catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
                float t0 = Time.realtimeSinceStartup;
                AsyncOperation unload = null;
                try { unload = Resources.UnloadUnusedAssets(); } catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
                if (unload != null) yield return unload;
                DorsthTweaksPlugin.Log.LogInfo("Last map's leftovers freed in " + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("F0", CultureInfo.InvariantCulture) + " ms");
            }
            Settled = true;
            try { Stills.ForgetGone(); }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
            try { Quality.AfterLoad(); } catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
            try { Effects.AfterLoad(); } catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
            // DorsthTweaks' own: the resized hitmarker pictures, made now instead of on the first hit (always, it's cheap)
            try { Hitmarkers.Prepare(); }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
            if (!DorsthTweaksPlugin.WarmUpEffects.Value) yield break;
            try
            {
                // the effects guns and players make, the ones not done in an earlier map of this game
                var prefabs = new HashSet<GameObject>();
                foreach (EffectPrefabs.Ref r in EffectPrefabs.All())
                    if (WarmOwner(r.Owner) && WarmedPrefabs.Add(r.Prefab.GetInstanceID())) prefabs.Add(r.Prefab);
                // and the prefabs those scripts point to through a part of them: a gun's bullet trail is held as its
                // line, not as an object, and its first use held up the frame of the first shot
                foreach (GameObject linked in LinkedPrefabs())
                    if (WarmedPrefabs.Add(linked.GetInstanceID())) prefabs.Add(linked);
                var materials = new HashSet<Material>();
                var renderers = new List<Renderer>();
                foreach (GameObject go in prefabs)
                {
                    go.GetComponentsInChildren(true, renderers);
                    foreach (Renderer r in renderers)
                    {
                        foreach (Material m in r.sharedMaterials) if (m != null && !Warmed.Contains(m.GetInstanceID())) materials.Add(m);
                        var pr = r as ParticleSystemRenderer;
                        if (pr != null && pr.trailMaterial != null && !Warmed.Contains(pr.trailMaterial.GetInstanceID())) materials.Add(pr.trailMaterial);
                    }
                }
                // the effects themselves made once, out of sight, and drawn the way the game draws them; the materials of
                // effects that can't be made that way are drawn on their own
                Spawn(prefabs, materials);
                Draw(materials);
                Models();
                Hitmarkers.Common();
                Sounds();
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        // effects already made and drawn this game: their parts stay loaded, they're not done again in the next map
        private static readonly HashSet<int> WarmedPrefabs = new HashSet<int>();

        // The scripts whose effects are warmed up: guns, players, and everything that is thrown, shot or placed and goes
        // off by itself. Without the last group, their explosion, its scorch mark and their hit effects were first made
        // in the frame of the first explosion of a game.
        private static readonly string[] WarmOwnerNames = { "Obus", "PhysicsGrenade", "ShrapnelBallistic", "PredictedProjectile", "ProximityMine", "Claymore",
            "HandGrenade", "HandGrenadeTwo", "Bubble", "BumpBullet", "RebondBalle", "MeleeChildCollision", "ItemBehaviour", "FirstPersonController", "PlayerSetup" };
        private static Type[] _warmOwners;
        private static Type[] WarmOwners()
        {
            if (_warmOwners != null) return _warmOwners;
            var list = new List<Type> { typeof(Weapon), typeof(PlayerHealth) };
            Assembly game = typeof(Weapon).Assembly;
            foreach (string name in WarmOwnerNames)
            {
                Type t = null;
                try { t = game.GetType(name, false); } catch { }
                if (t != null && typeof(MonoBehaviour).IsAssignableFrom(t)) list.Add(t);
            }
            return _warmOwners = list.ToArray();
        }
        private static bool WarmOwner(Type owner)
        {
            foreach (Type t in WarmOwners()) if (t.IsAssignableFrom(owner)) return true;
            return false;
        }

        private static readonly Dictionary<Type, FieldInfo[]> PartFields = new Dictionary<Type, FieldInfo[]>();
        /// <summary>
        /// Prefabs a gun or explosive points to through one of their parts (LineRenderer bulletTrailLocal and the like):
        /// the object that part sits on, when it is a prefab and not a part of the script's own object.
        /// </summary>
        private static List<GameObject> LinkedPrefabs()
        {
            var result = new List<GameObject>();
            var seen = new HashSet<int>();
            foreach (Type owner in WarmOwners())
            {
                foreach (MonoBehaviour b in Found.Of(owner))
                {
                    if (b == null) continue;
                    Type type = b.GetType();
                    FieldInfo[] fields;
                    if (!PartFields.TryGetValue(type, out fields))
                    {
                        var list = new List<FieldInfo>();
                        for (Type t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                                if (typeof(Component).IsAssignableFrom(f.FieldType) && !typeof(RectTransform).IsAssignableFrom(f.FieldType)) list.Add(f);
                        PartFields[type] = fields = list.ToArray();
                    }
                    if (fields.Length == 0) continue;
                    Transform ownRoot = null;
                    foreach (FieldInfo f in fields)
                    {
                        Component part = null;
                        try { part = f.GetValue(b) as Component; } catch { }
                        if (part == null) continue;
                        GameObject go = part.gameObject;
                        if (go.scene.IsValid()) continue;                    // something in the map, not a prefab
                        if (ownRoot == null) ownRoot = b.transform.root;
                        if (part.transform.root == ownRoot) continue;        // a part of the script's own prefab
                        if (seen.Add(go.GetInstanceID())) result.Add(go);
                    }
                }
            }
            return result;
        }
        // the sound fields of each kind of script, and the sounds already asked for
        private static readonly Dictionary<Type, FieldInfo[]> SoundFields = new Dictionary<Type, FieldInfo[]>();
        private static readonly HashSet<int> SoundsDone = new HashSet<int>();

        /// <summary>
        /// Each effect made once far below the map with none of the game's scripts on it (nothing of it runs: no sound,
        /// no shake, nothing sent), its particles moved on a little, drawn once by a copy of your camera into a small
        /// hidden picture, then removed. The first hit then makes nothing for the first time: the effect's parts are
        /// loaded and its shaders made in the way your camera draws them.
        /// </summary>
        private static int Spawn(HashSet<GameObject> prefabs, HashSet<Material> leftToDraw)
        {
            var holder = new GameObject("DorsthTweaks warm up effects");
            holder.hideFlags = HideFlags.HideAndDontSave;
            // made under a switched off parent: nothing of a copy wakes up before its scripts are taken off
            holder.SetActive(false);
            Vector3 at = new Vector3(0f, -10000f, 0f);
            holder.transform.position = at;
            RenderTexture rt = null;
            int n = 0;
            try
            {
                var behaviours = new List<MonoBehaviour>();
                foreach (GameObject p in prefabs)
                {
                    if (p == null || p.transform is RectTransform) continue;
                    if (p.GetComponentInChildren<Renderer>(true) == null) continue;
                    if (p.GetComponentInChildren<FishNet.Object.NetworkObject>(true) != null) continue;
                    GameObject c = null;
                    try
                    {
                        c = UnityEngine.Object.Instantiate(p, holder.transform);
                        // the game's scripts off it (twice: one script can need another)
                        for (int pass = 0; pass < 2; pass++)
                        {
                            c.GetComponentsInChildren(true, behaviours);
                            foreach (MonoBehaviour b in behaviours)
                                if (b != null && !IsUnity(b)) { try { UnityEngine.Object.DestroyImmediate(b); } catch { } }
                        }
                        c.GetComponentsInChildren(true, behaviours);
                        bool left = false;
                        foreach (MonoBehaviour b in behaviours) if (b != null && !IsUnity(b)) left = true;
                        if (left) { UnityEngine.Object.DestroyImmediate(c); continue; }
                        foreach (AudioSource a in c.GetComponentsInChildren<AudioSource>(true)) a.enabled = false;
                        // an effect with a camera or a listener of its own must not draw or hear anything
                        foreach (Camera own in c.GetComponentsInChildren<Camera>(true)) own.enabled = false;
                        foreach (AudioListener ears in c.GetComponentsInChildren<AudioListener>(true)) ears.enabled = false;
                        // drawn with the effect: its materials don't need drawing on their own
                        foreach (Renderer r in c.GetComponentsInChildren<Renderer>(true))
                        {
                            foreach (Material m in r.sharedMaterials) if (m != null && leftToDraw.Remove(m)) Warmed.Add(m.GetInstanceID());
                            var pr = r as ParticleSystemRenderer;
                            if (pr != null && pr.trailMaterial != null && leftToDraw.Remove(pr.trailMaterial)) Warmed.Add(pr.trailMaterial.GetInstanceID());
                        }
                        foreach (Collider col in c.GetComponentsInChildren<Collider>(true)) col.enabled = false;
                        foreach (Rigidbody rb in c.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
                        // a grid in front of the camera, 3 metres out
                        c.transform.localPosition = new Vector3((n % 8 - 3.5f) * 0.7f, (n / 8 % 8 - 3.5f) * 0.7f, 3f);
                        c.transform.localRotation = Quaternion.identity;
                        n++;
                    }
                    catch { if (c != null) UnityEngine.Object.DestroyImmediate(c); }
                }
                behaviours.Clear();
                if (n == 0) return 0;
                holder.SetActive(true);
                foreach (ParticleSystem ps in holder.GetComponentsInChildren<ParticleSystem>(true))
                {
                    try { ps.Simulate(0.1f, true, true); } catch { }
                }
                var camGo = new GameObject("DorsthTweaks warm up camera");
                camGo.hideFlags = HideFlags.HideAndDontSave;
                camGo.transform.SetParent(holder.transform, false);
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                Camera main = Camera.main;
                if (main != null) cam.CopyFrom(main);
                camGo.transform.SetPositionAndRotation(at, Quaternion.identity);
                cam.cullingMask = ~0;
                cam.fieldOfView = 90f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 30f;
                cam.useOcclusionCulling = false;
                rt = RenderTexture.GetTemporary(64, 64, 24);
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
            }
            finally
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(holder);
            }
            return n;
        }

        private static bool IsUnity(MonoBehaviour b)
        {
            string ns = b.GetType().Namespace ?? "";
            return ns.StartsWith("UnityEngine", StringComparison.Ordinal) || ns.StartsWith("TMPro", StringComparison.Ordinal);
        }

        /// <summary>
        /// The sounds of every gun and player (gunshots, hits, reloads, footsteps, slides) that aren't read in yet: read
        /// in now, in the background while the map settles. The game's sounds are read in on their first play, which held
        /// up the frame the first time a gun was fired or picked up (measured: 40 to 70 ms in the training range). Only
        /// the sounds those scripts point to and the ones on their own audio sources: not every sound in memory, which
        /// read in the music and every map's ambience too.
        /// </summary>
        private static int Sounds()
        {
            int n = 0;
            var sources = new List<AudioSource>();
            foreach (Type kind in new[] { typeof(Weapon), typeof(FirstPersonController), typeof(PlayerHealth) })
            {
                foreach (UnityEngine.Object found in Found.Of(kind))
                {
                    var script = found as MonoBehaviour;
                    if (script == null) continue;
                    Type type = script.GetType();
                    FieldInfo[] fields;
                    if (!SoundFields.TryGetValue(type, out fields))
                    {
                        var list = new List<FieldInfo>();
                        for (Type t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                            foreach (FieldInfo f in AccessTools.GetDeclaredFields(t))
                                if (!f.IsStatic && (f.FieldType == typeof(AudioClip) || f.FieldType == typeof(AudioClip[]))) list.Add(f);
                        SoundFields[type] = fields = list.ToArray();
                    }
                    foreach (FieldInfo f in fields)
                    {
                        object value = null;
                        try { value = f.GetValue(script); } catch { }
                        if (value is AudioClip one) n += Load(one);
                        else if (value is AudioClip[] many)
                            foreach (AudioClip c in many) n += Load(c);
                    }
                    script.GetComponentsInChildren(true, sources);
                    foreach (AudioSource a in sources) n += Load(a.clip);
                }
            }
            if (n > 0) DorsthTweaksPlugin.Log.LogInfo("Warm up: " + n + " sounds read in");
            return n;
        }

        private static int Load(AudioClip c)
        {
            if (c == null || !SoundsDone.Add(c.GetInstanceID()) || c.loadState != AudioDataLoadState.Unloaded) return 0;
            try { return c.LoadAudioData() ? 1 : 0; }
            catch { return 0; }
        }

        // gun and player parts already drawn this game (by mesh and material): they stay loaded
        private static readonly HashSet<long> ModelsWarmed = new HashSet<long>();

        /// <summary>
        /// Every gun and the player body, their parts drawn once by a copy of your camera into a small hidden picture,
        /// none of them made (they're network objects, a copy would try to join the game). The first time a gun or a
        /// player shows up its parts are already on the graphics card and their shaders made the way your camera draws
        /// them: a katana coming into view no longer holds a frame up.
        /// </summary>
        private static int Models()
        {
            var parts = new List<KeyValuePair<Mesh, Material>>();
            var subs = new List<int>();
            var roots = new List<GameObject>();
            foreach (Weapon w in Found.Of<Weapon>()) if (w != null && !w.gameObject.scene.IsValid()) roots.Add(w.gameObject);
            foreach (FirstPersonController p in Found.Of<FirstPersonController>()) if (p != null && !p.gameObject.scene.IsValid()) roots.Add(p.gameObject);
            var renderers = new List<Renderer>();
            foreach (GameObject root in roots)
            {
                root.GetComponentsInChildren(true, renderers);
                foreach (Renderer r in renderers)
                {
                    Mesh mesh = null;
                    var smr = r as SkinnedMeshRenderer;
                    if (smr != null) mesh = smr.sharedMesh;
                    else if (r is MeshRenderer) { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
                    if (mesh == null) continue;
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length && i < mesh.subMeshCount; i++)
                    {
                        if (mats[i] == null) continue;
                        long key = ((long)mesh.GetInstanceID() << 32) ^ (uint)mats[i].GetInstanceID();
                        if (!ModelsWarmed.Add(key)) continue;
                        parts.Add(new KeyValuePair<Mesh, Material>(mesh, mats[i]));
                        subs.Add(i);
                    }
                }
            }
            if (parts.Count == 0) return 0;
            var holder = new GameObject("DorsthTweaks warm up parts");
            holder.hideFlags = HideFlags.HideAndDontSave;
            Vector3 at = new Vector3(0f, -10000f, 0f);
            holder.transform.position = at;
            var cam = holder.AddComponent<Camera>();
            cam.enabled = false;
            Camera main = Camera.main;
            if (main != null) cam.CopyFrom(main);
            holder.transform.SetPositionAndRotation(at, Quaternion.identity);
            cam.cullingMask = 0;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 30f;
            cam.useOcclusionCulling = false;
            var rt = RenderTexture.GetTemporary(64, 64, 24);
            cam.targetTexture = rt;
            var cb = new CommandBuffer { name = "DorsthTweaks warm up parts" };
            Matrix4x4 place = Matrix4x4.TRS(at + Vector3.forward * 3f, Quaternion.identity, Vector3.one * 0.1f);
            for (int i = 0; i < parts.Count; i++)
            {
                try
                {
                    Material m = parts[i].Value;
                    for (int pass = 0; pass < m.passCount; pass++) cb.DrawMesh(parts[i].Key, place, m, subs[i], pass);
                }
                catch { }
            }
            try
            {
                cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, cb);
                cam.Render();
            }
            finally
            {
                cam.RemoveAllCommandBuffers();
                cb.Release();
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(holder);
            }
            return parts.Count;
        }

        /// <summary>Draws each material once on a hidden 32x32 camera far below the map, so its shader is compiled now.</summary>
        private static int Draw(HashSet<Material> materials)
        {
            if (materials.Count == 0) return 0;
            var rt = RenderTexture.GetTemporary(32, 32, 16);
            var holder = new GameObject("DorsthTweaks warm up");
            holder.hideFlags = HideFlags.HideAndDontSave;
            holder.transform.position = new Vector3(0f, -10000f, 0f);
            var cam = holder.AddComponent<Camera>();
            cam.enabled = false;
            cam.targetTexture = rt;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // forward, so the draw below runs whatever the game's own cameras use
            cam.renderingPath = RenderingPath.Forward;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.cullingMask = 0;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 10f;
            var quad = new Mesh();
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            var cb = new CommandBuffer { name = "DorsthTweaks warm up" };
            Matrix4x4 at = Matrix4x4.TRS(holder.transform.position + Vector3.forward, Quaternion.identity, Vector3.one);
            int n = 0;
            foreach (Material m in materials)
            {
                try
                {
                    for (int pass = 0; pass < m.passCount; pass++) cb.DrawMesh(quad, at, m, 0, pass);
                    Warmed.Add(m.GetInstanceID());
                    n++;
                }
                catch { }
            }
            try
            {
                cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, cb);
                cam.Render();
            }
            finally
            {
                cam.RemoveAllCommandBuffers();
                cb.Release();
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(quad);
                UnityEngine.Object.Destroy(holder);
            }
            return n;
        }
    }

    // ==================================================================== reuse shot effects

    /// <summary>
    /// Reduce Effects, reusing shot effects (and hiding bullet holes, in the same hook). Every surface a bullet hits
    /// makes an impact (10 or more for a shotgun shot) and every shot a muzzle flash, each a new object thrown away a
    /// moment later, which drops frames. Effects that end by themselves (CFX_AutoDestructShuriken) and are only
    /// particles, lights, light fades and sounds switch off when done instead, and the next one of that kind is that
    /// one again. Bullet trails and anything else are left to the game. Everything kept goes with the map.
    /// </summary>
    internal static class ShotEffects
    {
        private static readonly Func<bool> On = () => DorsthTweaksPlugin.ReduceEffects.Value;
        private const int MaxKept = 48;

        private static readonly HashSet<Type> Safe = new HashSet<Type>
        {
            typeof(Transform), typeof(ParticleSystem), typeof(ParticleSystemRenderer), typeof(AudioSource), typeof(Light),
            typeof(CFX_AutoDestructShuriken), typeof(CFX_LightIntensityFade), typeof(CFX_AutoStopLoopedEffect),
        };

        // the effects guns make (gathered when a map loads), whether each can be reused, and the copies kept of each
        private static readonly HashSet<GameObject> Candidates = new HashSet<GameObject>();
        // (by id: asked for every effect made, and a kept reference would keep a gone map's effects in memory)
        private static readonly Dictionary<int, bool> Reusable = new Dictionary<int, bool>();
        private static readonly HashSet<int> KeptIds = new HashSet<int>();
        private static readonly Dictionary<GameObject, List<GameObject>> Kept = new Dictionary<GameObject, List<GameObject>>();
        private static readonly List<Component> Parts = new List<Component>();
        private static readonly List<ParticleSystem> Systems = new List<ParticleSystem>();
        private static readonly List<Transform> To = new List<Transform>();
        private static readonly List<Transform> From = new List<Transform>();

        private static bool _gathered;

        internal static void Install()
        {
            GameHook.Add(typeof(UnityEngine.Object), "Instantiate", new[] { typeof(UnityEngine.Object), typeof(Vector3), typeof(Quaternion) },
                typeof(ShotEffects), "MakePrefix", "MakePostfix", On);
            GameHook.Add(typeof(UnityEngine.Object), "Instantiate", new[] { typeof(UnityEngine.Object), typeof(Vector3), typeof(Quaternion), typeof(Transform) },
                typeof(ShotEffects), "MakeUnderPrefix", "MakePostfix", On);
            DorsthTweaksPlugin.ReduceEffects.SettingChanged += (sender, args) => GameHook.RefreshAll();
            Scenes.Loaded += (scene, mode) => Forget();
            Host.Add<GatherClock>();
        }

        private static void Forget()
        {
            Kept.Clear();
            KeptIds.Clear();
            Candidates.Clear();
            Decals.Clear();
            Marks.Clear();
            _gathered = false;
        }

        // the effect prefabs the guns and the players point to (shots, slide dust), and the hit effects with bullet holes
        private static readonly HashSet<GameObject> Decals = new HashSet<GameObject>();
        // The burn mark of an explosion (the game's "explosionDecal" of a grenade, a shell, a projectile and shrapnel).
        // The game makes and switches it off like a bullet hole: 0.085 ms and 68 KB of garbage per explosion (2.65 ms
        // for the first of a map, measured), and its object lives on for 31 s. A mark that is nothing but a decal isn't
        // made at all. The game uses nothing of what Instantiate hands back in those four places, so nothing is handed
        // back. A mark with anything else on it (a script, a collider) is left as it is.
        private static readonly HashSet<GameObject> Marks = new HashSet<GameObject>();
        private static readonly List<Component> MarkParts = new List<Component>();

        private static void Gather()
        {
            _gathered = true;
            foreach (EffectPrefabs.Ref r in EffectPrefabs.All())
            {
                if (EffectPrefabs.Is<Weapon>(r.Owner) || EffectPrefabs.Is<FirstPersonController>(r.Owner)) Candidates.Add(r.Prefab);
                if (r.Field == "explosiondecal" && OnlyAMark(r.Prefab)) Marks.Add(r.Prefab);
                else if (r.Field.Contains("hitimpact") || r.Field == "genericimpact" || r.Field.Contains("decal")) Decals.Add(r.Prefab);
            }
            if (Marks.Count > 0) DorsthTweaksPlugin.Log.LogInfo("Reduce VFX: the burn marks of explosions aren't made (" + Marks.Count + " kinds).");
        }

        // a decal script and what it draws with, nothing else
        private static bool OnlyAMark(GameObject prefab)
        {
            bool decal = false, only = true;
            prefab.GetComponentsInChildren(true, MarkParts);
            foreach (Component c in MarkParts)
            {
                if (c == null) { only = false; break; }
                if (c is Transform || c is Renderer || c is MeshFilter) continue;
                if (c is MonoBehaviour && c.GetType().Name.IndexOf("Decal", StringComparison.OrdinalIgnoreCase) >= 0) { decal = true; continue; }
                only = false; break;
            }
            MarkParts.Clear();
            return decal && only;
        }

        private static bool CanReuse(GameObject prefab)
        {
            int id = prefab.GetInstanceID();
            bool ok;
            if (Reusable.TryGetValue(id, out ok)) return ok;
            ok = prefab.GetComponent<CFX_AutoDestructShuriken>() != null;
            if (ok)
            {
                prefab.GetComponentsInChildren(true, Parts);
                foreach (Component c in Parts) if (c == null || !Safe.Contains(c.GetType())) { ok = false; break; }
                Parts.Clear();
            }
            // a sub-emitter with its own stop action: switched on again, Unity warns about it every time (thousands of
            // log lines), so effects made like that are left to the game
            if (ok)
            {
                prefab.GetComponentsInChildren(true, Systems);
                foreach (ParticleSystem ps in Systems)
                {
                    var sub = ps.subEmitters;
                    if (!sub.enabled) continue;
                    for (int i = 0; i < sub.subEmittersCount; i++)
                    {
                        ParticleSystem child = sub.GetSubEmitterSystem(i);
                        if (child != null && child.main.stopAction != ParticleSystemStopAction.None) { ok = false; break; }
                    }
                    if (!ok) break;
                }
                Systems.Clear();
            }
            Reusable[id] = ok;
            return ok;
        }

        private static GameObject Take(GameObject prefab)
        {
            List<GameObject> list;
            if (!Kept.TryGetValue(prefab, out list)) return null;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                GameObject g = list[i];
                if (g == null) { list.RemoveAt(i); continue; }
                if (!g.activeSelf) return g;
            }
            return null;
        }

        private static bool Wanted(UnityEngine.Object original, out GameObject prefab)
        {
            prefab = original as GameObject;
            // gathered right after the map loads (GatherClock), never on a shot
            if (prefab == null || !_gathered) return false;
            return Candidates.Contains(prefab) && CanReuse(prefab);
        }

        private static bool Reuse(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent, ref UnityEngine.Object result)
        {
            GameObject g = Take(prefab);
            if (g == null) return true;
            // what's left of its last run goes, then it plays again like a new one (particles and sounds that play on
            // their own start when it's switched on)
            g.GetComponentsInChildren(true, Systems);
            foreach (ParticleSystem ps in Systems) ps.Clear(false);
            Systems.Clear();
            // the layers it came with: your own muzzle flash is moved to the viewmodel's layer, someone else's isn't
            prefab.GetComponentsInChildren(true, From);
            g.GetComponentsInChildren(true, To);
            if (From.Count == To.Count) for (int i = 0; i < To.Count; i++) To[i].gameObject.layer = From[i].gameObject.layer;
            To.Clear();
            From.Clear();
            Transform t = g.transform;
            t.SetParent(parent, false);
            t.SetPositionAndRotation(position, rotation);
            t.localScale = prefab.transform.localScale;
            g.SetActive(true);
            result = g;
            return false;
        }

        private static bool MakePrefix(UnityEngine.Object original, Vector3 position, Quaternion rotation, ref UnityEngine.Object __result)
        {
            try
            {
                if (Marks.Count > 0)
                {
                    var mark = original as GameObject;
                    if ((object)mark != null && Marks.Contains(mark)) { __result = null; return false; }
                }
                GameObject prefab;
                return !Wanted(original, out prefab) || Reuse(prefab, position, rotation, null, ref __result);
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); return true; }
        }

        private static bool MakeUnderPrefix(UnityEngine.Object original, Vector3 position, Quaternion rotation, Transform parent, ref UnityEngine.Object __result)
        {
            try
            {
                GameObject prefab;
                return !Wanted(original, out prefab) || Reuse(prefab, position, rotation, parent, ref __result);
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); return true; }
        }

        // a new one: a hit effect loses its bullet hole; a reusable one switches off when done instead of being destroyed, and is kept
        private static void MakePostfix(UnityEngine.Object original, UnityEngine.Object __result, bool __runOriginal)
        {
            if (!__runOriginal) return;
            try
            {
                GameObject prefab, made = __result as GameObject;
                // (one that's kept already: another mod's Instantiate hook can hand a reused one back through here)
                if (made == null || KeptIds.Contains(made.GetInstanceID())) return;
                var from = original as GameObject;
                if (_gathered && from != null && Decals.Contains(from)) NoHole(made);
                if (!Wanted(original, out prefab)) return;
                List<GameObject> list;
                if (!Kept.TryGetValue(prefab, out list)) Kept[prefab] = list = new List<GameObject>();
                if (list.Count >= MaxKept) return;
                made.GetComponentsInChildren(true, Ends);
                foreach (CFX_AutoDestructShuriken a in Ends) a.OnlyDeactivate = true;
                Ends.Clear();
                // a light that fades out and then destroys itself would take the whole effect with it
                made.GetComponentsInChildren(true, Fades);
                foreach (CFX_LightIntensityFade l in Fades) l.autodestruct = false;
                Fades.Clear();
                list.Add(made);
                KeptIds.Add(made.GetInstanceID());
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static readonly List<CFX_AutoDestructShuriken> Ends = new List<CFX_AutoDestructShuriken>();
        private static readonly List<CFX_LightIntensityFade> Fades = new List<CFX_LightIntensityFade>();
        private static readonly List<Renderer> Drawn = new List<Renderer>();
        private static readonly List<MonoBehaviour> Scripts = new List<MonoBehaviour>();
        private static readonly Dictionary<Type, bool> DecalTypes = new Dictionary<Type, bool>();

        /// <summary>An effect that's kept to be used again (it may be switched off right now).</summary>
        internal static bool IsKept(GameObject g)
        {
            return g != null && KeptIds.Contains(g.GetInstanceID());
        }

        /// <summary>
        /// The setting went off: the kept effects go. One that's switched off is destroyed; one still playing is given
        /// back to the game as it was made, so it destroys itself when it's done.
        /// </summary>
        internal static void Release()
        {
            foreach (List<GameObject> list in Kept.Values)
            {
                foreach (GameObject g in list)
                {
                    if (g == null) continue;
                    if (!g.activeSelf) { UnityEngine.Object.Destroy(g); continue; }
                    g.GetComponentsInChildren(true, Ends);
                    foreach (CFX_AutoDestructShuriken a in Ends) a.OnlyDeactivate = false;
                    Ends.Clear();
                    g.GetComponentsInChildren(true, Fades);
                    foreach (CFX_LightIntensityFade l in Fades) l.autodestruct = true;
                    Fades.Clear();
                }
            }
            Kept.Clear();
            KeptIds.Clear();
        }

        // its pictures off (the hole), and any decal script that would make one later; its particles stay
        private static void NoHole(GameObject made)
        {
            made.GetComponentsInChildren(true, Drawn);
            foreach (Renderer r in Drawn) if (!(r is ParticleSystemRenderer)) r.enabled = false;
            Drawn.Clear();
            made.GetComponentsInChildren(true, Scripts);
            foreach (MonoBehaviour b in Scripts)
            {
                if (b == null) continue;
                Type t = b.GetType();
                if (!DecalTypes.TryGetValue(t, out bool decal)) DecalTypes[t] = decal = t.Name.IndexOf("Decal", StringComparison.OrdinalIgnoreCase) >= 0;
                if (decal) b.enabled = false;
            }
            Scripts.Clear();
        }

        private sealed class GatherClock : MonoBehaviour
        {
            private void Update()
            {
                // once the map has settled (WarmUp has freed the last map and dropped what EffectPrefabs gathered)
                if (!_gathered && On() && WarmUp.Settled && !Scenes.InMainMenu())
                {
                    try { Gather(); } catch (Exception e) { _gathered = true; DorsthTweaksPlugin.LogErrorOnce(e); }
                }
            }
        }
    }

    // ==================================================================== effect prefabs, shared

    /// <summary>
    /// The prefabs the game's scripts point to (impacts, muzzle flashes, explosions, dust, death effects): every
    /// GameObject or GameObject[] field of a game script that holds a prefab, not something in a scene. Gathered once
    /// per map on first use, for Reduce Effects and Warm Up Effects alike, instead of each scanning on its own.
    /// </summary>
    internal static class EffectPrefabs
    {
        internal struct Ref
        {
            public Type Owner;
            public string Field; // lower case
            public GameObject Prefab;
        }

        private static readonly List<Ref> Refs = new List<Ref>();
        private static readonly Dictionary<Type, FieldInfo[]> FieldsOf = new Dictionary<Type, FieldInfo[]>();
        private static bool _gathered;

        internal static void Install()
        {
            // a new map's scripts point to its own effects too: gathered again when next needed
            Scenes.Loaded += (scene, mode) => Forget();
        }

        /// <summary>Drops what was gathered, so nothing of a map that's gone is kept in memory.</summary>
        internal static void Forget()
        {
            _gathered = false;
            Refs.Clear();
        }

        internal static List<Ref> All()
        {
            if (_gathered) return Refs;
            _gathered = true;
            Refs.Clear();
            Assembly game = typeof(Weapon).Assembly;
            var seen = new Dictionary<FieldInfo, HashSet<int>>();
            foreach (MonoBehaviour b in Found.All())
            {
                if (b == null) continue;
                Type t = b.GetType();
                if (t.Assembly != game) continue;
                foreach (FieldInfo f in Fields(t))
                {
                    object v = null;
                    try { v = f.GetValue(b); } catch { }
                    var one = v as GameObject;
                    if (one != null) Add(t, f, one, seen);
                    var many = v as GameObject[];
                    if (many != null) foreach (GameObject g in many) Add(t, f, g, seen);
                }
            }
            return Refs;
        }

        private static void Add(Type owner, FieldInfo f, GameObject g, Dictionary<FieldInfo, HashSet<int>> seen)
        {
            if (g == null || g.scene.IsValid()) return; // something in a scene, not a prefab
            HashSet<int> ids;
            if (!seen.TryGetValue(f, out ids)) seen[f] = ids = new HashSet<int>();
            if (!ids.Add(g.GetInstanceID())) return;
            Refs.Add(new Ref { Owner = owner, Field = f.Name.ToLowerInvariant(), Prefab = g });
        }

        private static FieldInfo[] Fields(Type type)
        {
            FieldInfo[] fields;
            if (FieldsOf.TryGetValue(type, out fields)) return fields;
            var list = new List<FieldInfo>();
            for (Type t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (f.FieldType == typeof(GameObject) || f.FieldType == typeof(GameObject[])) list.Add(f);
            FieldsOf[type] = fields = list.ToArray();
            return fields;
        }

        internal static bool Is<T>(Type owner) { return typeof(T).IsAssignableFrom(owner); }
    }

    // ==================================================================== reduce effects (all but the reused shots)

    /// <summary>
    /// Reduce Effects, the removals (reusing shot effects is ShotEffects):
    ///  - slide dust and a dropped gun's despawn puff aren't made (skipped on your client only, the game still tells everyone);
    ///  - the lights inside effect prefabs are off, and the scripts that fade or flicker them stop; fires burn at a steady light;
    ///  - light shafts (volumetric beams and their dust) and lens flares on maps are off;
    ///  - the camera's bloom, vignette, ambient occlusion, screen space reflections and lens distortion (the warp on a kill
    ///    and when falling) are off, and a kill no longer greys the screen out; anti-aliasing (FXAA here) is off;
    ///  - real time shadows and MSAA are off (Quality, with the other quality settings).
    /// Everything switched off is remembered and switched on again when the setting goes off.
    /// </summary>
    internal static class Effects
    {
        private static readonly Func<bool> On = () => DorsthTweaksPlugin.ReduceEffects.Value;

        // every post-processing effect but color grading (the game's brightness setting is color grading, so it stays):
        // looks, and auto exposure (the eye adjusting to light and dark)
        internal static readonly Type[] Filters = { typeof(PP.Bloom), typeof(PP.Vignette), typeof(PP.AmbientOcclusion), typeof(PP.ScreenSpaceReflections), typeof(PP.LensDistortion), typeof(PP.AutoExposure),
            typeof(PP.MotionBlur), typeof(PP.DepthOfField), typeof(PP.ChromaticAberration), typeof(PP.Grain) };
        private static readonly string[] EffectWords = { "fx", "vfx", "flash", "explosion", "impact", "particle", "spark", "smoke" };

        // what was switched off and how it was before (Perf's No Post-Processing reads these to know what's the game's own)
        internal static readonly Dictionary<PP.PostProcessEffectSettings, bool> Switched = new Dictionary<PP.PostProcessEffectSettings, bool>();
        internal static readonly Dictionary<PP.PostProcessLayer, PP.PostProcessLayer.Antialiasing> AaModes = new Dictionary<PP.PostProcessLayer, PP.PostProcessLayer.Antialiasing>();
        private static readonly List<FirstPersonController> Players = new List<FirstPersonController>();
        private static readonly List<Behaviour> Off = new List<Behaviour>();
        private static readonly List<Light> Darkened = new List<Light>();
        // The sparks and the flying debris of an explosion. Every explosion effect is built from the same parts: the
        // dome (root), "RocketFlash..", " Shockwave", "Smoke..", and "Eclats..", "PF_Sparks_..",
        // "PF_Concrete_Debris_..": up to 340 sparks and 60 rocks an explosion, most of them checked against the map
        // every frame they live (collision), the rocks for up to 4 s. Those last three kinds are switched off ON THE
        // EFFECT PREFAB, once per map, so an effect made from it never starts them: nothing is done per explosion. The
        // dome, flash and shockwave stay.
        private static readonly List<GameObject> Parked = new List<GameObject>();
        private static readonly List<ParticleSystem> _parts = new List<ParticleSystem>();
        private static int _parkedSaid = -1;
        // The smoke of an explosion ("Smoke", "Smoke (1)") and the gas cloud over the Blister's and Cyst's puddle
        // ("VFX_toxicvape" in a damage zone) go the same way. These two are the only smoke this mod removes: they hide
        // nobody and only cost frames. Smoke or cover that hides players is never removed. A damage zone loses only
        // its cloud: its puddle shows where the zone is. A rocket's trail stays: it shows where the rocket is.
        private static bool Cloud(string name)
        {
            return name.IndexOf("Smoke", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Vape", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private static bool Shards(string name)
        {
            return name.IndexOf("Eclat", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Spark", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Debris", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private static readonly List<Renderer> Hidden = new List<Renderer>();
        private static AccessTools.FieldRef<UnityStandardAssets.Effects.FireLight, Light> _fireLight;
        private static AccessTools.FieldRef<UnityStandardAssets.Effects.FireLight, bool> _burning;

        internal static void Install()
        {
            Type me = typeof(Effects);
            int dust = 0;
            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(typeof(FirstPersonController)))
                if (m.Name.StartsWith("RpcLogic___SpawnVFXObservers_", StringComparison.Ordinal))
                {
                    GameHook.Add(typeof(FirstPersonController), m.Name, null, me, "Skip", null, On, Priority.First);
                    dust++;
                }
            if (dust == 0) GameHook.Missing.Add("FirstPersonController.SpawnVFXObservers");
            GameHook.Add(typeof(Weapon), "SpawnEffect", null, me, "DespawnPrefix", null, On, Priority.First);
            // a light fade still runs (it can be what removes its effect at the end), its light just stays off
            GameHook.Add(typeof(CFX_LightIntensityFade), "OnEnable", null, me, null, "DarkPostfix", On);
            // a blade's trail only shows while it swings: your own swing, and other players' (their swing's effect)
            _fireTimer = AccessTools.FieldRefAccess<MeleeWeapon, float>("fireTimer");
            GameHook.Add(typeof(MeleeWeapon), "Fire", null, me, "SwingPrefix", "SwingPostfix", On);
            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(typeof(MeleeWeapon)))
                if (m.Name.StartsWith("RpcLogic___ShootObserversEffect_", StringComparison.Ordinal))
                    GameHook.Add(typeof(MeleeWeapon), m.Name, null, me, null, "SwungPostfix", On);
            Host.Add<SwingClock>();
            // a broken pane of glass makes no pieces (see GlassPrefix)
            _glassSound = AccessTools.FieldRefAccess<ShatterableGlass, AudioSource>("SoundEmitter");
            GameHook.Add(typeof(ShatterableGlass), "Shatter", new[] { typeof(Vector2), typeof(Vector3) }, me, "GlassPrefix", null, On);
            // chunks shot off scenery, and broken pots (see ChunkPrefix)
            GameHook.Add(typeof(DetachableObject), "Detach", null, me, "ChunkPrefix", null, On);
            GameHook.Add(typeof(Pot), "Die", null, me, "PotPrefix", null, On);
            GameHook.Add(typeof(CFX_LightIntensityFade), "Update", null, me, null, "DarkPostfix", On);
            GameHook.Add(typeof(WFX_LightFlicker), "Start", null, me, "FlickerPrefix", null, On);
            _fireLight = AccessTools.FieldRefAccess<UnityStandardAssets.Effects.FireLight, Light>("m_Light");
            _burning = AccessTools.FieldRefAccess<UnityStandardAssets.Effects.FireLight, bool>("m_Burning");
            GameHook.Add(typeof(UnityStandardAssets.Effects.FireLight), "Update", null, me, "FirePrefix", null, On);
            // every kind of kill (guns, explosives, props) has its own KillShockWave that greys the screen out
            foreach (Type t in AccessTools.GetTypesFromAssembly(typeof(Weapon).Assembly))
                if (t.GetMethod("KillShockWave", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null) != null)
                    GameHook.Add(t, "KillShockWave", Type.EmptyTypes, me, null, "KillPostfix", On);
            // each player's camera has its own post-processing: dealt with as the player is made, no scene search
            DorsthTweaksPlugin.Harmony.Patch(DorsthTweaksPlugin.Target(typeof(FirstPersonController), "Awake___UserLogic"), null, DorsthTweaksPlugin.Patch(me, "PlayerPostfix"));
            DorsthTweaksPlugin.OnChange(DorsthTweaksPlugin.ReduceEffects, () => { Quality.Apply(); Apply(); });
            Scenes.Loaded += (scene, mode) =>
            {
                // (not for FishNet's holder scene, which loads along with every map)
                if (!On() || Scenes.IsMenu(scene) || scene.name == "MovedObjectsHolder") return;
                // A map: AfterLoad comes in a moment and goes over everything again once the last map is gone. So here
                // only what's in the scene is dealt with; the effect prefabs are left as they are (their lights stay
                // off). Looking them up here as well was a second search through every script in memory per map, and
                // its result was dropped again before the last map was freed.
                if (WarmUp.RunsFor(scene.name)) Safe(ApplyScene);
                else Safe(Apply);
            };
        }

        /// <summary>
        /// Before the last map is freed: the prefab lights that were switched off are let go of, so a gone map's
        /// effects aren't kept in memory by this list. AfterLoad switches the ones still there off again right after.
        /// </summary>
        // Shot glass: the pieces on the ground halve the frame rate (1200 to about 600 FPS). The game's ShatterableGlass
        // cuts a pane into up to 36 pieces, each a new object with its own mesh (never freed), two materials, a box
        // collider and a rigidbody with ContinuousDynamic collision. The pieces don't block bullets. Here the pane
        // breaks as in the game (its picture and colliders go, the sound plays, the object goes when the sound is over)
        // but no pieces are made. A pane that only cracks and stays (ShatterButNotBreak) is left to the game: its pieces
        // are the pane.
        private static AccessTools.FieldRef<ShatterableGlass, AudioSource> _glassSound;

        private static bool GlassPrefix(ShatterableGlass __instance)
        {
            try
            {
                if (__instance.ShatterButNotBreak) return true;
                AudioSource sound = _glassSound(__instance);
                GameObject g = __instance.gameObject;
                if (sound != null) sound.Play();
                Renderer r = g.GetComponent<Renderer>(); if (r != null) UnityEngine.Object.Destroy(r);
                MeshFilter mf = g.GetComponent<MeshFilter>(); if (mf != null) UnityEngine.Object.Destroy(mf);
                UnityEngine.Object.Destroy(__instance);
                MeshCollider mc = g.GetComponent<MeshCollider>(); if (mc != null) UnityEngine.Object.Destroy(mc);
                BoxCollider bc = g.GetComponent<BoxCollider>(); if (bc != null) UnityEngine.Object.Destroy(bc);
                if (sound == null) UnityEngine.Object.Destroy(g);
                else if (sound.clip != null) UnityEngine.Object.Destroy(g, sound.clip.length);
                return false;
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); return true; }
        }

        // A DetachableObject is a chunk of scenery that a shot knocks off (every client, from the server). It is visual
        // only: you walk and shoot through it. The game puts it on layer 18, unparents it, gives it a falling rigidbody
        // that is never removed, and makes its effect (vfx) at that spot. Here the chunk only disappears: layer 18 as in
        // the game, its pictures and colliders off, no rigidbody woken, no effect. It stays where it was, so nothing
        // about the networked object changes. A pot (Pot.Die) goes as in the game but without its effect. Drawing a
        // pillar's chunks as one object does not work: the game's chunk meshes can't be read, and reading them back from
        // the graphics card crashes the game.
        private static readonly List<Renderer> _chunkDrawn = new List<Renderer>();
        private static readonly List<Collider> _chunkSolid = new List<Collider>();

        private static bool ChunkPrefix(DetachableObject __instance)
        {
            try
            {
                GameObject g = __instance.gameObject;
                g.layer = 18;
                g.GetComponentsInChildren(true, _chunkDrawn);
                foreach (Renderer r in _chunkDrawn) r.enabled = false;
                _chunkDrawn.Clear();
                g.GetComponentsInChildren(true, _chunkSolid);
                foreach (Collider c in _chunkSolid) c.enabled = false;
                _chunkSolid.Clear();
                return false;
            }
            catch (Exception e) { _chunkDrawn.Clear(); _chunkSolid.Clear(); DorsthTweaksPlugin.LogErrorOnce(e); return true; }
        }

        private static bool PotPrefix(Pot __instance)
        {
            try { UnityEngine.Object.Destroy(__instance.gameObject); return false; }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); return true; }
        }

        internal static void ReleasePrefabLights()
        {
            foreach (Light l in Darkened) if (l != null) l.enabled = true;
            Darkened.Clear();
            foreach (GameObject g in Parked) if (g != null) g.SetActive(true);
            Parked.Clear();
        }

        /// <summary>Once a map has settled (WarmUp): what's kept of the last map is dropped, and everything is gone over again.</summary>
        internal static void AfterLoad()
        {
            FadeLights.Clear();
            NotFlickering.RemoveAll(f => f == null);
            // blades of the maps before are gone: their trail lists and widths go too (they were kept until the setting changed,
            // one entry more for every blade that ever spawned). A blade still here is looked up again when it next swings.
            BladeTrails.Clear();
            Scenes.PruneGone(OwnWidth);
            Scenes.PruneGone(Swinging);
            if (On()) Safe(Apply);
        }

        private static void Safe(Action a)
        {
            try { a(); }
            // logged once per method called, not once for all of them
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e, a.Method.DeclaringType?.Name + "." + a.Method.Name, 0); }
        }

        /// <summary>Everything put back, then off again if the setting is on (when it changes, and when a map loads).</summary>
        private static void Apply() { Apply(true); }

        /// <summary>The same without the effect prefabs: right as a map's scene has loaded (see Install).</summary>
        private static void ApplyScene() { Apply(false); }

        private static void Apply(bool prefabLights)
        {
            foreach (var kv in Switched) if (kv.Key != null) kv.Key.enabled.value = kv.Value;
            Switched.Clear();
            foreach (var kv in AaModes) if (kv.Key != null) kv.Key.antialiasingMode = kv.Value;
            AaModes.Clear();
            foreach (Behaviour b in Off) if (b != null) b.enabled = true;
            Off.Clear();
            if (prefabLights)
            {
                foreach (Light l in Darkened) if (l != null) l.enabled = true;
                Darkened.Clear();
                foreach (GameObject g in Parked) if (g != null) g.SetActive(true);
                Parked.Clear();
            }
            FadeLights.Clear();
            // lights whose flicker was never started: started now that the setting is off
            if (!On())
            {
                foreach (WFX_LightFlicker f in NotFlickering)
                {
                    if (f != null && f.isActiveAndEnabled)
                    {
                        try { AccessTools.Method(typeof(WFX_LightFlicker), "Start")?.Invoke(f, null); } catch { }
                    }
                }
            }
            NotFlickering.Clear();
            foreach (Renderer r in Hidden) if (r != null) r.enabled = true;
            Hidden.Clear();
            RestoreBlades();
            if (!On()) ShotEffects.Release();
            if (!On()) return;
            Safe(NoCigSmoke);
            Safe(NoGunSmoke);
            Safe(NoBladeTrail);
            foreach (UnityEngine.Object o in Scenes.FindAll(typeof(PP.PostProcessVolume))) Volume((PP.PostProcessVolume)o);
            foreach (UnityEngine.Object o in Scenes.FindAll(typeof(PP.PostProcessLayer))) Layer((PP.PostProcessLayer)o);
            // light shafts and flares on the map
            foreach (Type t in new[] { typeof(VLB.VolumetricLightBeam), typeof(VLB.VolumetricDustParticles), typeof(LensFlare) })
                foreach (UnityEngine.Object o in Scenes.FindAll(t))
                {
                    var b = o as Behaviour;
                    if (b == null || !b.enabled || Scenes.IsMenu(b.gameObject.scene)) continue;
                    b.enabled = false;
                    Off.Add(b);
                }
            // the lights inside the effects the game's scripts make
            if (!prefabLights) return;
            foreach (EffectPrefabs.Ref r in EffectPrefabs.All())
            {
                bool effect = IsEffect(r.Field);
                if (!effect && r.Prefab.GetComponentInChildren<DamageZone>(true) == null) continue;
                if (effect)
                    foreach (Light l in r.Prefab.GetComponentsInChildren<Light>(true))
                        if (l.enabled) { l.enabled = false; Darkened.Add(l); }
                // sparks, debris and smoke of an effect, the cloud of a damage zone: off on the prefab (never the root)
                r.Prefab.GetComponentsInChildren(true, _parts);
                for (int i = 0; i < _parts.Count; i++)
                {
                    GameObject g = _parts[i].gameObject;
                    if (g != r.Prefab && g.activeSelf && (Cloud(g.name) || (effect && Shards(g.name)))) { g.SetActive(false); Parked.Add(g); }
                }
                _parts.Clear();
            }
            if (Parked.Count != _parkedSaid)
            {
                _parkedSaid = Parked.Count;
                DorsthTweaksPlugin.Log.LogInfo("Reduce VFX+: sparks, debris, smoke and gas cloud switched off in the effects: " + Parked.Count + " parts.");
            }
        }

        // the smoke of the cigarette and pipe cosmetics: off on the cosmetics themselves (so every one put on from now
        // has none) and on the ones players already wear
        private static void NoCigSmoke()
        {
            CosmeticsManager cm = CosmeticsManager.Instance;
            if (cm == null || cm.cigs == null) return;
            var names = new HashSet<string>();
            foreach (GameObject cig in cm.cigs)
            {
                if (cig == null) continue;
                names.Add(cig.name + "(Clone)");
                HideSmoke(cig);
            }
            foreach (HatPosition worn in UnityEngine.Object.FindObjectsOfType<HatPosition>(true))
                if (worn != null && names.Contains(worn.gameObject.name)) HideSmoke(worn.gameObject);
        }

        // the smoke trail a gun leaves after shooting: off on every gun loaded (the guns of the map and the ones they're
        // made from, so a gun spawned later has none either)
        private static void NoGunSmoke()
        {
            foreach (ItemBehaviour item in Found.Of<ItemBehaviour>())
                if (item != null && item.smokeTrail != null) HideSmoke(item.smokeTrail.gameObject);
        }

        // A blade (the katana and the like) has one trail: a single particle sat on the tip whose trail follows it
        // through the world, so it draws a line behind the tip even when you just walk, and the swing's trail is the
        // same line moving faster. It's kept running but drawn at no width, and gets its own width back while the
        // blade swings. (Turning it off pauses it; clearing it would remove the one particle until it comes back.)
        private static readonly Dictionary<int, ParticleSystem[]> BladeTrails = new Dictionary<int, ParticleSystem[]>();
        private static readonly Dictionary<ParticleSystem, float> OwnWidth = new Dictionary<ParticleSystem, float>();
        // each blade's own trail width by name ("Katana/trail"): a blade made after its original was thinned is
        // thin too, so its own width comes from here
        private static readonly Dictionary<string, float> WidthByName = new Dictionary<string, float>();

        private static string NameOf(ParticleSystem ps)
        {
            Transform t = ps.transform;
            MeleeWeapon blade = t.GetComponentInParent<MeleeWeapon>(true);
            string owner = blade != null ? blade.name : t.root.name;
            return owner.Replace("(Clone)", "").Trim() + "/" + ps.name;
        }
        private static readonly Dictionary<ParticleSystem, float> Swinging = new Dictionary<ParticleSystem, float>();
        private static AccessTools.FieldRef<MeleeWeapon, float> _fireTimer;
        private const float SwingShown = 0.6f;
        // the light of each light fade (asked for every frame it runs), and the flickers that were never started
        private static readonly Dictionary<int, Light> FadeLights = new Dictionary<int, Light>();
        private static readonly List<WFX_LightFlicker> NotFlickering = new List<WFX_LightFlicker>();

        // the blade's own trails: on the blade or just above it, never the player's own effects further up
        private static ParticleSystem[] TrailsOf(MeleeWeapon blade)
        {
            int id = blade.GetInstanceID();
            if (BladeTrails.TryGetValue(id, out ParticleSystem[] found)) return found;
            var list = new List<ParticleSystem>();
            Transform t = blade.transform;
            for (int up = 0; up < 3 && t != null && list.Count == 0; up++, t = t.parent)
            {
                if (up > 0 && (t.GetComponent<FirstPersonController>() != null || t.GetComponent<PlayerHealth>() != null || t.name.StartsWith("PlayerIK", StringComparison.Ordinal))) break;
                foreach (ParticleSystem ps in t.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    if (ps.trails.enabled && main.loop && main.playOnAwake) list.Add(ps);
                }
            }
            return BladeTrails[id] = list.ToArray();
        }

        private static void Thin(ParticleSystem ps)
        {
            if (ps == null) return;
            var trails = ps.trails;
            if (!OwnWidth.ContainsKey(ps))
            {
                string key = NameOf(ps);
                float w = trails.widthOverTrailMultiplier;
                if (w > 0f && !WidthByName.ContainsKey(key)) WidthByName[key] = w;
                OwnWidth[ps] = WidthByName.TryGetValue(key, out float known) ? known : (w > 0f ? w : 1f);
            }
            trails.widthOverTrailMultiplier = 0f;
        }

        private static void RestoreBlades()
        {
            foreach (var kv in OwnWidth)
            {
                if (kv.Key == null) continue;
                var trails = kv.Key.trails;
                trails.widthOverTrailMultiplier = kv.Value;
            }
            OwnWidth.Clear();
            Swinging.Clear();
            BladeTrails.Clear();
        }

        private static void NoBladeTrail()
        {
            foreach (MeleeWeapon blade in Found.Of<MeleeWeapon>())
                if (blade != null) foreach (ParticleSystem ps in TrailsOf(blade)) Thin(ps);
        }

        private static void Show(MeleeWeapon blade)
        {
            if (blade == null) return;
            foreach (ParticleSystem ps in TrailsOf(blade))
            {
                if (ps == null) continue;
                if (!OwnWidth.TryGetValue(ps, out float own)) { Thin(ps); own = OwnWidth[ps]; }
                var trails = ps.trails;
                if (!Swinging.ContainsKey(ps)) trails.widthOverTrailMultiplier = own;
                Swinging[ps] = Time.time + SwingShown;
            }
        }

        private static void SwingPrefix(MeleeWeapon __instance, out float __state)
        {
            __state = __instance != null && _fireTimer != null ? _fireTimer(__instance) : 1f;
        }

        // a swing started: the wait between swings was over and now runs again
        private static void SwingPostfix(MeleeWeapon __instance, float __state)
        {
            try { if (__state <= 0f && _fireTimer(__instance) > 0f) Show(__instance); }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static void SwungPostfix(MeleeWeapon __instance)
        {
            try { Show(__instance); }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private sealed class SwingClock : MonoBehaviour
        {
            private static readonly List<ParticleSystem> Done = new List<ParticleSystem>();

            private void Update()
            {
                if (Swinging.Count == 0) return;
                float now = Time.time;
                foreach (var kv in Swinging) if (kv.Key == null || now >= kv.Value) Done.Add(kv.Key);
                foreach (ParticleSystem ps in Done)
                {
                    Swinging.Remove(ps);
                    if (ps != null && On()) { var trails = ps.trails; trails.widthOverTrailMultiplier = 0f; }
                }
                Done.Clear();
            }
        }

        private static void HideSmoke(GameObject cig)
        {
            foreach (ParticleSystemRenderer r in cig.GetComponentsInChildren<ParticleSystemRenderer>(true))
                if (r.enabled) { r.enabled = false; Hidden.Add(r); }
            foreach (TrailRenderer r in cig.GetComponentsInChildren<TrailRenderer>(true))
                if (r.enabled) { r.enabled = false; Hidden.Add(r); }
        }

        private static bool IsEffect(string field)
        {
            foreach (string w in EffectWords) if (field.Contains(w)) return true;
            return false;
        }

        private static void Volume(PP.PostProcessVolume v)
        {
            if (v == null) return;
            PP.PostProcessProfile profile = v.HasInstantiatedProfile() ? v.profile : v.sharedProfile;
            if (profile == null) return;
            foreach (PP.PostProcessEffectSettings fx in profile.settings)
            {
                if (fx == null || !fx.enabled.value || Switched.ContainsKey(fx) || Array.IndexOf(Filters, fx.GetType()) < 0) continue;
                Switched[fx] = true;
                fx.enabled.value = false;
            }
        }

        private static void Layer(PP.PostProcessLayer l)
        {
            if (l == null || l.antialiasingMode == PP.PostProcessLayer.Antialiasing.None || AaModes.ContainsKey(l)) return;
            AaModes[l] = l.antialiasingMode;
            l.antialiasingMode = PP.PostProcessLayer.Antialiasing.None;
        }

        private static void PlayerPostfix(FirstPersonController __instance)
        {
            try
            {
                if (__instance == null) return;
                Players.RemoveAll(p => p == null);
                Players.Add(__instance);
                if (!On()) return;
                Volume(__instance.volume);
                if (__instance.playerCamera != null) Layer(__instance.playerCamera.GetComponent<PP.PostProcessLayer>());
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        // the kill greys the screen out: back to its normal color right away (the warp is off with the lens distortion)
        private static void KillPostfix()
        {
            try
            {
                foreach (FirstPersonController p in Players)
                    if (p != null && p.colorGrading != null) p.colorGrading.saturation.value = 0f;
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static bool Skip() { return false; }

        // the gun is still put away as the game does, only without its puff
        private static bool DespawnPrefix(Weapon __instance)
        {
            try { if (__instance != null) __instance.gameObject.SetActive(false); }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
            return false;
        }

        private static void DarkPostfix(Component __instance)
        {
            try
            {
                if (__instance == null) return;
                int id = __instance.GetInstanceID();
                Light l;
                if (!FadeLights.TryGetValue(id, out l)) FadeLights[id] = l = __instance.GetComponent<Light>();
                // remembered, so it comes back on when the setting goes off
                if (l != null && l.enabled) { l.enabled = false; Darkened.Add(l); }
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        // a flickering light (muzzle flashes): off, and its flicker never starts
        private static bool FlickerPrefix(Component __instance)
        {
            DarkPostfix(__instance);
            if (__instance is WFX_LightFlicker flicker) NotFlickering.Add(flicker);
            return false;
        }

        // a fire's light stays at its middle brightness and in its place instead of flickering
        private static bool FirePrefix(UnityStandardAssets.Effects.FireLight __instance)
        {
            try
            {
                Light l = _fireLight(__instance);
                if (l == null || !_burning(__instance)) return false;
                if (l.intensity != 1f) l.intensity = 1f;
                if (__instance.transform.localPosition != Vector3.up) __instance.transform.localPosition = Vector3.up;
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
            return false;
        }
    }

    // ==================================================================== quality settings

    /// <summary>
    /// Unity's quality settings on top of the game's own: Reduce Effects turns off real time shadows, MSAA and
    /// the reflection probes that redraw while you play.
    /// The game's values are kept and put back when a setting goes off. When the game
    /// sets a new quality level (its own setting), those are its values now and these go on top again.
    /// </summary>
    // Reduce VFX+ (and No Post-Processing, its own setting), what PerfLog's benchmark found costs frames and PerfTest tried in matches (more FPS each, in
    // brackets the average over 5 maps):
    //   every post-processing effect but color grading off, auto exposure too (Effects; color grading carries the
    //   game's brightness setting); the map's own animators stop moving their transforms while off screen (about 9%);
    //   the map's own particles aren't drawn (about 3%); far models switch to their simpler versions sooner, lod
    //   bias 0.3 (about 1%); the gun's camera draws forward instead of deferred.
    // Physics.autoSyncTransforms is left on: turning it off (about 9%) broke fast falling, whose lean moves colliders the
    // player's own movement then reads.
    // Players (their arms, bodies and hitboxes) and weapons (blade trails, muzzle flashes) are never touched. All of it goes back when the setting is turned off.
    internal static class Perf
    {
        private static bool On() { return DorsthTweaksPlugin.ReduceEffects.Value; }

        private static readonly HashSet<Animator> Culled = new HashSet<Animator>();
        private static readonly HashSet<ParticleSystemRenderer> ParticlesOff = new HashSet<ParticleSystemRenderer>();
        private static readonly HashSet<Renderer> RaysOff = new HashSet<Renderer>();
        // light rays and shafts some maps draw as see-through meshes (the sun's beams through fog)
        private static readonly System.Text.RegularExpressions.Regex RayWords = new System.Text.RegularExpressions.Regex(
            @"god ?rays?|light ?rays?|light ?shafts?|sun ?shafts?|sun ?beams?|light ?beams?|volumetric|shaft|lightcone|light ?cone",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        // whether a material is one of those, by its id: its name and shader name are read once a map, not for every mesh
        private static readonly Dictionary<int, bool> RayMaterial = new Dictionary<int, bool>();
        private static readonly HashSet<PP.PostProcessLayer> LayersOff = new HashSet<PP.PostProcessLayer>();
        private static readonly Dictionary<Camera, RenderingPath> GunCams = new Dictionary<Camera, RenderingPath>();
        private static readonly Dictionary<int, bool> Owned = new Dictionary<int, bool>();
        private static float _lod = -1f;
        // one delegate, made once: passing Small makes a new one every second
        private static readonly Action SmallNow = Small;
        // the cameras, read into one array that's kept, and the ones already looked at
        private static Camera[] _cams = new Camera[8];
        private static readonly HashSet<int> Checked = new HashSet<int>();

        private static bool NoPost() { return DorsthTweaksPlugin.NoPostProcessing.Value; }

        internal static void Install()
        {
            GameHook.Add(typeof(PP.PostProcessLayer), "OnPreCull", null, typeof(Perf), "PreCull", null, NoPost);
            DorsthTweaksPlugin.OnChange(DorsthTweaksPlugin.NoPostProcessing, Small);
            // the brightness slider decides whether color grading stays
            DorsthTweaksPlugin.OnChange(DorsthTweaksPlugin.ReduceEffects, () => { if (On()) Scan(); else Restore(); });
            Scenes.Loaded += (scene, mode) =>
            {
                RayMaterial.Clear();
                Checked.Clear();
                Owned.Clear();
                LayersOff.RemoveWhere(x => x == null);
                Culled.RemoveWhere(x => x == null);
                ParticlesOff.RemoveWhere(x => x == null);
                RaysOff.RemoveWhere(x => x == null);
                Scenes.PruneGone(GunCams);
                // (not for FishNet's holder scene, which loads along with every map)
                if (On() && !Scenes.IsMenu(scene) && scene.name != "MovedObjectsHolder") Host.Run(Scans());
            };
            Host.Run(Keep());
            Host.Recreated += () => Host.Run(Keep());
        }

        private static bool Mine(Component c)
        {
            int id = c.GetInstanceID();
            if (Owned.TryGetValue(id, out bool o)) return o;
            o = c.GetComponentInParent<FirstPersonController>() != null || c.GetComponentInParent<ItemBehaviour>() != null
                || c.GetComponentInParent<FPArms>() != null || c.GetComponentInParent<Weapon>() != null || c.GetComponentInParent<PlayerHealth>() != null;
            Owned[id] = o;
            return o;
        }


        // No Post-Processing: a camera's post-processing goes off before it draws its first frame (the menus keep theirs)
        private static bool PreCull(PP.PostProcessLayer __instance)
        {
            // in the main menu only the cameras that draw the scene (the blur behind the menus); screens drawn into a texture keep theirs
            if (Scenes.InMainMenu()) { Camera c = __instance.GetComponent<Camera>(); if (c == null || !UltimateTweaks.Keeper.WorldCamera(c)) return true; }
            __instance.enabled = false;
            LayersOff.Add(__instance);
            return false;
        }

        /// <summary>The game's quality setting changed, and set its own model detail: that's the one to go back to.</summary>
        internal static void QualityChanged()
        {
            if (_lod >= 0f)
            {
                _lod = QualitySettings.lodBias;
                Safe(SmallNow);
            }
        }

        // a map's things are all there a few seconds after it loads: looked over once it has settled, and once more
        private static System.Collections.IEnumerator Scans()
        {
            yield return null;
            float until = Time.realtimeSinceStartup + 5f;
            while (!WarmUp.Settled && Time.realtimeSinceStartup < until) yield return null;
            Safe(Scan);
            yield return new WaitForSecondsRealtime(3f);
            Safe(Scan);
        }

        private static void Scan()
        {
            if (!On() || Scenes.InMainMenu()) return;
            foreach (Animator a in UnityEngine.Object.FindObjectsOfType<Animator>())
                if (a.cullingMode == AnimatorCullingMode.AlwaysAnimate && !Mine(a)) { a.cullingMode = AnimatorCullingMode.CullUpdateTransforms; Culled.Add(a); }
            // (an effect that's kept to be used again is the game's own, made by a gun: left alone)
            foreach (ParticleSystemRenderer r in UnityEngine.Object.FindObjectsOfType<ParticleSystemRenderer>())
                if (r.enabled && !Mine(r) && !ShotEffects.IsKept(r.transform.root.gameObject)) { r.enabled = false; ParticlesOff.Add(r); }
            // light rays: see-through map meshes named for it (their object, material or shader)
            foreach (MeshRenderer r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
            {
                if (!r.enabled) continue;
                Material m = r.sharedMaterial;
                if (m == null || m.renderQueue < 2900 || Mine(r)) continue;
                int id = m.GetInstanceID();
                bool rays;
                if (!RayMaterial.TryGetValue(id, out rays))
                    RayMaterial[id] = rays = RayWords.IsMatch(m.name) || (m.shader != null && RayWords.IsMatch(m.shader.name));
                if (rays || RayWords.IsMatch(r.name)) { r.enabled = false; RaysOff.Add(r); }
            }
        }

        // the gun camera comes with each round's player: checked each second
        private static System.Collections.IEnumerator Keep()
        {
            var wait = new WaitForSecondsRealtime(1f);
            while (true)
            {
                Safe(SmallNow);
                yield return wait;
            }
        }

        private static void Small()
        {
            bool on = On() && !Scenes.InMainMenu();
            if (on)
            {
                if (QualitySettings.lodBias != 0.3f) { if (_lod < 0f) _lod = QualitySettings.lodBias; QualitySettings.lodBias = 0.3f; }
                // each camera is looked at once (reading a camera's name makes a new string), into an array that's kept
                if (_cams.Length < Camera.allCamerasCount) _cams = new Camera[Camera.allCamerasCount * 2];
                int n = Camera.GetAllCameras(_cams);
                for (int i = 0; i < n; i++)
                {
                    Camera cam = _cams[i];
                    _cams[i] = null;
                    if (cam == null || !Checked.Add(cam.GetInstanceID())) continue;
                    if (cam.name == "WeaponCam" && cam.renderingPath != RenderingPath.Forward && !GunCams.ContainsKey(cam)) { GunCams[cam] = cam.renderingPath; cam.renderingPath = RenderingPath.Forward; }
                }
            }
            else
            {
                if (_lod >= 0f) { QualitySettings.lodBias = _lod; _lod = -1f; }
                foreach (var kv in GunCams) if (kv.Key != null) kv.Key.renderingPath = kv.Value;
                GunCams.Clear();
                Checked.Clear();
            }
            if (!NoPost() && LayersOff.Count > 0) { foreach (var l in LayersOff) if (l != null) l.enabled = true; LayersOff.Clear(); }
        }

        private static void Restore()
        {
            foreach (var a in Culled) if (a != null) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Culled.Clear();
            foreach (var r in ParticlesOff) if (r != null) r.enabled = true;
            ParticlesOff.Clear();
            foreach (var r in RaysOff) if (r != null) r.enabled = true;
            RaysOff.Clear();
            Small();
        }

        private static void Safe(Action a)
        {
            try { a(); }
            // logged once per method called, not once for all of them
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e, a.Method.DeclaringType?.Name + "." + a.Method.Name, 0); }
        }
    }

    internal static class Quality
    {
        private static bool _saved;
        private static ShadowQuality _shadows;
        private static bool _reflections;
        private static int _textures, _msaa;

        internal static void Install()
        {
            GameHook.Add(typeof(Settings), "SetQualityLevelDropdown", null, typeof(Quality), null, "QualityPostfix", () => true);
            DorsthTweaksPlugin.LowerTextures.SettingChanged += (sender, args) => Safe(ApplyLower);
            // maps: WarmUp calls BeforeUnload and AfterLoad once a map has settled
            Safe(Apply);
        }

        /// <summary>
        /// A new map, just before the last one's leftovers are freed: every material goes back to its own texture and
        /// nothing here points to a texture of the game's anymore, so the last map's textures aren't held on to. The
        /// small copies themselves stay (they are this mod's own): AfterLoad uses them again for every texture that
        /// is still there, and destroys the ones whose texture went with the map.
        /// </summary>
        internal static void BeforeUnload()
        {
            // copies still waiting to be made were for the map that's going
            _run++;
            Queue.Clear();
            if (Swapped.Count > 0 || Originals.Count > 0) Release();
        }

        /// <summary>
        /// A map (or the next round on the same one) has loaded: its materials get the smaller copies. A texture that
        /// already has a copy (guns, players, effects: they stay loaded all game) gets that one again; only textures
        /// new with this map get a copy made, a few each frame while the map is still settling.
        /// </summary>
        internal static void AfterLoad()
        {
            if (Swapped.Count == 0 && DorsthTweaksPlugin.LowerTextures.Value <= 0) return;
            Swapped.RemoveAll(sw => sw.Mat == null);
            Safe(ApplyLower);
        }

        private static void Safe(Action a)
        {
            try { a(); }
            // logged once per method called, not once for all of them
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e, a.Method.DeclaringType?.Name + "." + a.Method.Name, 0); }
        }

        // the game just set its own values: those are the ones to keep now
        private static void QualityPostfix()
        {
            _saved = false;
            Safe(Apply);
            Safe(Perf.QualityChanged);
        }

        internal static void Apply()
        {
            bool reduce = DorsthTweaksPlugin.ReduceEffects.Value, probes = reduce;
            if (!_saved)
            {
                // the game's own values, what everything goes back to
                _shadows = QualitySettings.shadows;
                _reflections = QualitySettings.realtimeReflectionProbes;
                _textures = QualitySettings.masterTextureLimit;
                _msaa = QualitySettings.antiAliasing;
                _saved = true;
            }
            // only what differs is set: a new texture size limit reloads every texture
            ShadowQuality shadows = reduce ? ShadowQuality.Disable : _shadows;
            if (QualitySettings.shadows != shadows) QualitySettings.shadows = shadows;
            int msaa = reduce ? 0 : _msaa;
            if (QualitySettings.antiAliasing != msaa) QualitySettings.antiAliasing = msaa;
            bool reflections = !probes && _reflections;
            if (QualitySettings.realtimeReflectionProbes != reflections) QualitySettings.realtimeReflectionProbes = reflections;
            // the game's texture size limit stays the game's: it would lower the menus and HUD too (Lower Textures
            // swaps in smaller copies of the world's textures instead, ApplyLower)
            if (QualitySettings.masterTextureLimit != _textures) QualitySettings.masterTextureLimit = _textures;
        }

        // Lower Textures: every texture the map, guns, players and effects draw is swapped in their materials for a copy
        // made from its own smaller versions (half the size for each step), the same near and far, so the graphics card
        // reads less for each frame. The copy is made on the graphics card (nothing read back). Textures and materials
        // the menus, HUD and fonts use are left as they are. At 0 every material gets its own texture back.
        // From map to map the copies are kept: most of them belong to guns, players and effects, which stay loaded all
        // game. Remaking all of them (about 400) at every map cost one frame of 119 to 129 ms right after loading.
        // Before a map is freed the materials get their own textures back and nothing of the game's is held, so the
        // map's textures can go; after the next one loaded, a texture that is still there gets its copy again, a copy
        // whose texture is gone is destroyed, and only textures new with the map get one made, at most PerFrame a frame.
        private static readonly List<int> TexProps = new List<int>();
        // a texture's id to its copy (null: it has no smaller version). Kept from map to map; by id, so the texture
        // itself isn't held on to
        private static readonly Dictionary<int, Texture2D> Copies = new Dictionary<int, Texture2D>();
        private static readonly HashSet<int> CopyIds = new HashSet<int>();
        // a copy's id to the texture it was made from. Only while materials hold the copy: this does hold the texture,
        // so it's emptied before a map is freed (Release)
        private static readonly Dictionary<int, Texture2D> Originals = new Dictionary<int, Texture2D>();
        // the same as numbers only, kept as long as the copy
        private static readonly Dictionary<int, int> OwnIdOf = new Dictionary<int, int>();
        // what a texture looked like when its copy was made: a kept copy is only used again for a texture that still
        // looks like that
        private struct Shape { public int W, H, Mips; public UnityEngine.Experimental.Rendering.GraphicsFormat Format; }
        private static readonly Dictionary<int, Shape> ShapeOf = new Dictionary<int, Shape>();
        // the textures met in the last look through the materials: a copy of any other one isn't needed anymore
        private static readonly HashSet<int> Used = new HashSet<int>();
        private static readonly HashSet<int> Fresh = new HashSet<int>();
        private static readonly List<int> Drop = new List<int>();
        private struct Swap { public Material Mat; public int Prop; public Texture2D Own; }
        private static readonly List<Swap> Swapped = new List<Swap>();
        // places waiting for a copy that has to be made first
        private static readonly List<Swap> Queue = new List<Swap>();
        // copies made in one frame at most: making all of a map's at once was one frame of about 120 ms
        private const int PerFrame = 8;
        private static int _level, _limit = -1, _run;
        private static readonly string[] UiShaders = { "UI/", "TextMeshPro/", "Sprites/", "GUI/", "Hidden/" };

        private static void AddTextures(Material m, HashSet<Texture> into)
        {
            if (m == null) return;
            TexProps.Clear();
            m.GetTexturePropertyNameIDs(TexProps);
            foreach (int id in TexProps)
            {
                Texture t = m.GetTexture(id);
                if (t != null) into.Add(t);
            }
        }

        private static bool UnderCanvas(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.GetComponent<Canvas>() != null) return true;
            return false;
        }

        // everything the menus, HUD and fonts draw with
        private static void UiParts(HashSet<Texture> textures, HashSet<Material> materials)
        {
            foreach (Renderer r in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                try
                {
                    if (r == null) continue;
                    GameObject go = r.gameObject;
                    if (go.layer != 5 && go.GetComponent<TMP_Text>() == null && !UnderCanvas(go.transform)) continue;
                    foreach (Material m in r.sharedMaterials) if (m != null) { materials.Add(m); AddTextures(m, textures); }
                }
                catch { }
            }
            foreach (Graphic g in Found.Of<Graphic>())
            {
                try
                {
                    if (g == null) continue;
                    if (g.mainTexture != null) textures.Add(g.mainTexture);
                    if (g.material != null) { materials.Add(g.material); AddTextures(g.material, textures); }
                }
                catch { }
            }
            foreach (Sprite sp in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                try { if (sp != null && sp.texture != null) textures.Add(sp.texture); }
                catch { }
            }
            foreach (TMP_FontAsset f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                try
                {
                    if (f == null) continue;
                    if (f.material != null) materials.Add(f.material);
                    if (f.atlasTextures != null) foreach (Texture2D t in f.atlasTextures) if (t != null) textures.Add(t);
                }
                catch { }
            }
        }

        private static bool UiShader(Material m)
        {
            string n = m.shader != null ? m.shader.name : "";
            foreach (string u in UiShaders) if (n.StartsWith(u, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void ApplyLower()
        {
            int lower = Mathf.Clamp(DorsthTweaksPlugin.LowerTextures.Value, 0, 3);
            int limit = QualitySettings.masterTextureLimit;
            // another size, or the game's own texture quality changed (a copy is cut for the quality it was made at):
            // every copy is made anew
            if (lower != _level || (limit != _limit && Copies.Count > 0)) Restore();
            _level = lower;
            _limit = limit;
            if (lower == 0 || (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0) return;
            _run++;
            Queue.Clear();
            var uiTextures = new HashSet<Texture>();
            var uiMaterials = new HashSet<Material>();
            UiParts(uiTextures, uiMaterials);
            Used.Clear();
            Fresh.Clear();
            int swapped = 0;
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                try
                {
                    if (m == null || uiMaterials.Contains(m) || UiShader(m)) continue;
                    TexProps.Clear();
                    m.GetTexturePropertyNameIDs(TexProps);
                    foreach (int prop in TexProps)
                    {
                        var t = m.GetTexture(prop) as Texture2D;
                        if (t == null) continue;
                        int id = t.GetInstanceID();
                        // already a copy in that place: the texture it was made from is still in use
                        if (CopyIds.Contains(id)) { int own; if (OwnIdOf.TryGetValue(id, out own)) Used.Add(own); continue; }
                        if (uiTextures.Contains(t)) continue;
                        Used.Add(id);
                        Texture2D copy;
                        if (!Kept(t, id, out copy))
                        {
                            // no copy yet: made in the next frames (MakeQueued)
                            Queue.Add(new Swap { Mat = m, Prop = prop, Own = t });
                            Fresh.Add(id);
                            continue;
                        }
                        if (copy == null) continue;
                        m.SetTexture(prop, copy);
                        Originals[copy.GetInstanceID()] = t;
                        Swapped.Add(new Swap { Mat = m, Prop = prop, Own = t });
                        swapped++;
                    }
                }
                catch { }
            }
            // copies of textures no material has anymore (a map that's gone): destroyed and forgotten, so neither the
            // copies nor these lists grow from map to map
            Drop.Clear();
            foreach (int id in Copies.Keys) if (!Used.Contains(id)) Drop.Add(id);
            int dropped = 0, kept = 0;
            foreach (int id in Drop)
            {
                Texture2D c = Copies[id];
                if ((object)c != null)
                {
                    int cid = c.GetInstanceID();
                    CopyIds.Remove(cid);
                    OwnIdOf.Remove(cid);
                    Originals.Remove(cid);
                    if (c) UnityEngine.Object.Destroy(c);
                    dropped++;
                }
                Copies.Remove(id);
                ShapeOf.Remove(id);
            }
            Drop.Clear();
            foreach (Texture2D c in Copies.Values) if (c) kept++;
            DorsthTweaksPlugin.Log.LogInfo("Lower Textures: " + kept + " small copies kept, " + Fresh.Count + " to make, " + dropped + " dropped (in " + swapped + " places now, game texture limit " + limit + ")");
            Fresh.Clear();
            if (Queue.Count > 0) Host.Run(MakeQueued(_run));
        }

        // The copy a texture already has, from an earlier map or an earlier look. False: it has none (or the one it
        // had is gone, or was made for a texture that looked different), so one has to be made. True with null: it's
        // known to have no smaller version.
        private static bool Kept(Texture2D t, int id, out Texture2D copy)
        {
            copy = null;
            Texture2D made;
            if (!Copies.TryGetValue(id, out made)) return false;
            Shape was;
            bool same = ShapeOf.TryGetValue(id, out was) && was.W == t.width && was.H == t.height && was.Mips == t.mipmapCount && was.Format == t.graphicsFormat;
            if (same && (object)made == null) return true;
            if (same && made) { copy = made; return true; }
            Forget(id, made);
            return false;
        }

        private static void Forget(int id, Texture2D made)
        {
            if ((object)made != null)
            {
                int cid = made.GetInstanceID();
                CopyIds.Remove(cid);
                OwnIdOf.Remove(cid);
                Originals.Remove(cid);
                if (made) UnityEngine.Object.Destroy(made);
            }
            Copies.Remove(id);
            ShapeOf.Remove(id);
        }

        // The copies that have to be made, a few each frame. Stops when the materials are looked through again or a
        // new map comes (_run moves on): what's left of the list is not for this map anymore.
        private static System.Collections.IEnumerator MakeQueued(int run)
        {
            int i = 0;
            while (run == _run && i < Queue.Count)
            {
                int made = 0;
                for (; made < PerFrame && i < Queue.Count; i++)
                {
                    Swap q = Queue[i];
                    try
                    {
                        if (q.Mat == null || q.Own == null) continue;
                        // only if that place still has its own texture
                        if (q.Mat.GetTexture(q.Prop) != q.Own) continue;
                        bool isNew = !Copies.ContainsKey(q.Own.GetInstanceID());
                        Texture2D copy = CopyOf(q.Own, _level);
                        if (isNew && copy != null) made++;
                        if (copy == null) continue;
                        q.Mat.SetTexture(q.Prop, copy);
                        Originals[copy.GetInstanceID()] = q.Own;
                        Swapped.Add(q);
                    }
                    catch { }
                }
                yield return null;
            }
            if (run == _run) Queue.Clear();
        }

        // a copy of the texture from its version `lower` steps smaller, made on the graphics card; null when it has
        // none that small or can't be copied
        private static Texture2D CopyOf(Texture2D t, int lower)
        {
            int id = t.GetInstanceID();
            if (Copies.TryGetValue(id, out Texture2D made))
            {
                // null on purpose: this texture has no smaller version. A copy that's been destroyed is made again
                if ((object)made == null) return null;
                if (made) return made;
            }
            Texture2D copy = null;
            try
            {
                // with the game's own texture quality turned down, the graphics card leaves out the biggest versions of
                // both the texture and the copy, and counts versions from the biggest one it holds: version i of the
                // copy is version i of the texture's, n smaller. The last ones of the texture's aren't there
                int mips = t.mipmapCount, n = Mathf.Min(lower, mips - 1);
                int limit = Mathf.Max(0, QualitySettings.masterTextureLimit);
                int srcLimit = t.ignoreMipmapLimit ? 0 : limit;
                int w = t.width >> n, h = t.height >> n;
                if (n <= 0 || w < 4 || h < 4 || mips - n - limit < 1 || mips - srcLimit < 1) { Copies[id] = null; Note(t, id); return null; }
                // a crunch-compressed texture can't be copied on the graphics card (Unity logs an error for every size of it,
                // every time): it stays as it is
                TextureFormat packed = t.format;
                if (packed == TextureFormat.DXT1Crunched || packed == TextureFormat.DXT5Crunched
                    || packed == TextureFormat.ETC_RGB4Crunched || packed == TextureFormat.ETC2_RGBA8Crunched) { Copies[id] = null; Note(t, id); return null; }
                var format = t.graphicsFormat;
                if (!SystemInfo.IsFormatSupported(format, UnityEngine.Experimental.Rendering.FormatUsage.Sample)) { Copies[id] = null; Note(t, id); return null; }
                copy = new Texture2D(w, h, format, mips - n, UnityEngine.Experimental.Rendering.TextureCreationFlags.MipChain)
                {
                    name = t.name, filterMode = t.filterMode, wrapModeU = t.wrapModeU, wrapModeV = t.wrapModeV, anisoLevel = t.anisoLevel,
                    mipMapBias = t.mipMapBias,
                };
                // on the graphics card only: no copy kept in memory
                copy.Apply(false, true);
                int dstLimit = copy.ignoreMipmapLimit ? 0 : limit;
                for (int i = 0; i < mips - n - dstLimit; i++)
                {
                    int from = i + n + dstLimit - srcLimit;
                    if (from < 0 || from >= mips - srcLimit) break;
                    Graphics.CopyTexture(t, 0, from, copy, 0, i);
                }
            }
            catch
            {
                if (copy != null) UnityEngine.Object.Destroy(copy);
                copy = null;
            }
            Copies[id] = copy;
            Note(t, id);
            if (copy != null) { CopyIds.Add(copy.GetInstanceID()); OwnIdOf[copy.GetInstanceID()] = id; }
            return copy;
        }

        private static void Note(Texture2D t, int id)
        {
            try { ShapeOf[id] = new Shape { W = t.width, H = t.height, Mips = t.mipmapCount, Format = t.graphicsFormat }; }
            catch { ShapeOf.Remove(id); }
        }

        /// <summary>
        /// Every material gets its own texture back, and nothing here holds a texture of the game's anymore. The copies
        /// stay, for AfterLoad to use again.
        /// </summary>
        private static void Release()
        {
            foreach (Swap sw in Swapped)
            {
                try { if (sw.Mat != null && sw.Own != null && sw.Mat.GetTexture(sw.Prop) is Texture2D now && CopyIds.Contains(now.GetInstanceID())) sw.Mat.SetTexture(sw.Prop, sw.Own); }
                catch { }
            }
            Swapped.Clear();
            // a gun or player that came in after the swap made its own copy of the material, with the smaller texture
            // in it: those get theirs back too, before the smaller ones are thrown away (they'd draw broken textures
            // until the gun came back otherwise)
            if (Originals.Count > 0)
                foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                {
                    try
                    {
                        if (m == null) continue;
                        TexProps.Clear();
                        m.GetTexturePropertyNameIDs(TexProps);
                        foreach (int prop in TexProps)
                            if (m.GetTexture(prop) is Texture2D now && now != null && Originals.TryGetValue(now.GetInstanceID(), out Texture2D own) && own != null)
                                m.SetTexture(prop, own);
                    }
                    catch { }
                }
            Originals.Clear();
        }

        /// <summary>Everything back as the game had it, and every copy destroyed (the setting changed).</summary>
        private static void Restore()
        {
            _run++;
            Queue.Clear();
            Release();
            foreach (Texture2D c in Copies.Values) if (c) UnityEngine.Object.Destroy(c);
            Copies.Clear();
            CopyIds.Clear();
            OwnIdOf.Clear();
            ShapeOf.Clear();
        }
    }

    // ==================================================================== rain and static textures

    /// <summary>
    /// Disable Rain: rain is particle effects placed in the map (no code of its own), found by name ("Rain", "VFX_Rain",
    /// "HeavyRain", not "Terrain" or "Train") and switched off with its sounds when a map loads.
    ///
    /// Static Textures: in maps, animated materials get their speed settings zeroed, the water scripts stop, the water drops
    /// its reflection and refraction cameras (each one renders the scene again every frame, which is where most of the FPS
    /// comes from), light beam noise and the sky stop. All of it comes back when it's turned off.
    /// </summary>
    internal static class World
    {
        private static readonly Func<bool> Textures = () => DorsthTweaksPlugin.StaticTextures.Value;

        private struct BeamNoise
        {
            public bool UseGlobal;
            public Vector3 Local;
        }

        private struct RainState
        {
            public bool Emission, Drawn, Playing;
        }

        private static readonly Dictionary<Water, Water.WaterMode> WaterModes = new Dictionary<Water, Water.WaterMode>();
        private static readonly List<PlanarReflection> Reflections = new List<PlanarReflection>();
        private static readonly Dictionary<VLB.VolumetricLightBeam, BeamNoise> Beams = new Dictionary<VLB.VolumetricLightBeam, BeamNoise>();
        private static readonly Dictionary<ParticleSystem, RainState> RainParticles = new Dictionary<ParticleSystem, RainState>();
        private static readonly List<AudioSource> RainSounds = new List<AudioSource>();

        internal static void Install()
        {
            Type me = typeof(World);
            GameHook.Add(typeof(Water), "Update", null, me, "Skip", null, Textures);
            GameHook.Add(typeof(WaterBasic), "Update", null, me, "Skip", null, Textures);
            GameHook.Add(typeof(PlanarReflection), "WaterTileBeingRendered", null, me, "Skip", null, Textures);
            GameHook.Add(typeof(Wobble), "Update", null, me, "Skip", null, Textures);
            GameHook.Add(typeof(DF_SkyRotation), "Update", null, me, "SkyPrefix", null,
                () => DorsthTweaksPlugin.StaticTextures.Value || DorsthTweaksPlugin.ReduceUIAnimations.Value);

            DorsthTweaksPlugin.OnChange(DorsthTweaksPlugin.StaticTextures, ApplyTextures);
            DorsthTweaksPlugin.OnChange(DorsthTweaksPlugin.DisableRain, ApplyRain);
            Scenes.Loaded += OnSceneLoaded;
            SceneManager.activeSceneChanged += (previous, now) =>
            {
                if (!now.IsValid() || !now.isLoaded) return;
                try
                {
                    // some materials are shared: stilled for the menus means moving again in maps, and the other way round
                    Stills.Release(Scenes.IsMenu(now) ? Stills.Maps : Stills.Menu);
                    StillSky();
                }
                catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
            };
        }

        private static bool Skip() { return false; }

        /// <summary>The active scene's sky stands still: in the menus with Reduce UI Animations, in maps with Static Textures.</summary>
        internal static void StillSky()
        {
            Scene active = SceneManager.GetActiveScene();
            if (!active.IsValid() || !active.isLoaded) return; // no scene yet: there's no sky to read
            bool menu = Scenes.IsMenu(active);
            if (menu ? DorsthTweaksPlugin.ReduceUIAnimations.Value : Textures())
                Stills.Freeze(RenderSettings.skybox, menu ? Stills.Menu : Stills.Maps);
        }

        private static bool SkyPrefix()
        {
            return !(Scenes.InMainMenu() ? DorsthTweaksPlugin.ReduceUIAnimations.Value : DorsthTweaksPlugin.StaticTextures.Value);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try
            {
                if (Scenes.IsMenu(scene)) return;
                if (DorsthTweaksPlugin.DisableRain.Value) RemoveRain(scene);
                if (!Textures()) return;
                StillTextures(scene);
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static void ApplyTextures()
        {
            if (Textures()) StillTextures(null);
            else RestoreTextures();
        }

        private static bool InMap(Component c, Scene? only)
        {
            if (c == null) return false;
            Scene s = c.gameObject.scene;
            return !Scenes.IsMenu(s) && (only == null || s == only.Value);
        }

        private static void StillTextures(Scene? only)
        {
            Scenes.PruneGone(WaterModes);
            Scenes.PruneGone(Beams);
            Reflections.RemoveAll(p => p == null);
            foreach (UnityEngine.Object o in Scenes.FindAll(typeof(Renderer)))
            {
                var r = (Renderer)o;
                if (r is ParticleSystemRenderer || !InMap(r, only)) continue;
                foreach (Material m in r.sharedMaterials) Stills.Freeze(m, Stills.Maps);
            }
            StillSky();
            foreach (UnityEngine.Object o in Scenes.FindAll(typeof(Water)))
            {
                var w = (Water)o;
                if (!InMap(w, only) || WaterModes.ContainsKey(w) || w.waterMode == Water.WaterMode.Simple) continue;
                WaterModes[w] = w.waterMode;
                w.waterMode = Water.WaterMode.Simple;
            }
            foreach (UnityEngine.Object o in Scenes.FindAll(typeof(PlanarReflection)))
            {
                var p = (PlanarReflection)o;
                if (!InMap(p, only) || !p.enabled) continue;
                p.enabled = false; // also switches the water shader to its simple look
                Reflections.Add(p);
            }
            foreach (UnityEngine.Object o in Scenes.FindAll(typeof(VLB.VolumetricLightBeam)))
            {
                var b = (VLB.VolumetricLightBeam)o;
                if (!InMap(b, only) || Beams.ContainsKey(b) || b.noiseMode == VLB.NoiseMode.Disabled) continue;
                Beams[b] = new BeamNoise { UseGlobal = b.noiseVelocityUseGlobal, Local = b.noiseVelocityLocal };
                b.noiseVelocityUseGlobal = false;
                b.noiseVelocityLocal = Vector3.zero;
                b.UpdateAfterManualPropertyChange();
            }
        }

        private static void RestoreTextures()
        {
            Stills.Release(Stills.Maps);
            foreach (KeyValuePair<Water, Water.WaterMode> kv in WaterModes)
                if (kv.Key != null) kv.Key.waterMode = kv.Value;
            WaterModes.Clear();
            foreach (PlanarReflection p in Reflections)
                if (p != null) p.enabled = true;
            Reflections.Clear();
            foreach (KeyValuePair<VLB.VolumetricLightBeam, BeamNoise> kv in Beams)
            {
                VLB.VolumetricLightBeam b = kv.Key;
                if (b == null) continue;
                b.noiseVelocityUseGlobal = kv.Value.UseGlobal;
                b.noiseVelocityLocal = kv.Value.Local;
                b.UpdateAfterManualPropertyChange();
            }
            Beams.Clear();
        }

        private static void ApplyRain()
        {
            if (DorsthTweaksPlugin.DisableRain.Value)
            {
                RemoveRain(null);
                return;
            }
            foreach (KeyValuePair<ParticleSystem, RainState> kv in RainParticles)
            {
                ParticleSystem ps = kv.Key;
                if (ps == null) continue;
                ParticleSystem.EmissionModule emission = ps.emission;
                emission.enabled = kv.Value.Emission;
                var drawn = ps.GetComponent<ParticleSystemRenderer>();
                if (drawn != null) drawn.enabled = kv.Value.Drawn;
                if (kv.Value.Playing) ps.Play(false);
            }
            RainParticles.Clear();
            foreach (AudioSource a in RainSounds)
                if (a != null) a.enabled = true;
            RainSounds.Clear();
        }

        /// <summary>Rain in maps: its particles stop, clear and aren't drawn, its sounds are off. Never a whole object.</summary>
        private static void RemoveRain(Scene? only)
        {
            Scenes.PruneGone(RainParticles);
            RainSounds.RemoveAll(a => a == null);
            foreach (UnityEngine.Object o in Scenes.FindAll(typeof(ParticleSystem)))
            {
                var ps = (ParticleSystem)o;
                if (!InMap(ps, only) || RainParticles.ContainsKey(ps) || !RainNamed(ps)) continue;
                ParticleSystem.EmissionModule emission = ps.emission;
                var drawn = ps.GetComponent<ParticleSystemRenderer>();
                RainParticles[ps] = new RainState { Emission = emission.enabled, Drawn = drawn != null && drawn.enabled, Playing = ps.isPlaying };
                emission.enabled = false;
                if (drawn != null) drawn.enabled = false;
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            foreach (UnityEngine.Object o in Scenes.FindAll(typeof(AudioSource)))
            {
                var sound = (AudioSource)o;
                if (!InMap(sound, only) || !sound.enabled) continue;
                // a rain sound by its object's name, or a looping one by its clip's name
                if (!RainNamed(sound) && !(sound.loop && sound.clip != null && IsRainName(sound.clip.name))) continue;
                sound.enabled = false;
                RainSounds.Add(sound);
            }
        }

        /// <summary>Rain by its own name, or part of a rain effect (like "Splashes" under "Rain").</summary>
        private static bool RainNamed(Component c)
        {
            if (IsRainName(c.name)) return true;
            Transform parent = c.transform.parent;
            return parent != null && IsRainName(parent.name);
        }

        /// <summary>"rain" as a word or word start (Rain, VFX_Rain, HeavyRain, rain_splash), not in Terrain/Train/Brain or rainbow.</summary>
        private static bool IsRainName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            if (lower.Contains("pluie") || lower.Contains("drizzle") || lower.Contains("precipitation")) return true;
            for (int i = lower.IndexOf("rain", StringComparison.Ordinal); i >= 0; i = lower.IndexOf("rain", i + 1, StringComparison.Ordinal))
            {
                bool wordStart = i == 0 || !char.IsLetter(name[i - 1]) || (name[i] == 'R' && name[i + 1] == 'a'); // HeavyRain, not TRAIN
                if (wordStart && string.CompareOrdinal(lower, i, "rainbow", 0, 7) != 0) return true;
            }
            return false;
        }
    }

    // ==================================================================== shared: still materials

    /// <summary>
    /// Stills an animated material by zeroing its speed-like shader settings (speed, scroll, panner, fps, frame rate, time
    /// scale, the water's bump direction). Each material is remembered for the menus or maps and gets its values back when
    /// neither wants it still.
    /// </summary>
    internal static class Stills
    {
        internal const int Menu = 1, Maps = 2;

        private struct Saved
        {
            public int Id;
            public bool Vector;
            public Vector4 Value;
        }

        private sealed class Frozen
        {
            public int Owners;
            public Saved[] Values;
        }

        private static readonly Saved[] NoProps = new Saved[0];
        private static readonly Dictionary<Shader, Saved[]> SpeedProps = new Dictionary<Shader, Saved[]>();
        // by material id: a kept reference would keep every map's materials (and their textures) in memory after the map is gone
        private static readonly Dictionary<int, Frozen> FrozenMaterials = new Dictionary<int, Frozen>();

        /// <summary>Forgets the materials of a map that's gone (the list grew with every map).</summary>
        internal static void ForgetGone()
        {
            if (FrozenMaterials.Count == 0) return;
            var alive = new HashSet<int>();
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null) alive.Add(m.GetInstanceID());
            var gone = new List<int>();
            foreach (int id in FrozenMaterials.Keys)
                if (!alive.Contains(id)) gone.Add(id);
            foreach (int id in gone) FrozenMaterials.Remove(id);
        }

        internal static void Freeze(Material m, int owner)
        {
            if (m == null) return;
            int id = m.GetInstanceID();
            Frozen frozen;
            if (FrozenMaterials.TryGetValue(id, out frozen))
            {
                frozen.Owners |= owner;
                return;
            }
            Saved[] props = PropsOf(m.shader);
            if (props.Length == 0) return;
            var saved = new List<Saved>(props.Length);
            foreach (Saved p in props)
            {
                if (!m.HasProperty(p.Id)) continue;
                if (p.Vector)
                {
                    saved.Add(new Saved { Id = p.Id, Vector = true, Value = m.GetVector(p.Id) });
                    m.SetVector(p.Id, Vector4.zero);
                }
                else
                {
                    saved.Add(new Saved { Id = p.Id, Value = new Vector4(m.GetFloat(p.Id), 0f, 0f, 0f) });
                    m.SetFloat(p.Id, 0f);
                }
            }
            if (saved.Count > 0) FrozenMaterials[id] = new Frozen { Owners = owner, Values = saved.ToArray() };
        }

        /// <summary>Materials no one wants still any more get their values back (found by id; one gone with its map is just forgotten).</summary>
        internal static void Release(int owner)
        {
            var done = new Dictionary<int, Frozen>();
            foreach (KeyValuePair<int, Frozen> kv in FrozenMaterials)
            {
                kv.Value.Owners &= ~owner;
                if (kv.Value.Owners == 0) done[kv.Key] = kv.Value;
            }
            if (done.Count == 0) return;
            foreach (int id in done.Keys) FrozenMaterials.Remove(id);
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                Frozen f;
                if (m == null || !done.TryGetValue(m.GetInstanceID(), out f)) continue;
                foreach (Saved s in f.Values)
                {
                    if (s.Vector) m.SetVector(s.Id, s.Value);
                    else m.SetFloat(s.Id, s.Value.x);
                }
            }
        }

        private static Saved[] PropsOf(Shader shader)
        {
            if (shader == null) return NoProps;
            Saved[] props;
            if (SpeedProps.TryGetValue(shader, out props)) return props;
            var found = new List<Saved>();
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                ShaderPropertyType type = shader.GetPropertyType(i);
                if (type != ShaderPropertyType.Float && type != ShaderPropertyType.Range && type != ShaderPropertyType.Vector) continue;
                if (IsSpeedName(shader.GetPropertyName(i))) found.Add(new Saved { Id = shader.GetPropertyNameId(i), Vector = type == ShaderPropertyType.Vector });
            }
            props = found.Count == 0 ? NoProps : found.ToArray();
            SpeedProps[shader] = props;
            return props;
        }

        private static bool IsSpeedName(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("speed") || n.Contains("scroll") || n.Contains("panner") || n.Contains("panning") || n.Contains("fps")
                || n.Contains("framerate") || n.Contains("frame_rate") || n.Contains("timescale") || n == "_bumpdirection";
        }
    }

    // ==================================================================== default playlists

    /// <summary>
    /// The game re-adds its default playlists every time it loads yours. With the setting on that's skipped, and saved copies
    /// that are still exactly a default are removed. A default playlist you changed is kept, renamed with " (edited)". The
    /// active playlist falls back to "all maps" if it was removed, and the list is never left empty.
    /// </summary>
    internal static class Playlists
    {
        private static MethodInfo _addPresets;
        private static AccessTools.FieldRef<MapsManager, TextMeshProUGUI> _activeLabel;
        private static bool _gettingDefaults, _waitingForMenu;

        internal static void Install()
        {
            _addPresets = DorsthTweaksPlugin.Target(typeof(MapsManager), "AddPresetPlaylists");
            _activeLabel = AccessTools.FieldRefAccess<MapsManager, TextMeshProUGUI>("activePlaylistText");
            Harmony h = DorsthTweaksPlugin.Harmony;
            h.Patch(_addPresets, DorsthTweaksPlugin.Patch(typeof(Playlists), "AddPresetsPrefix"));
            h.Patch(DorsthTweaksPlugin.Target(typeof(MapsManager), "LoadState"), null, DorsthTweaksPlugin.Patch(typeof(Playlists), "LoadStatePostfix"));
            DorsthTweaksPlugin.HideDefaultPlaylists.SettingChanged += (sender, args) => SettingChanged();
        }

        private static bool AddPresetsPrefix(MapsManager __instance)
        {
            if (_gettingDefaults || !DorsthTweaksPlugin.HideDefaultPlaylists.Value) return true;
            try { KeepOne(__instance); }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
            return false;
        }

        private static void LoadStatePostfix(MapsManager __instance)
        {
            try
            {
                if (DorsthTweaksPlugin.HideDefaultPlaylists.Value) Clean(__instance, false);
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        /// <summary>Applied right away in the menus; during a match it waits until you're back, so the map rotation doesn't change mid-match.</summary>
        private static void SettingChanged()
        {
            try
            {
                if (MapsManager.Instance == null) return;
                if (PauseManager.Instance != null && !PauseManager.Instance.inMainMenu)
                {
                    if (_waitingForMenu) return;
                    _waitingForMenu = true;
                    Scenes.Loaded += BackInMenu;
                    return;
                }
                Apply(MapsManager.Instance);
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static void BackInMenu(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != Scenes.MainMenu) return;
            Scenes.Loaded -= BackInMenu;
            _waitingForMenu = false;
            try
            {
                if (MapsManager.Instance != null) Apply(MapsManager.Instance);
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static void Apply(MapsManager maps)
        {
            if (DorsthTweaksPlugin.HideDefaultPlaylists.Value)
            {
                Clean(maps, true);
                return;
            }
            _addPresets.Invoke(maps, null); // the game's own way: adds the defaults you don't have
            maps.UpdatePlaylists(false);
        }

        private static void KeepOne(MapsManager maps)
        {
            if (maps.Playlists.Count == 0) maps.Playlists.Add(new Playlist("Playlist n°1", new string[0]));
        }

        /// <summary>The game's default playlists, made by its own AddPresetPlaylists into an empty list.</summary>
        private static Dictionary<string, string[]> GameDefaults(MapsManager maps)
        {
            List<Playlist> yours = maps.Playlists;
            var defaults = new List<Playlist>();
            maps.Playlists = defaults;
            _gettingDefaults = true;
            try { _addPresets.Invoke(maps, null); }
            finally
            {
                _gettingDefaults = false;
                maps.Playlists = yours;
            }
            var byName = new Dictionary<string, string[]>();
            foreach (Playlist p in defaults)
                if (p != null && p.Name != null && !byName.ContainsKey(p.Name)) byName[p.Name] = p.Maps;
            return byName;
        }

        private static void Clean(MapsManager maps, bool live)
        {
            List<Playlist> list = maps.Playlists;
            Dictionary<string, string[]> defaults = GameDefaults(maps);
            int selected = maps.selectedPlaylistIndex, active = maps.activePlaylistIndex;
            bool selectedChanged = selected < 0 || selected >= list.Count;
            var kept = new List<Playlist>(list.Count);
            int newActive = -1, newSelected = 0;
            bool changed = false;
            for (int i = 0; i < list.Count; i++)
            {
                Playlist p = list[i];
                string[] gameMaps;
                if (p != null && p.Name != null && defaults.TryGetValue(p.Name, out gameMaps))
                {
                    changed = true;
                    if (i == selected) selectedChanged = true;
                    if (SameMaps(p.Maps, gameMaps)) continue; // still the game's default: removed
                    p.Name = FreeName(list, p.Name + " (edited)");
                }
                if (i == active) newActive = kept.Count;
                if (i == selected) newSelected = kept.Count;
                kept.Add(p);
            }
            if (!changed) return;

            list.Clear();
            list.AddRange(kept);
            KeepOne(maps);
            maps.selectedPlaylistIndex = newSelected;
            maps.activePlaylistIndex = newActive;

            // only the active playlist's label: its maps didn't change (same playlist, or "all maps")
            TextMeshProUGUI label = _activeLabel(maps);
            if (label != null) label.text = newActive < 0 ? "all maps" : list[newActive].Name;

            if (live) maps.UpdatePlaylists(false); // redraws and saves (on load the game does both itself)
            if (selectedChanged)
            {
                // the map editor shows the selected playlist: show the one now selected, so map clicks go to the right one
                maps.PopulateMapsFromPlayList(newSelected);
                maps.currentPlaylistText.text = list[newSelected].Name;
            }
        }

        private static bool SameMaps(string[] a, string[] b)
        {
            return new HashSet<string>(a ?? new string[0]).SetEquals(b ?? new string[0]);
        }

        private static string FreeName(List<Playlist> list, string wanted)
        {
            string name = wanted;
            for (int n = 2; list.Exists(p => p != null && p.Name == name); n++) name = wanted + " " + n;
            return name;
        }
    }

    // ==================================================================== key prompt

    /// <summary>
    /// Hide Key Prompt: the game writes "name [f]" into the prompt each frame you look at something. Right after that
    /// the " [f]" is cut off (the cut text is kept, so it's only made again when the prompt itself changes).
    /// </summary>
    internal static class KeyPrompt
    {
        internal static void Install()
        {
            HarmonyMethod post = DorsthTweaksPlugin.Patch(typeof(KeyPrompt), "Postfix");
            DorsthTweaksPlugin.Harmony.Patch(DorsthTweaksPlugin.Target(typeof(PlayerPickup), "HandleInteractEnvironment"), null, post);
            DorsthTweaksPlugin.Harmony.Patch(DorsthTweaksPlugin.Target(typeof(PlayerPickup), "HandleInteractionCheck"), null, post);
        }

        private static string _from, _to;

        private static void Postfix()
        {
            if (!DorsthTweaksPlugin.HideKeyPrompt.Value) return;
            try
            {
                PauseManager pm = PauseManager.Instance;
                if (pm == null) return;
                Cut(pm.grabPopup);
                Cut(pm.interactPopup);
            }
            catch (Exception e) { DorsthTweaksPlugin.LogErrorOnce(e); }
        }

        private static void Cut(TMP_Text t)
        {
            if (t == null || !t.gameObject.activeSelf) return;
            string s = t.text;
            if (string.IsNullOrEmpty(s) || s[s.Length - 1] != ']') return;
            if (!ReferenceEquals(s, _from) && s != _from)
            {
                int at = s.LastIndexOf(" [", StringComparison.Ordinal);
                if (at < 0) return;
                _from = s;
                _to = s.Substring(0, at);
            }
            t.text = _to;
        }
    }

    // ==================================================================== slide cooldown timer

    // Based on MovementPro by Glizzmn: its slide cooldown text and bar.
    /// <summary>
    /// Slide Cooldown Timer: after a slide the game waits slideResetTime (1.5 s) before the next one. The time left shows
    /// under the crosshair, as text ("0.4s") or a bar that empties, in the Minimal HUD Color. Nothing runs while it's off;
    /// on, one small check a frame, and it only draws while there's time left (the strings are made once).
    /// </summary>
    internal static class SlideClock
    {
        private static readonly AccessTools.FieldRef<FirstPersonController, float> LeftOf =
            AccessTools.FieldRefAccess<FirstPersonController, float>("slideResetTimer");
        private static readonly AccessTools.FieldRef<FirstPersonController, float> FullOf =
            AccessTools.FieldRefAccess<FirstPersonController, float>("slideResetTime");
        // every player body made (the game's Awake hook below), so the local one is found without searching the scene
        private static readonly List<FirstPersonController> Players = new List<FirstPersonController>();

        internal static void Install()
        {
            DorsthTweaksPlugin.SlideTimer.SettingChanged += (s, e) => Apply();
            DorsthTweaksPlugin.MinimalHudColor.Changed += Apply;
            DorsthTweaksPlugin.Harmony.Patch(DorsthTweaksPlugin.Target(typeof(FirstPersonController), "Awake___UserLogic"),
                postfix: DorsthTweaksPlugin.Patch(typeof(SlideClock), nameof(PlayerPostfix)));
            Host.Recreated += Apply;
            Apply();
        }

        private static void PlayerPostfix(FirstPersonController __instance)
        {
            Players.RemoveAll(p => p == null);
            if (__instance != null) Players.Add(__instance);
        }

        private static void Apply()
        {
            Clock c = Host.Add<Clock>();
            Painter p = Host.Add<Painter>();
            p.useGUILayout = false;
            c.enabled = DorsthTweaksPlugin.SlideTimer.Value != DorsthTweaksPlugin.SlideTimerStyle.Off;
            // the clock turns it on while there's time left
            p.enabled = false;
            c.Restyle();
        }

        /// <summary>
        /// Reads the time left once a frame and turns the painter on only while there is some: an enabled OnGUI is called
        /// several times a frame by Unity even when it draws nothing.
        /// </summary>
        internal sealed class Clock : MonoBehaviour
        {
            private FirstPersonController _me;
            private float _look, _left, _full = 1.5f;
            private Color _fill = Color.green;

            internal void Restyle()
            {
                _fill = DorsthTweaksPlugin.MinimalHudColor.IsDefault ? Color.green : DorsthTweaksPlugin.MinimalHudColor.Color;
            }

            private void OnDisable() { _left = 0f; Show(false); }

            private void Show(bool on)
            {
                Painter p = Host.Add<Painter>();
                if (p.enabled != on) p.enabled = on;
            }

            private void Update()
            {
                // the local player: looked for again only now and then while there isn't one
                if (_me == null || !_me.IsOwner)
                {
                    _me = null; _left = 0f;
                    Show(false);
                    if (Time.unscaledTime < _look) return;
                    _look = Time.unscaledTime + 0.5f;
                    foreach (FirstPersonController p in Players)
                        if (p != null && p.IsOwner) { _me = p; break; }
                    if (_me == null) return;
                }
                _left = _me.isActiveAndEnabled ? LeftOf(_me) : 0f;
                _full = Mathf.Max(0.01f, FullOf(_me));
                Show(_left > 0f);
                Painter.Left = _left;
                Painter.Full = _full;
                Painter.Fill = _fill;
            }
        }

        /// <summary>
        /// Drawn like Movement Pro's slide cooldown (glizzmn), so the two look the same: Unity's own GUI box and font, a
        /// 200x20 bar at the bottom middle of the screen, 150 px up, dark behind and green for the time left, with the
        /// time ("1.2s") just above its left end. Text alone is that same label, centred where the bar would be.
        /// Only enabled while there's time left.
        /// </summary>
        internal sealed class Painter : MonoBehaviour
        {
            // "0.0s" to "3.0s", made once
            private static readonly string[] Tenths = Enumerable.Range(0, 31).Select(i => (i / 10f).ToString("0.0", CultureInfo.InvariantCulture) + "s").ToArray();
            private const float W = 200f, H = 20f;
            private static readonly Color Back = new Color(0f, 0f, 0f, 0.6f);

            internal static float Left, Full = 1.5f;
            internal static Color Fill = Color.green;
            private GUIStyle _centred;

            private void OnGUI()
            {
                float left = Left, full = Full;
                if (left <= 0f || Event.current.type != EventType.Repaint) return;
                // under the crosshair, centred (sizes as at 1080p, scaled with the screen)
                float k = Screen.height / 1080f, w = W * k, h = H * k, x = (Screen.width - w) / 2f, y = Screen.height / 2f + 56f * k;
                string text = Tenths[Mathf.Clamp(Mathf.CeilToInt(left * 10f - 0.001f), 0, Tenths.Length - 1)];
                if (_centred == null) _centred = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
                _centred.fontSize = Mathf.RoundToInt(13f * Mathf.Max(1f, k));
                Color was = GUI.color;
                if (DorsthTweaksPlugin.SlideTimer.Value == DorsthTweaksPlugin.SlideTimerStyle.Bar)
                {
                    // well below the crosshair; it empties toward its left end, the time above that end (Movement Pro's)
                    float by = y + 112f * k;
                    GUI.color = Back;
                    GUI.Box(new Rect(x, by, w, h), GUIContent.none);
                    GUI.color = Fill;
                    float fw = w * Mathf.Clamp01(left / full), fx = x;
                    // the fill is the full bar's box cut off at the time left, so it keeps the same look to the very
                    // end and never squeezes or spills (a narrow box would warp its rounded ends)
                    if (fw >= 0.5f)
                    {
                        GUI.BeginGroup(new Rect(fx, by, fw, h));
                        GUI.Box(new Rect(0f, 0f, w, h), GUIContent.none);
                        GUI.EndGroup();
                    }
                    GUI.color = Color.white;
                    GUI.Label(new Rect(x, by - h, w, h), text);
                }
                else
                {
                    // text alone takes the Minimal HUD Color
                    GUI.color = DorsthTweaksPlugin.MinimalHudColor.IsDefault ? Color.white : Fill;
                    GUI.Label(new Rect(x, y, w, h), text, _centred);
                }
                GUI.color = was;
            }
        }
    }
}
