// MatchStats - a STRAFTAT mod (BepInEx 5). Dorsth was here.
//
// Kills, deaths and damage of every player, shown between rounds and on a board after the match.
// Client-side: counted from what the game already sends everyone, shown only to you, never while anyone can play.

using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using ComputerysModdingUtilities;

[assembly: AssemblyTitle("MatchStats")]
[assembly: AssemblyDescription("Kills, deaths and damage of every player, between rounds and after the match.")]
[assembly: AssemblyVersion("1.0.6.0")]

// Only changes what you see, so matchmaking with unmodded players still works.
[assembly: StraftatMod(isVanillaCompatible: true)]

namespace MatchStats
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class MatchStatsPlugin : BaseUnityPlugin
    {
        public const string Guid = "dorsth.straftat.matchstats";
        public const string Name = "MatchStats";
        public const string Version = "1.0.6";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
#if !DORSTH_BUNDLE
            // in MONOLITH only MONOLITH says it loaded, so the line shows once
            Log.LogInfo(Name + " " + Version + " loaded. Dorsth was here.");
#endif
            bool fresh = !File.Exists(Config.ConfigFilePath);
            try { Stats.Bind(Config, new HarmonyLib.Harmony(Guid)); }
            catch (Exception e) { Log.LogWarning("Match stats are off: " + e.Message); return; }
            // match stats used to be part of MONOLITH (up to 1.1.8): its on/off setting comes along once
            if (fresh) try { Carry(); } catch (Exception e) { Log.LogWarning("The old on/off setting could not be read: " + e.Message); }
        }

        private void Carry()
        {
            string old = Path.Combine(Paths.ConfigPath, "dorsth.straftat.monolith.cfg");
            if (!File.Exists(old)) return;
            bool inside = false;
            foreach (string raw in File.ReadAllLines(old))
            {
                string l = raw.Trim();
                if (l.StartsWith("[", StringComparison.Ordinal)) { inside = l == "[Match Stats]"; continue; }
                if (!inside || !l.StartsWith("Enabled", StringComparison.Ordinal)) continue;
                int eq = l.IndexOf('=');
                if (eq > 0 && l.Substring(eq + 1).Trim().Equals("false", StringComparison.OrdinalIgnoreCase)) Stats.On.Value = false;
                return;
            }
        }
    }
}
