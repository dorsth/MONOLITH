// TrueSens - a STRAFTAT mod (BepInEx 5). Dorsth was here.
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ComputerysModdingUtilities;
using HarmonyLib;
using UnityEngine;
using System.Reflection;

[assembly: AssemblyTitle("TrueSens")]
[assembly: AssemblyDescription("Set your sensitivity in cm/360 from your mouse DPI, with every zoom scaled from it.")]
[assembly: AssemblyVersion("1.2.5.0")]
// only changes your own aim on your own screen: fine to play with people who don't have it
[assembly: StraftatMod(isVanillaCompatible: true)]

namespace TrueSens
{
    // Set your mouse DPI and the cm/360 you want: your sensitivity is set to match, and every zoom (aiming down sights,
    // scopes, each gun's own zoom) is scaled from it, then times the ADS or scoped multiplier.
    //
    // The game turns 0.1 x sensitivity degrees per mouse count, so a full turn is 3600 / sens counts, and
    //   cm/360 = 3600 / sens / dpi x 2.54   ->   sens = 9144 / (cm/360 x dpi)
    // Zoomed, that sensitivity is multiplied by tan(zoom fov / 2) / tan(normal fov / 2): 0% monitor distance (focal
    // length), so small moves around the crosshair line up exactly at any zoom.
    //
    // The game's own three sensitivity sliders become TrueSens' settings while it's on (SettingsMenu.cs).
    //
    // STRAFTH reads the Guid and the "Sensitivity" settings "Enabled" and "cm/360" to post cm/360 with runs:
    // keep those names and types.
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dorsth.straftat.truesens", Name = "TrueSens", Version = "1.2.5";
        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> On;
        internal static ConfigEntry<int> Dpi;
        internal static ConfigEntry<float> Cm360;
        internal static ConfigEntry<float> AdsMult, ScopedMult;

        internal const float MinCm = 1f, MaxCm = 150f, MinMult = 0.1f, MaxMult = 3f;

        private void Awake()
        {
            Log = Logger;
            On = Config.Bind("Sensitivity", "Enabled", true, "Set your sensitivity from your DPI and cm/360. Off: the game's own sensitivity settings.");
            Dpi = Config.Bind("Sensitivity", "Mouse DPI", 800, new ConfigDescription("The DPI your mouse is set to.", new AcceptableValueRange<int>(100, 32000)));
            Cm360 = Config.Bind("Sensitivity", "cm/360", 30f, new ConfigDescription("How far you move your mouse, in cm, for a full turn.", new AcceptableValueRange<float>(MinCm, MaxCm)));
            AdsMult = Config.Bind("Sensitivity", "ADS Multiplier", 1f, new ConfigDescription("Sensitivity while aiming down sights, times the zoom scaling. 1 matches your normal aim.", new AcceptableValueRange<float>(MinMult, MaxMult)));
            ScopedMult = Config.Bind("Sensitivity", "Scoped Multiplier", 1f, new ConfigDescription("Sensitivity while looking through a scope, times the zoom scaling. 1 matches your normal aim.", new AcceptableValueRange<float>(MinMult, MaxMult)));
            On.SettingChanged += (s, e) => { if (!On.Value) Restore(); SettingsMenu.RefreshAll(); Report(); };
            Dpi.SettingChanged += (s, e) => { SettingsMenu.RefreshAll(); Report(); };
            Cm360.SettingChanged += (s, e) => { SettingsMenu.RefreshAll(); Report(); };
            AdsMult.SettingChanged += (s, e) => SettingsMenu.RefreshAll();
            ScopedMult.SettingChanged += (s, e) => SettingsMenu.RefreshAll();
            var harmony = new Harmony(Guid);
            harmony.Patch(AccessTools.Method(typeof(FirstPersonController), "HandleMouseLook"), new HarmonyMethod(typeof(Plugin), nameof(LookPrefix)));
            // the free camera after you die turns at the game's slider sensitivity; ours while it runs
            MethodInfo killCam = AccessTools.Method(typeof(KillCam), "Update");
            if (killCam != null) harmony.Patch(killCam, prefix: new HarmonyMethod(typeof(Plugin), nameof(KillCamPrefix)), finalizer: new HarmonyMethod(typeof(Plugin), nameof(KillCamPostfix)));
            try { SettingsMenu.Install(harmony); }
            catch (Exception e) { Log.LogWarning("The game's sensitivity sliders stay as they are: " + e.Message); }
            Logger.LogInfo(Name + " " + Version + " loaded. Dorsth was here.");
            Report();
        }

        internal static float HipSens() { return 9144f / (Mathf.Max(MinCm, Cm360.Value) * Mathf.Max(100, Dpi.Value)); }

        private static void Report()
        {
            if (On.Value) Log.LogInfo("Sensitivity " + HipSens().ToString("0.####") + " for " + Dpi.Value + " dpi at " + Cm360.Value.ToString("0.##") + " cm/360");
        }

        // 0% monitor distance: the ratio of the focal lengths. The screen shape cancels out, so Unity's vertical fov works
        // as it is.
        internal static float ZoomFactor(float zoomFov, float hipFov)
        {
            if (zoomFov <= 0f || hipFov <= 0f || zoomFov >= hipFov) return 1f;
            return Mathf.Tan(zoomFov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(hipFov * 0.5f * Mathf.Deg2Rad);
        }

        // Right before the game turns the camera: its four sensitivities from ours. Only your own player, only a mouse.
        private static void LookPrefix(FirstPersonController __instance)
        {
            if (!On.Value || !__instance.IsOwner) return;
            if (__instance.pauseManager != null && __instance.pauseManager.gamepad) return;
            float sens = HipSens();
            __instance.lookSpeedX = sens;
            __instance.lookSpeedY = sens;
            float zoomed = sens * ZoomFactor(__instance.zoomFOV, __instance.defaultFOV);
            __instance.lookSpeedAimNoScope = zoomed * AdsMult.Value;
            __instance.lookSpeedAim = zoomed * ScopedMult.Value;
        }

        // The death camera (KillCam.Update) reads the game's sensitivity from Settings each frame. For that one call the
        // three values are ours, then the game's again, so nothing of ours is ever saved into the game's settings.
        private static bool _swapped;
        private static float _sens, _sensX, _sensY;

        private static void KillCamPrefix()
        {
            if (!On.Value) return;
            var s = Settings.Instance;
            if (s == null || (PauseManager.Instance != null && PauseManager.Instance.gamepad)) return;
            _sens = s.mouseSensitivity; _sensX = s.horizontalSensitivity; _sensY = s.verticalSensitivity;
            float sens = HipSens();
            s.mouseSensitivity = s.horizontalSensitivity = s.verticalSensitivity = sens;
            _swapped = true;
        }

        // a finalizer: runs even if the game's code throws, so the game's values always come back
        private static void KillCamPostfix()
        {
            if (!_swapped) return;
            _swapped = false;
            var s = Settings.Instance;
            if (s == null) return;
            s.mouseSensitivity = _sens; s.horizontalSensitivity = _sensX; s.verticalSensitivity = _sensY;
        }

        // turned off: back to the game's own sensitivity settings
        private static void Restore()
        {
            try
            {
                var s = Settings.Instance;
                if (s == null) return;
                foreach (var p in UnityEngine.Object.FindObjectsOfType<FirstPersonController>())
                {
                    if (p == null || !p.IsOwner) continue;
                    p.lookSpeedX = s.toggleTwoAxis ? s.horizontalSensitivity : s.mouseSensitivity;
                    p.lookSpeedY = s.toggleTwoAxis ? s.verticalSensitivity : s.mouseSensitivity;
                    p.lookSpeedAim = s.mouseAimScopeSensitivity;
                    p.lookSpeedAimNoScope = s.mouseAimSensitivity;
                }
            }
            catch (Exception e) { Log.LogWarning("Could not restore the game's sensitivity: " + e.Message); }
        }
    }
}
