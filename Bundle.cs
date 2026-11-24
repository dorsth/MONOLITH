// MONOLITH - every Dorsth mod for STRAFTAT in one file. Dorsth was here.
//
// Each mod in here is the same source as its own release, built together into one plugin file. BepInEx finds every
// one of them in it and loads them as before: same names, same settings files. Their settings are changed in the
// bundle's own menu (Menu.cs, F2). Mod Menu itself can't be installed next to it: modmenu/ stands in for it, under
// its plugin id, so mods made for Mod Menu still load.
// A mod that is ALSO installed on its own is loaded once: BepInEx skips the second copy and says so in its log.
using System.Reflection;
using BepInEx;
using ComputerysModdingUtilities;

[assembly: AssemblyTitle("MONOLITH")]
[assembly: AssemblyDescription("A competitive mod bundle for STRAFTAT.")]
[assembly: AssemblyVersion("2.0.0.0")]
// only changes things on your own screen: fine to play with people who don't have it
[assembly: StraftatMod(isVanillaCompatible: true)]

namespace Monolith
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(ModMenuGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class BundlePlugin : BaseUnityPlugin
    {
        public const string Guid = "dorsth.straftat.monolith", Name = "MONOLITH", Version = "2.6.5";
        // filled in by build.sh: the mods and versions this file was built from
        internal const string Contents = "@CONTENTS@";

        // the stand-in for Mod Menu in modmenu/: loaded first, so the mods in here that talk to Mod Menu find it
        internal const string ModMenuGuid = "kestrel.straftat.modmenu";
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static BundlePlugin I;   // runs the update check, which waits on the web

        private void Awake()
        {
            Log = Logger; I = this;
            Logger.LogInfo(Name + " " + Version + " loaded. Dorsth was here.");
            Logger.LogInfo("It holds: " + Contents);
            // as the mods in here do: the object BepInEx keeps its plugins on is hidden, so the game does not destroy it
            gameObject.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
            BundleMenu.Key = Config.Bind("Keys", "Menu", UnityEngine.KeyCode.F2, "Opens the menu with the settings of every mod in the bundle.");
            Lang.Bind(Config);
            GameConfig.Bind(Config);
            try
            {
                // the menu: a script that is off until the key is pressed, and one that only watches the key
                gameObject.AddComponent<BundleMenu>();
                gameObject.AddComponent<MenuKeyWatch>();
                MenuHooks.Install(new HarmonyLib.Harmony(Guid));
            }
            catch (System.Exception e) { Logger.LogError("The menu could not be set up (settings can still be changed in the .cfg files): " + e); }
            try { Keys.Bind(Config); Keys.Install(new HarmonyLib.Harmony(Guid + ".keys")); }
            catch (System.Exception e) { Logger.LogWarning("Extra keys are off: " + e.Message); }
            try { Players.Bind(Config); Players.Install(new HarmonyLib.Harmony(Guid + ".players")); }
            catch (System.Exception e) { Logger.LogWarning("Forced outfits and hats are off: " + e.Message); }
            try { FrameCap.Bind(Config); }
            catch (System.Exception e) { Logger.LogWarning("Frame cap is off: " + e.Message); }
        }

        private void Start()
        {
            // settings that one switch of the menu stands for: not in the search, not in a profile
            foreach (var e in MenuLayout.Inert()) if (e != null) BundleMenu.Hidden.Add(e);
            try { Updates.Start(this); } catch (System.Exception e) { Logger.LogInfo("Updates: " + e.Message); }
        }
    }
}
