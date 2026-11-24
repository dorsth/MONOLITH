// MatchStats - a STRAFTAT mod (BepInEx 5). Dorsth was here.
//
// Kills, deaths and damage of every player, shown between rounds and on a board after the match.
// Client-side: counted from what the game already sends everyone, shown only to you, never while anyone can play.

using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using ComputerysModdingUtilities;

[assembly: AssemblyTitle("MatchStats")]
[assembly: AssemblyDescription("Kills, deaths and damage of every player, between rounds and after the match.")]
[assembly: AssemblyVersion("1.0.7.0")]

// Only changes what you see, so matchmaking with unmodded players still works.
[assembly: StraftatMod(isVanillaCompatible: true)]

namespace MatchStats
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class MatchStatsPlugin : BaseUnityPlugin
    {
        public const string Guid = "dorsth.straftat.matchstats";
        public const string Name = "MatchStats";
        public const string Version = "1.0.7";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
#if !DORSTH_BUNDLE
            // in MONOLITH only MONOLITH says it loaded, so the line shows once
            Log.LogInfo(Name + " " + Version + " loaded. Dorsth was here.");
#endif
            try { Stats.Bind(Config, new HarmonyLib.Harmony(Guid)); }
            catch (Exception e) { Log.LogWarning("Match stats are off: " + e.Message); }
        }
    }
}
