// MONOLITH - every Dorsth mod for STRAFTAT in one file. Dorsth was here.
//
// Each mod in here is the same source as its own release, built together into one plugin file. BepInEx finds every
// one of them in it and loads them as before: same names, same settings files. Their settings are changed in the
// bundle's own menu (Menu.cs, F2); Mod Menu, if installed, shows the bundle once with the key that opens it.
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
        public const string Guid = "dorsth.straftat.monolith", Name = "MONOLITH", Version = "2.0.6";
        // filled in by build.sh: the mods and versions this file was built from
        internal const string Contents = "@CONTENTS@";

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
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(ModMenuGuid))
            {
                try { ModMenuPage(); }
                catch (System.Exception e) { Logger.LogWarning("Mod Menu page failed: " + e.Message); }
            }
        }

        // Mod Menu: everything is set in the bundle's menu, so Mod Menu shows the bundle once, with the menu key and the
        // language, and not the mods in it. Only touched when Mod Menu is installed.
        // Mod Menu makes its list (and reads which settings are hidden) when the game's settings window is made, which is
        // BEFORE any plugin's Start: hiding them in Start is too late, and every mod and every setting shows.
        // So two hooks on the routine that makes the list, put in from Awake: before it, the settings are hidden (every
        // mod has loaded by then, so all their settings exist); after it, the mods in this file are taken out of the list.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void ModMenuPage()
        {
            if (HarmonyLib.AccessTools.TypeByName("ModMenu.Pages") != null) return;   // MONOLITH's own stand-in: no window of its own
            System.Type manager = HarmonyLib.AccessTools.TypeByName("ModMenu.ModMenuManager");
            MethodInfo init = manager != null ? HarmonyLib.AccessTools.Method(manager, "Init") : null;
            _mods = manager != null ? HarmonyLib.AccessTools.Field(manager, "mods") : null;
            if (init == null || _mods == null) { Logger.LogInfo("Mod Menu lists the mods in the bundle one by one: its list could not be reached."); return; }
            new HarmonyLib.Harmony(Guid + ".modmenu").Patch(init,
                prefix: new HarmonyLib.HarmonyMethod(typeof(BundlePlugin), nameof(ModListPrefix)),
                postfix: new HarmonyLib.HarmonyMethod(typeof(BundlePlugin), nameof(ModListPostfix)));
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ModListPrefix()
        {
            try
            {
                var hide = new System.Collections.Generic.List<BepInEx.Configuration.ConfigEntryBase>();
                Assembly mine = typeof(BundlePlugin).Assembly;
                foreach (var kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
                {
                    BaseUnityPlugin p = kv.Value.Instance;
                    if (p == null || p.GetType().Assembly != mine) continue;
                    foreach (var e in p.Config) if (e.Value != BundleMenu.Key && e.Value != Lang.Pick) hide.Add(e.Value);
                }
                ModMenu.Api.ModMenuCustomisation.HideEntries(hide);
            }
            catch (System.Exception e) { if (Log != null) Log.LogInfo("Mod Menu shows the bundle's settings: " + e.Message); }
        }

        private static FieldInfo _mods;

        private static void ModListPostfix()
        {
            try
            {
                System.Array all = _mods.GetValue(null) as System.Array;
                if (all == null || all.Length == 0) return;
                System.Type modType = all.GetType().GetElementType();
                FieldInfo info = HarmonyLib.AccessTools.Field(modType, "info");
                FieldInfo guid = HarmonyLib.AccessTools.Field(info.FieldType, "guid");
                Assembly mine = typeof(BundlePlugin).Assembly;
                var keep = new System.Collections.Generic.List<object>();
                foreach (object m in all)
                {
                    string g = (string)guid.GetValue(info.GetValue(m));
                    BepInEx.PluginInfo pi;
                    bool inBundle = g != Guid && BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(g, out pi) && pi.Instance != null && pi.Instance.GetType().Assembly == mine;
                    if (!inBundle) keep.Add(m);
                }
                if (keep.Count == all.Length) return;
                System.Array kept = System.Array.CreateInstance(modType, keep.Count);
                for (int i = 0; i < keep.Count; i++) kept.SetValue(keep[i], i);
                _mods.SetValue(null, kept);
            }
            catch (System.Exception e) { if (Log != null) Log.LogInfo("Mod Menu's list left as it was: " + e.Message); }
        }

        private void Start()
        {
            // settings that one switch of the menu stands for: not in the search, not in a profile
            foreach (var e in MenuLayout.Inert()) if (e != null) BundleMenu.Hidden.Add(e);
            try { Updates.Start(this); } catch (System.Exception e) { Logger.LogInfo("Updates: " + e.Message); }
        }
    }
}
