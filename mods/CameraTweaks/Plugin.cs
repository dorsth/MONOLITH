// CameraTweaks - a STRAFTAT mod (BepInEx 5). Dorsth was here.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ComputerysModdingUtilities;
using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using HarmonyLib;
using UnityEngine;

[assembly: AssemblyTitle("CameraTweaks")]
[assembly: AssemblyDescription("Takes out the camera movement STRAFTAT adds on its own, one switch each.")]
[assembly: AssemblyVersion("1.2.1.0")]
// only changes your own camera on your own screen: fine to play with people who don't have it
[assembly: StraftatMod(isVanillaCompatible: true)]

namespace CameraTweaks
{
    // Competitive camera tweaks: the camera movements the game adds on top of your aim, each one with its own toggle.
    // Six toggles: NoTilt, NoFallShake, NoJumpShake, NoVaultShake, NoSpeedFov and BetterRecoil. On takes that movement out,
    // off leaves the game as it is. ViewmodelTweaks leaves the camera's FOV to this mod.
    [BepInDependency("kestrel.straftat.modmenu", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dorsth.straftat.cameratweaks", Name = "CameraTweaks", Version = "1.2.1";
        internal static ManualLogSource Log;

        // NoTilt: all of SideTilt's roll (tiltAmount, slideTiltAmount, wallSlideLean, rotationAmount)
        internal static ConfigEntry<bool> NoTilt;
        // NoFallShake: fallShake + highFallShake. NoJumpShake: jumpSlope, wall jump, ceiling hit. NoVaultShake: vault.
        internal static ConfigEntry<bool> NoFallShake, NoJumpShake, NoVaultShake;
        internal static ConfigEntry<bool> BetterRecoil;
        internal static ConfigEntry<bool> NoSpeedFov;
        internal static ConfigEntry<float> SpeedFovAmount;

#if !DORSTH_BUNDLE
        // Mod Menu (if installed) shows this next to the mod. Its own method, so nothing of Mod Menu is needed without it.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ModMenuDescription() { ModMenu.Api.ModMenuCustomisation.SetPluginDescription("Camera without roll, landing and jump shake, or sprint FOV, and recoil that kicks your aim instead of the camera."); }
#endif

        private void Awake()
        {
            Log = Logger;
#if !DORSTH_BUNDLE
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("kestrel.straftat.modmenu"))
            { try { ModMenuDescription(); } catch (Exception e) { Logger.LogInfo("Mod Menu description: " + e.Message); } }
#endif
            // The recoil keeps the last player (and everything it points to) until the next shot; a heap walk found
            // ~300 destroyed objects of an earlier take held that way. Let go of it when a scene loads.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => Recoil.Forget();
            const string roll = "Camera", shake = roll, recoil = roll;
            NoTilt = Config.Bind(roll, "NoTilt", true, "On: the camera stops rolling to the side when you strafe, slide, slide along a wall or move your mouse sideways.");
            NoFallShake = Config.Bind(shake, "NoFallShake", true, "On: no camera shake or dip when you land.");
            NoJumpShake = Config.Bind(shake, "NoJumpShake", true, "On: no camera shake when you jump out of a slide, wall jump or hit a ceiling.");
            NoVaultShake = Config.Bind(shake, "NoVaultShake", false, "On: no camera shake when you vault over something.");
            NoSpeedFov = Config.Bind(roll, "NoSpeedFov", true, "On: the FOV stays what you set in the game when you sprint and slide. Off: it widens like in the game.");
            NoSpeedFov.SettingChanged += (o, e) => SpeedFov.ApplyAll();
            SpeedFovAmount = Config.Bind(roll, "SpeedFovAmount", 1f, new ConfigDescription("How much the FOV widens when you sprint and slide, with NoSpeedFov off. 1 is the game's.", new AcceptableValueRange<float>(0.1f, 2f)));
            SpeedFovAmount.SettingChanged += (o, e) => SpeedFov.ApplyAll();
            BetterRecoil = Config.Bind(recoil, "BetterRecoil", true, "On: the AR, AK and warden kick your aim instead of the camera. When you let go it comes back down by the kick you didn't pull down yourself, so taps and bursts come back to where you aimed and a pulled down spray doesn't swing below it.");

            var h = new Harmony(Guid);
            Shakes.Patch(h);
            try { h.Patch(AccessTools.Method(typeof(FirstPersonController), "Start"), postfix: new HarmonyMethod(typeof(SpeedFov), nameof(SpeedFov.Started))); }
            catch (Exception e) { Log.LogWarning("The sprint and slide FOV could not be switched off (game update?): " + e.Message); }
            h.Patch(AccessTools.Method(typeof(FirstPersonController), "HandleCameraController"), new HarmonyMethod(typeof(Roll), nameof(Roll.Prefix)));
            foreach (var n in new[] { "CameraRevolverAnimation", "AltCameraRevolverAnimation" })
            {
                var m = AccessTools.Method(typeof(Weapon), n);
                // after other mods' prefixes: if one already took care of this shot, leave it
                if (m != null) h.Patch(m, new HarmonyMethod(typeof(Recoil), nameof(Recoil.Prefix)) { priority = Priority.Last });
            }
            Logger.LogInfo(Name + " " + Version + " loaded. Dorsth was here.");
            // BepInEx's manager object (every plugin's home) is a plain object the game destroys on the way into a lobby, unless something hides it (Mod Menu does): hidden here too, so this works without Mod Menu.
            gameObject.hideFlags = HideFlags.HideAndDontSave;
        }

        private void Update() { Recoil.Tick(); }
    }

    // How much the FOV widens when you sprint and slide: three numbers the game works out once, when a player starts
    // (distToRunFov, distToSlideFov, distToRunSlideFov), and adds to your FOV every frame. With NoSpeedFov they are set to 0,
    // so the FOV stays what you set in the game; without it they are the game's times SpeedFovAmount. The game's own numbers are kept from the first player, to put back.
    internal static class SpeedFov
    {
        private static AccessTools.FieldRef<FirstPersonController, float> Run, Slide, RunSlide;
        private static bool failed, known;
        private static float run, slide, runSlide;

        // the switch was flipped: every player there is now, at once (players that start later get it in Started)
        internal static void ApplyAll()
        {
            if (!known) return;
            foreach (var p in UnityEngine.Object.FindObjectsOfType<FirstPersonController>()) Set(p);
        }

        private static void Set(FirstPersonController p)
        {
            float k = Plugin.NoSpeedFov.Value ? 0f : Mathf.Clamp(Plugin.SpeedFovAmount.Value, 0.1f, 2f);
            Run(p) = run * k; Slide(p) = slide * k; RunSlide(p) = runSlide * k;
        }

        internal static void Started(FirstPersonController __instance)
        {
            if (failed || __instance == null) return;
            try
            {
                if (Run == null)
                {
                    Run = AccessTools.FieldRefAccess<FirstPersonController, float>("distToRunFov");
                    Slide = AccessTools.FieldRefAccess<FirstPersonController, float>("distToSlideFov");
                    RunSlide = AccessTools.FieldRefAccess<FirstPersonController, float>("distToRunSlideFov");
                }
                if (!known) { known = true; run = Run(__instance); slide = Slide(__instance); runSlide = RunSlide(__instance); }
                Set(__instance);
            }
            catch (Exception e) { failed = true; Plugin.Log.LogWarning("The sprint and slide FOV could not be switched off (game update?): " + e.Message); }
        }
    }

    // The four parts of the camera roll the game adds up in HandleCameraController and SideTilt: set to nothing when off.
    internal static class Roll
    {
        private static readonly AccessTools.FieldRef<FirstPersonController, float> Tilt = AccessTools.FieldRefAccess<FirstPersonController, float>("tiltAmount");
        private static readonly AccessTools.FieldRef<FirstPersonController, float> SlideTilt = AccessTools.FieldRefAccess<FirstPersonController, float>("slideTiltAmount");
        private static readonly AccessTools.FieldRef<FirstPersonController, float> Mouse = AccessTools.FieldRefAccess<FirstPersonController, float>("rotationAmount");
        // the game's own values, from the first player seen
        private static bool known;
        private static float tilt, slideTilt, mouse;

        public static void Prefix(FirstPersonController __instance)
        {
            if (!__instance.IsOwner) return;
            if (!known) { known = true; tilt = Tilt(__instance); slideTilt = SlideTilt(__instance); mouse = Mouse(__instance); }
            Tilt(__instance) = !Plugin.NoTilt.Value ? tilt : 0f;
            SlideTilt(__instance) = !Plugin.NoTilt.Value ? slideTilt : 0f;
            Mouse(__instance) = !Plugin.NoTilt.Value ? mouse : 0f;
            if (Plugin.NoTilt.Value) __instance.wallSlideLean = 0f;
        }
    }

    // Each camera shake in the movement code is a call to DOPunchRotation (and the hard landing a DOLocalRotate). Those
    // calls are swapped for ones that only shake when that setting is on, by where they are in the game's code.
    internal static class Shakes
    {
        private static readonly MethodInfo Punch = AccessTools.Method(typeof(ShortcutExtensions), "DOPunchRotation", new[] { typeof(Transform), typeof(Vector3), typeof(float), typeof(int), typeof(float) });
        private static readonly MethodInfo LocalRotate = AccessTools.Method(typeof(ShortcutExtensions), "DOLocalRotate", new[] { typeof(Transform), typeof(Vector3), typeof(float), typeof(RotateMode) });

        // which shake each call is, in order, per method of the game's FirstPersonController
        private static readonly Dictionary<string, string[]> Punches = new Dictionary<string, string[]>
        {
            { "OnControllerColliderHit", new[] { "CeilingHit", "CeilingHit" } },
            { "Jump", new[] { "WallJump", "JumpSlope", "JumpSlope", "JumpSlope" } },
            { "ApplyFinalMovements", new[] { "JumpSlope", "FallShake" } },
            { "CheckForVault", new[] { "Vault" } },
        };

        public static void Patch(Harmony h)
        {
            var t = new HarmonyMethod(typeof(Shakes), nameof(Transpiler));
            foreach (var name in Punches.Keys)
            {
                var m = AccessTools.Method(typeof(FirstPersonController), name);
                if (m == null) { Plugin.Log.LogWarning(name + " not found, its camera shake stays"); continue; }
                h.Patch(m, transpiler: t);
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var list = instructions.ToList();
            string where = __originalMethod.Name;
            var kinds = Punches[where];
            var punches = list.Where(c => c.Calls(Punch)).ToList();
            if (punches.Count != kinds.Length)
            {
                // the game changed: better no tweak than the wrong one
                Plugin.Log.LogWarning(where + ": " + punches.Count + " camera shakes where " + kinds.Length + " were expected, left as they are");
                return list;
            }
            for (int i = 0; i < punches.Count; i++)
            {
                punches[i].opcode = OpCodes.Call;
                punches[i].operand = AccessTools.Method(typeof(Shakes), kinds[i]);
            }
            if (where == "ApplyFinalMovements")
            {
                var dips = list.Where(c => c.Calls(LocalRotate)).ToList();
                if (dips.Count == 1) { dips[0].opcode = OpCodes.Call; dips[0].operand = AccessTools.Method(typeof(Shakes), nameof(HighFall)); }
                else Plugin.Log.LogWarning("ApplyFinalMovements: the high fall dip wasn't where it was expected, left as it is");
            }
            return list;
        }

        private static Tweener Gate(bool on, Transform t, Vector3 p, float d, int v, float e) { return on ? t.DOPunchRotation(p, d, v, e) : null; }

        public static Tweener CeilingHit(Transform t, Vector3 p, float d, int v, float e) { return Gate(!Plugin.NoJumpShake.Value, t, p, d, v, e); }
        public static Tweener WallJump(Transform t, Vector3 p, float d, int v, float e) { return Gate(!Plugin.NoJumpShake.Value, t, p, d, v, e); }
        public static Tweener JumpSlope(Transform t, Vector3 p, float d, int v, float e) { return Gate(!Plugin.NoJumpShake.Value, t, p, d, v, e); }
        public static Tweener FallShake(Transform t, Vector3 p, float d, int v, float e) { return Gate(!Plugin.NoFallShake.Value, t, p, d, v, e); }
        public static Tweener Vault(Transform t, Vector3 p, float d, int v, float e) { return Gate(!Plugin.NoVaultShake.Value, t, p, d, v, e); }

        public static TweenerCore<Quaternion, Vector3, QuaternionOptions> HighFall(Transform t, Vector3 end, float d, RotateMode mode)
        {
            return !Plugin.NoFallShake.Value ? t.DOLocalRotate(end, d, mode) : null;
        }
    }

    // akAnim guns (ar, ak, warden) turn the camera with every shot and, once you let go of fire, ease it back at the
    // game's camera speed (CameraShakeConstrains, 3 a second). With BetterRecoil the same kick goes into your aim,
    // spread over the shot like the game's own, and once you let go it comes back the same way, but only the part
    // you didn't pull down yourself: taps and bursts come back to where you aimed like in the game, and a spray you
    // held down doesn't swing below it (which is what the camera swing did).
    internal static class Recoil
    {
        private static readonly AccessTools.FieldRef<Weapon, Vector3> Kick = AccessTools.FieldRefAccess<Weapon, Vector3>("recoil");
        private static FirstPersonController player;
        private static Weapon weapon;
        // kick still to put into the aim, and kick put in that hasn't been pulled down or come back yet (degrees,
        // x is pitch where minus is up, y is yaw)
        private static Vector2 left, owed;
        private static float time = 0.1f;
        // where the view was after the last frame's changes, to tell what the mouse did since
        private static float lastPitch, lastYaw;
        private static bool haveLast;

        internal static void Forget() { player = null; weapon = null; left = owed = Vector2.zero; haveLast = false; }

        public static bool Prefix(Weapon __instance, MethodBase __originalMethod, bool __runOriginal)
        {
            if (!__runOriginal || !Plugin.BetterRecoil.Value || !__instance.akAnim || !Swings(__instance)) return true;
            var p = __instance.playerController;
            if (p == null || !p.IsOwner) return true;
            Vector3 r = Kick(__instance) * (__originalMethod.Name == "AltCameraRevolverAnimation" ? 3f : 1f);
            if (player != p) { player = p; left = owed = Vector2.zero; haveLast = false; }
            weapon = __instance;
            var kick = new Vector2(-r.x, -r.y);
            left += kick;
            owed += kick;
            time = Mathf.Max(0.01f, __instance.duration);
            return false;
        }

        // only the ar, ak and warden (other guns use the same camera kick). Their names in game are "ar", "ak" and
        // "warden"; the objects are called AR15, AK-K and Warden, so the object's name never matches.
        private static bool Swings(Weapon w)
        {
            var b = w.behaviour != null ? w.behaviour : w.GetComponent<ItemBehaviour>();
            string n = b != null && b.weaponName != null ? b.weaponName.Trim().ToLowerInvariant() : "";
            if (n == "ar" || n == "ak" || n == "warden") return true;
            string o = w.gameObject.name;
            return o.StartsWith("AR15", System.StringComparison.Ordinal) || o.StartsWith("AK-K", System.StringComparison.Ordinal)
                || o.StartsWith("Warden", System.StringComparison.Ordinal);
        }

        // a bit of the kick each frame, fast at first like the game's tween; once fire is let go, the kick that's
        // still owed comes back at the game's camera speed
        public static void Tick()
        {
            if (player == null) { left = owed = Vector2.zero; haveLast = false; return; }
            float pitch = player.rotationX, yaw = player.transform.eulerAngles.y;
            // what the mouse did since last frame: pulling against the kick takes that much off what comes back
            if (haveLast)
            {
                owed.x = Against(owed.x, pitch - lastPitch);
                owed.y = Against(owed.y, Mathf.DeltaAngle(lastYaw, yaw));
            }
            Vector2 step = Vector2.zero;
            if (left.sqrMagnitude > 1e-8f)
            {
                float k = 1f - Mathf.Exp(-Time.deltaTime * 3f / time);
                Vector2 s = left * k;
                left -= s;
                step += s;
            }
            else left = Vector2.zero;
            // the game holds the camera up while fire is held (rotateBack off) and eases it back once it's let go
            var shake = weapon != null ? weapon.camAnimScript : null;
            bool held = shake != null && !shake.rotateBack;
            if (!held && owed.sqrMagnitude > 1e-8f)
            {
                float speed = shake != null && shake.baseSpeed > 0f ? shake.baseSpeed : 3f;
                Vector2 back = owed * Mathf.Min(1f, speed * Time.deltaTime);
                owed -= back;
                step -= back;
            }
            else if (owed.sqrMagnitude <= 1e-8f) owed = Vector2.zero;
            if (step.sqrMagnitude > 1e-12f)
            {
                player.rotationX = Mathf.Clamp(player.rotationX + step.x, -90f, 90f);
                if (Mathf.Abs(step.y) > 1e-5f) player.transform.rotation *= Quaternion.Euler(0f, step.y, 0f);
            }
            lastPitch = player.rotationX;
            lastYaw = player.transform.eulerAngles.y;
            haveLast = true;
        }

        // owed kick less a mouse move against it (never past zero: pulling further doesn't make it come back the other way)
        private static float Against(float owed, float moved)
        {
            if (owed < 0f && moved > 0f) return Mathf.Min(0f, owed + moved);
            if (owed > 0f && moved < 0f) return Mathf.Max(0f, owed + moved);
            return owed;
        }
    }
}
