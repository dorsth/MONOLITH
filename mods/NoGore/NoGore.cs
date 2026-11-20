// NoGore - a STRAFTAT mod (BepInEx 5). Dorsth was here.
//
// Client-side and visual only:
//   Hide Bodies  everything a death leaves lying around. Off, you pick:
//     Hide Ragdolls    dead bodies (yours too) and the pieces from an explosive death
//     Hide Dropped Hats   hats that come off players
//   No Blood     all blood. Off, you pick:
//     No Hit Blood     blood from hits
//     No Death Blood   the blood that comes out of a body when it dies
//     No Blood Stains  blood left on floors and walls
// Blood: while the game spawns an effect, its blood prefab is swapped for an empty one.
// No per-frame code: everything runs on a shot, a hit, an explosion or a death.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ComputerysModdingUtilities;
using HarmonyLib;
using TMPro;
using UnityEngine;

[assembly: AssemblyTitle("NoGore")]
[assembly: AssemblyDescription("STRAFTAT: no blood and no ragdolls, only on your screen.")]
[assembly: AssemblyVersion("1.3.4.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

// Only changes what you see on your own screen, so matchmaking with unmodded players still works.
[assembly: StraftatMod(isVanillaCompatible: true)]

namespace NoGore
{
    [BepInDependency("kestrel.straftat.modmenu", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(Guid, Name, Version)]
    public sealed class NoGorePlugin : BaseUnityPlugin
    {
        public const string Guid = "dorsth.straftat.nogore";
        public const string Name = "NoGore";
        public const string Version = "1.3.4";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> HideBodies, HideRagdolls, HideHats, NoBlood, NoHitBlood, NoDeathBlood, NoBloodStains;

        // a group switch that's on covers everything under it; off, each of its own switches decides
        internal static bool Ragdolls { get { return HideBodies.Value || HideRagdolls.Value; } }
        // a setting that was renamed keeps its value: BepInEx keeps the line under the old name until something takes it
        private static void FromOldName(ConfigFile c, ConfigDefinition old, ConfigEntry<bool> now)
        {
            try
            {
                var orphans = HarmonyLib.AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(c, null) as System.Collections.Generic.Dictionary<ConfigDefinition, string>;
                string v; bool b;
                if (orphans == null || !orphans.TryGetValue(old, out v)) return;
                if (bool.TryParse(v, out b)) now.Value = b;
                orphans.Remove(old); c.Save();
            }
            catch { }
        }

        internal static bool Hats { get { return HideBodies.Value || HideHats.Value; } }
        internal static bool HitBlood { get { return NoBlood.Value || NoHitBlood.Value; } }
        internal static bool DeathBlood { get { return NoBlood.Value || NoDeathBlood.Value; } }
        internal static bool Stains { get { return NoBlood.Value || NoBloodStains.Value; } }
        private static bool _errorLogged;

#if !DORSTH_BUNDLE
        // Mod Menu (if installed) shows this next to the mod. Its own method, so nothing of Mod Menu is needed without it.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ModMenuDescription() { ModMenu.Api.ModMenuCustomisation.SetPluginDescription("Hides blood, bodies, ragdolls and hats that come off players. Only on your screen."); }
#endif

        private void Awake()
        {
            Log = Logger;
            Log.LogMessage("Dorsth was here");
#if !DORSTH_BUNDLE
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("kestrel.straftat.modmenu"))
            { try { ModMenuDescription(); } catch (Exception e) { Logger.LogInfo("Mod Menu description: " + e.Message); } }
#endif

            const string Bodies = "Bodies", Blood = "Blood";
            HideBodies = Config.Bind(Bodies, "Hide Bodies", true, "No ragdolls, body pieces or dropped hats. Turn off to pick which.");
            HideRagdolls = Config.Bind(Bodies, "Hide Ragdolls", false, "Dead bodies (yours too) and the pieces from an explosive death.");
            HideHats = Config.Bind(Bodies, "Hide Dropped Hats", false, "Hats that come off players.");
            FromOldName(Config, new ConfigDefinition(Bodies, "Hide Hats"), HideHats);   // its name until 1.3.2
            NoBlood = Config.Bind(Blood, "No Blood", true, "No blood at all. Turn off to pick which.");
            NoHitBlood = Config.Bind(Blood, "No Hit Blood", false, "Blood from hits.");
            NoDeathBlood = Config.Bind(Blood, "No Death Blood", false, "The blood that comes out of a body when it dies. It shows without the body too.");
            NoBloodStains = Config.Bind(Blood, "No Blood Stains", false, "Blood left on floors and walls.");
            try { CarryOld(); }
            catch (Exception e) { Log.LogWarning("The old settings could not be carried over. (" + e.Message + ")"); }

            var harmony = new Harmony(Guid);
            Install("deaths", () => Deaths.Install(harmony));
            Install("blood", () => global::NoGore.Blood.Install(harmony));
            Install("mod menu links", () => MenuLinks.Install(harmony));
        }

        /// <summary>
        /// Settings of earlier versions: up to 1.1.1 there were two switches (NoRagdoll, NoBlood), 1.2.0 had blood in two
        /// (NoBloodParticles, NoBloodTextures), and a first 1.3.0 had one switch for hit and death blood together and one
        /// for the pieces of a body (now part of Hide Ragdolls). What they were set to carries over, and their lines leave
        /// the settings file.
        /// </summary>
        private void CarryOld()
        {
            var left = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(Config, null) as Dictionary<ConfigDefinition, string>;
            if (left == null || left.Count == 0) return;
            Func<string, bool?> old = section =>
            {
                string v;
                if (!left.TryGetValue(new ConfigDefinition(section, "Enabled"), out v) || v == null) return null;
                return string.Equals(v.Trim(), "true", StringComparison.OrdinalIgnoreCase);
            };
            bool? ragdoll = old("NoRagdoll"), blood = old("NoBlood"), spray = old("NoBloodParticles"), stains = old("NoBloodTextures");
            if (ragdoll == false) HideBodies.Value = false;
            if (spray != null || stains != null)
            {
                bool sp = spray ?? true, st = stains ?? true;
                NoBlood.Value = sp && st;
                if (!NoBlood.Value) { NoHitBlood.Value = sp; NoDeathBlood.Value = sp; NoBloodStains.Value = st; }
            }
            else if (blood == false) NoBlood.Value = false;
            string was;
            if (left.TryGetValue(new ConfigDefinition("Blood", "No Blood Spray"), out was) && was != null && string.Equals(was.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            {
                NoHitBlood.Value = true;
                NoDeathBlood.Value = true;
            }
            left.Clear();
            Config.Save();
        }

        private static void Install(string feature, Action install)
        {
            try { install(); }
            catch (Exception e) { Log.LogWarning("The " + feature + " hooks don't fit this game version, so they're off. (" + e.Message + ")"); }
        }

        internal static void LogErrorOnce(Exception e)
        {
            if (_errorLogged) return;
            _errorLogged = true;
            Log.LogError("Error (further errors are not logged): " + e);
        }

        internal static MethodInfo Method(Type type, string name)
        {
            MethodInfo m = AccessTools.Method(type, name);
            if (m == null) throw new MissingMethodException(type.Name, name);
            return m;
        }

        /// <summary>A FishNet RPC body, "RpcLogic___Name_hash" (the hash changes when the method does).</summary>
        internal static MethodInfo Rpc(Type type, string name)
        {
            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(type))
                if (m.Name.StartsWith("RpcLogic___" + name + "_", StringComparison.Ordinal)) return m;
            throw new MissingMethodException(type.Name, "RpcLogic___" + name);
        }

        internal static HarmonyMethod Patch(Type type, string name)
        {
            return new HarmonyMethod(Method(type, name));
        }
    }

    // ==================================================================== deaths: ragdolls, gibs, hats

    /// <summary>
    /// PlayerHealth's ExplodeForAll RPC runs every death on every client: an explosive death spawns gibs, any other spawns a
    /// ragdoll, knocks the hat off, points the kill cam at the body and, last, spawns the death blood on the body's hips.
    /// Gibs are skipped (the list of parts is emptied for the call). The ragdoll and hat are switched off right after,
    /// before they're ever drawn: switched off, not destroyed, because your spectator camera follows your own body.
    ///
    /// The death blood: the game's own call that spawns it is pointed at SpawnDeathBlood instead (the one call in that
    /// method that spawns something onto a parent). That spawns the same effect once, as the game would: on the hips,
    /// or loose at the same spot when the body is hidden (on a hidden body it would be hidden too), or not at all. No
    /// second object and nothing per frame. If that call isn't found in a new game version, death blood is taken out the
    /// old way (an empty effect in its place) and doesn't show without the body.
    /// </summary>
    internal static class Deaths
    {
        private static readonly GameObject[] NoParts = new GameObject[0];
        private static AccessTools.FieldRef<PlayerHealth, bool> _spawnedRagdoll;
        private static AccessTools.FieldRef<PlayerHealth, GameObject[]> _bodyParts;
        private static AccessTools.FieldRef<PlayerHealth, GameObject> _bloodFX;
        private static AccessTools.FieldRef<PlayerHealth, KillCam> _killCam;
        // the game's death blood goes through SpawnDeathBlood
        private static bool _ownSpawn;

        // death blood spawned loose (its body is hidden), with the hips it would have sat on: it goes when they do
        private struct Loose { public Transform Hips; public GameObject Blood; }
        private static readonly List<Loose> LooseBlood = new List<Loose>(32);

        internal struct State
        {
            public bool Active;
            public GameObject[] Parts;
            public GameObject BloodFX;
            public KillCam Cam; // set when someone else dies: the kill cam is put back where it was
            public Transform CamRagdoll;
            public Vector3 CamFirstPosition;
            public bool CamTrigger;
        }

        internal static void Install(Harmony harmony)
        {
            _spawnedRagdoll = AccessTools.FieldRefAccess<PlayerHealth, bool>("spawnedRagdoll");
            _bodyParts = AccessTools.FieldRefAccess<PlayerHealth, GameObject[]>("bodyParts");
            _bloodFX = AccessTools.FieldRefAccess<PlayerHealth, GameObject>("bloodFX");
            _killCam = AccessTools.FieldRefAccess<PlayerHealth, KillCam>("killCamScript");
            MethodInfo death = NoGorePlugin.Rpc(typeof(PlayerHealth), "ExplodeForAll");
            harmony.Patch(death, NoGorePlugin.Patch(typeof(Deaths), "DeathPrefix"), NoGorePlugin.Patch(typeof(Deaths), "DeathPostfix"));
            try { harmony.Patch(death, null, null, NoGorePlugin.Patch(typeof(Deaths), "DeathTranspiler")); }
            catch (Exception e) { _ownSpawn = false; NoGorePlugin.Log.LogWarning("Death blood is handled the old way in this game version. (" + e.Message + ")"); }
            harmony.Patch(NoGorePlugin.Method(typeof(PlayerHealth), "Hat"), null, NoGorePlugin.Patch(typeof(Deaths), "HatPostfix"));
            // a hat shot off a living player
            harmony.Patch(NoGorePlugin.Method(typeof(Weapon), "TriggerEnvironment"), null, NoGorePlugin.Patch(typeof(Deaths), "ShotHatPostfix"));
        }

        /// <summary>The one call in the death method that spawns something onto a parent (the death blood) is ours from here on.</summary>
        private static IEnumerable<CodeInstruction> DeathTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo mine = AccessTools.Method(typeof(Deaths), "SpawnDeathBlood");
            int at = -1, found = 0;
            for (int i = 0; i < list.Count; i++)
            {
                MethodInfo m = list[i].operand as MethodInfo;
                if (m == null || list[i].opcode != OpCodes.Call || m.Name != "Instantiate" || m.DeclaringType != typeof(UnityEngine.Object)) continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length != 4 || ps[1].ParameterType != typeof(Vector3) || ps[2].ParameterType != typeof(Quaternion) || ps[3].ParameterType != typeof(Transform)) continue;
                if (ps[0].ParameterType != typeof(GameObject) && ps[0].ParameterType != typeof(UnityEngine.Object)) continue;
                at = i; found++;
            }
            _ownSpawn = mine != null && found == 1;
            if (_ownSpawn) list[at].operand = mine;
            else NoGorePlugin.Log.LogWarning("Death blood is handled the old way in this game version (its spawn was found " + found + " times).");
            return list;
        }

        /// <summary>In place of the game's own spawn of the death blood: the same one spawn, or none.</summary>
        private static GameObject SpawnDeathBlood(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
        {
            try
            {
                // loose blood whose body is gone goes too, as it would have on the body
                for (int i = LooseBlood.Count - 1; i >= 0; i--)
                {
                    if (LooseBlood[i].Hips != null) continue;
                    if (LooseBlood[i].Blood != null) UnityEngine.Object.Destroy(LooseBlood[i].Blood);
                    LooseBlood.RemoveAt(i);
                }
                if (NoGorePlugin.DeathBlood) return null;
                if (!NoGorePlugin.Ragdolls) return UnityEngine.Object.Instantiate(prefab, position, rotation, parent);
                GameObject blood = UnityEngine.Object.Instantiate(prefab, position, rotation);
                if (LooseBlood.Count < 256) LooseBlood.Add(new Loose { Hips = parent, Blood = blood });
                return blood;
            }
            catch (Exception e)
            {
                NoGorePlugin.LogErrorOnce(e);
                return UnityEngine.Object.Instantiate(prefab, position, rotation, parent);
            }
        }

        private static void DeathPrefix(PlayerHealth __instance, bool explode, out State __state)
        {
            __state = default(State);
            try
            {
                if (_spawnedRagdoll(__instance)) return; // the RPC can arrive twice: nothing spawns the second time
                __state.Active = true;
                // only where the game's own spawn couldn't be taken over (see SpawnDeathBlood)
                if (!_ownSpawn && NoGorePlugin.DeathBlood)
                {
                    __state.BloodFX = _bloodFX(__instance);
                    _bloodFX(__instance) = Blood.Placeholder();
                }
                if (explode)
                {
                    if (!NoGorePlugin.Ragdolls) return;
                    __state.Parts = _bodyParts(__instance);
                    _bodyParts(__instance) = NoParts;
                }
                else if (NoGorePlugin.Ragdolls && !__instance.IsOwner)
                {
                    // someone else died: the game swings your spectator camera to their (hidden) body, so remember where it was
                    KillCam cam = _killCam(__instance);
                    if (cam == null) return;
                    __state.Cam = cam;
                    __state.CamRagdoll = cam.ragdoll;
                    __state.CamFirstPosition = cam.firstPosition;
                    __state.CamTrigger = cam.triggerLookAtBody;
                }
            }
            catch (Exception e) { NoGorePlugin.LogErrorOnce(e); }
        }

        private static void DeathPostfix(PlayerHealth __instance, bool explode, State __state)
        {
            if (!__state.Active) return;
            try
            {
                if (__state.BloodFX != null) _bloodFX(__instance) = __state.BloodFX;
                if (__state.Parts != null) _bodyParts(__instance) = __state.Parts;
                if (explode || !NoGorePlugin.Ragdolls) return;
                KillCam cam = _killCam(__instance);
                if (cam != null && cam.ragdoll != null) Hide(cam.ragdoll.gameObject); // the game just pointed it at the new ragdoll
                if (__state.Cam == null) return;
                __state.Cam.ragdoll = __state.CamRagdoll;
                __state.Cam.firstPosition = __state.CamFirstPosition;
                __state.Cam.triggerLookAtBody = __state.CamTrigger;
            }
            catch (Exception e) { NoGorePlugin.LogErrorOnce(e); }
        }

        /// <summary>PlayerHealth.Hat: the hat flies off a dying player.</summary>
        private static void HatPostfix(GameObject obj)
        {
            try
            {
                if (NoGorePlugin.Hats && obj != null) Hide(obj);
            }
            catch (Exception e) { NoGorePlugin.LogErrorOnce(e); }
        }

        private static void ShotHatPostfix(GameObject obj)
        {
            try
            {
                if (NoGorePlugin.Hats && obj != null && obj.transform.parent == null && obj.CompareTag("Hat")) Hide(obj);
            }
            catch (Exception e) { NoGorePlugin.LogErrorOnce(e); }
        }

        /// <summary>
        /// Visuals and physics off, then the object. The game switches the last dropped hat back on every frame, so it has to
        /// stay invisible and out of the way while on.
        /// </summary>
        private static void Hide(GameObject go)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
            go.SetActive(false);
        }
    }

    // ==================================================================== blood

    /// <summary>
    /// Weapons keep their blood prefabs in four public fields: three sprays (head, body, generic) and the stain a hit leaves
    /// on the floor or wall behind it ("bloodSplatter"). For the length of each call that can spawn blood, the ones that are
    /// switched off point at an empty placeholder instead, and are put back right after (weapons are shared objects).
    /// Projectiles, shrapnel, grenades and props get theirs swapped for good when they spawn or explode.
    /// </summary>
    internal static class Blood
    {
        private static GameObject _placeholder;
        private static AccessTools.FieldRef<MeleeChildCollision, MeleeWeapon> _meleeWeapon;
        private static readonly Dictionary<MethodBase, AccessTools.FieldRef<object, GameObject>[]> SpawnFields =
            new Dictionary<MethodBase, AccessTools.FieldRef<object, GameObject>[]>();
        // per field of SpawnFields: true = a stain (No Blood Stains), false = blood from a hit (No Hit Blood)
        private static readonly Dictionary<MethodBase, bool[]> SpawnStains = new Dictionary<MethodBase, bool[]>();

        internal struct Saved
        {
            public Weapon Weapon;
            public GameObject Head, Body, Generic, Splatter;
        }

        internal static void Install(Harmony harmony)
        {
            _meleeWeapon = AccessTools.FieldRefAccess<MeleeChildCollision, MeleeWeapon>("weaponScript");
            HarmonyMethod prefix = NoGorePlugin.Patch(typeof(Blood), "WeaponPrefix"), postfix = NoGorePlugin.Patch(typeof(Blood), "WeaponPostfix");
            var missing = new List<string>();
            Action<Func<MethodInfo>, HarmonyMethod> hook = (find, pre) =>
            {
                try { harmony.Patch(find(), pre, postfix); }
                catch (Exception e) { missing.Add(e.Message); }
            };
            foreach (Type gun in new[] { typeof(Gun), typeof(BeamGun), typeof(ChargeGun), typeof(Minigun), typeof(Shotgun), typeof(LargeRaycastGun) })
            {
                hook(() => NoGorePlugin.Method(gun, "ShootServer"), prefix);
                hook(() => NoGorePlugin.Rpc(gun, "ObserversFX"), prefix);
                hook(() => NoGorePlugin.Rpc(gun, "SpawnVFX"), prefix);
            }
            hook(() => NoGorePlugin.Method(typeof(LargeRaycastGun), "ShootServerBox"), prefix);
            hook(() => NoGorePlugin.Method(typeof(MeleeWeapon), "HitServer"), prefix);
            hook(() => NoGorePlugin.Rpc(typeof(MeleeWeapon), "SpawnVFX"), prefix);
            hook(() => NoGorePlugin.Rpc(typeof(Taser), "SpawnVFX"), prefix);
            hook(() => NoGorePlugin.Rpc(typeof(RepulsiveGun), "SpawnVFX"), prefix);
            hook(() => NoGorePlugin.Method(typeof(Weapon), "TriggerEnvironment"), prefix);
            hook(() => NoGorePlugin.Method(typeof(MeleeChildCollision), "OnCollisionEnter"), NoGorePlugin.Patch(typeof(Blood), "MeleePrefix"));

            Action<Func<MethodInfo>, Type, string[]> onSpawn = (find, type, fields) =>
            {
                try { SwapOnSpawn(harmony, find(), type, fields); }
                catch (Exception e) { missing.Add(e.Message); }
            };
            onSpawn(() => NoGorePlugin.Method(typeof(PredictedProjectile), "Awake"), typeof(PredictedProjectile), new[] { "bloodVfx", "headBloodVfx", "bloodSplatter" });
            onSpawn(() => NoGorePlugin.Method(typeof(ShrapnelBallistic), "Awake"), typeof(ShrapnelBallistic), new[] { "bloodSplatter" });
            onSpawn(() => NoGorePlugin.Rpc(typeof(PhysicsGrenade), "HandleExplosion"), typeof(PhysicsGrenade), new[] { "bloodSplatter" });
            onSpawn(() => NoGorePlugin.Method(typeof(PhysicsProp), "Start"), typeof(PhysicsProp), new[] { "bloodImpact" });

            if (missing.Count > 0) NoGorePlugin.Log.LogWarning("NoBlood skipped what's not in this game version: " + string.Join("; ", missing.ToArray()));
        }

        private static void SwapOnSpawn(Harmony harmony, MethodInfo method, Type type, string[] fields)
        {
            var refs = new AccessTools.FieldRef<object, GameObject>[fields.Length];
            var stains = new bool[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo f = AccessTools.Field(type, fields[i]);
                if (f == null) throw new MissingFieldException(type.Name, fields[i]);
                refs[i] = AccessTools.FieldRefAccess<object, GameObject>(f);
                stains[i] = fields[i] == "bloodSplatter";
            }
            SpawnFields[method] = refs;
            SpawnStains[method] = stains;
            harmony.Patch(method, NoGorePlugin.Patch(typeof(Blood), "SpawnPrefix"));
        }

        private static void WeaponPrefix(Weapon __instance, out Saved __state)
        {
            __state = default(Saved);
            if (NoGorePlugin.HitBlood || NoGorePlugin.Stains) Swap(__instance, ref __state);
        }

        private static void MeleePrefix(MeleeChildCollision __instance, out Saved __state)
        {
            __state = default(Saved);
            if (NoGorePlugin.HitBlood || NoGorePlugin.Stains) Swap(_meleeWeapon(__instance), ref __state);
        }

        private static void Swap(Weapon w, ref Saved s)
        {
            if (w == null) return;
            GameObject p = Placeholder();
            s.Weapon = w;
            s.Head = w.headImpact;
            s.Body = w.bodyImpact;
            s.Generic = w.genericBodyImpact;
            s.Splatter = w.bloodSplatter;
            if (NoGorePlugin.HitBlood) w.headImpact = w.bodyImpact = w.genericBodyImpact = p;
            if (NoGorePlugin.Stains) w.bloodSplatter = p;
        }

        /// <summary>Puts the blood back. A call inside another (a shot spawning its hit effects) saved the placeholder: the outer call restores last.</summary>
        private static void WeaponPostfix(Saved __state)
        {
            Weapon w = __state.Weapon;
            if ((object)w == null) return;
            w.headImpact = __state.Head;
            w.bodyImpact = __state.Body;
            w.genericBodyImpact = __state.Generic;
            w.bloodSplatter = __state.Splatter;
        }

        private static void SpawnPrefix(object __instance, MethodBase __originalMethod)
        {
            try
            {
                bool sprays = NoGorePlugin.HitBlood, stains = NoGorePlugin.Stains;
                if (!sprays && !stains) return;
                GameObject p = Placeholder();
                AccessTools.FieldRef<object, GameObject>[] fields = SpawnFields[__originalMethod];
                bool[] isStain = SpawnStains[__originalMethod];
                for (int i = 0; i < fields.Length; i++)
                    if (isStain[i] ? stains : sprays) fields[i](__instance) = p;
            }
            catch (Exception e) { NoGorePlugin.LogErrorOnce(e); }
        }

        internal static GameObject Placeholder()
        {
            if (_placeholder == null)
            {
                _placeholder = new GameObject("NoGore_EmptyBlood") { hideFlags = HideFlags.HideAndDontSave }; // hidden, survives map changes
                _placeholder.AddComponent<EmptyEffect>();
            }
            return _placeholder;
        }

        /// <summary>The empty "blood effect": every copy the game spawns deletes itself on the spot.</summary>
        private sealed class EmptyEffect : MonoBehaviour
        {
            private void Awake()
            {
                if (gameObject != _placeholder) Destroy(gameObject);
            }
        }
    }

    // ==================================================================== mod menu

    /// <summary>
    /// In Mod Menu, a group's own switches are only listed while the group switch is off (Hide Ragdolls and Hide Hats under
    /// Hide Bodies, No Hit Blood, No Death Blood and No Blood Stains under No Blood). Mod Menu has no way to ask for this, so its option
    /// list is read by reflection: every part is looked up by name, and if one is missing every switch is listed.
    /// </summary>
    internal static class MenuLinks
    {
        private static Type _panel, _boxed;
        private static FieldInfo _cache, _current, _source, _search;
        private static PropertyInfo _entry;
        // the setting behind each row of the list (rows are made once and kept by Mod Menu)
        private static readonly Dictionary<int, ConfigEntryBase> RowEntry = new Dictionary<int, ConfigEntryBase>();

        internal static void Install(Harmony harmony)
        {
            // looked up without AccessTools, which writes a warning when Mod Menu is not installed
            _panel = null;
            foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies()) { _panel = a.GetType("ModMenu.Behaviours.OptionList.OptionListPanel", false); if (_panel != null) break; }
            if (_panel == null) return;     // no Mod Menu
            _boxed = AccessTools.TypeByName("ModMenu.Behaviours.OptionList.ValueControllers.BoxedValueController");
            Type option = AccessTools.TypeByName("ModMenu.Options.Option");
            _cache = AccessTools.Field(_panel, "m_optionCache");
            _current = AccessTools.Field(_panel, "m_currentEnabledGuid");
            _search = AccessTools.Field(_panel, "searchBar");
            _source = _boxed != null ? AccessTools.Field(_boxed, "sourceOption") : null;
            _entry = option != null ? AccessTools.Property(option, "BaseEntry") : null;
            if (_boxed == null || _cache == null || _current == null || _source == null || _entry == null) return;

            // the list is filtered again when a group switch changes
            foreach (ConfigEntryBase e in new ConfigEntryBase[] { NoGorePlugin.HideBodies, NoGorePlugin.NoBlood })
            {
                ConfigEntryBase group = e;
                group.ConfigFile.SettingChanged += (sender, args) => { if (args.ChangedSetting == group) Refresh(); };
            }
            HarmonyMethod shown = NoGorePlugin.Patch(typeof(MenuLinks), "ShownPostfix");
            harmony.Patch(NoGorePlugin.Method(_panel, "ShowListFor"), null, shown);
            harmony.Patch(NoGorePlugin.Method(_panel, "FilterOptionList"), null, shown);
        }

        private static bool Hidden(ConfigEntryBase e)
        {
            if (e == NoGorePlugin.HideRagdolls || e == NoGorePlugin.HideHats) return NoGorePlugin.HideBodies.Value;
            if (e == NoGorePlugin.NoHitBlood || e == NoGorePlugin.NoDeathBlood || e == NoGorePlugin.NoBloodStains) return NoGorePlugin.NoBlood.Value;
            return false;
        }

        /// <summary>After Mod Menu has shown or filtered NoGore's list: hides the rows a group switch covers.</summary>
        private static void ShownPostfix(object __instance)
        {
            try
            {
                // only NoGore's own list, and only if Mod Menu has made its rows
                if (!(_current.GetValue(__instance) as string == NoGorePlugin.Guid)) return;
                var cache = _cache.GetValue(__instance) as Dictionary<string, GameObject[]>;
                GameObject[] rows;
                if (cache == null || !cache.TryGetValue(NoGorePlugin.Guid, out rows)) return;
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
            catch (Exception e) { NoGorePlugin.LogErrorOnce(e); }
        }

        /// <summary>Has Mod Menu filter its list again (with what's in its search box), which shows every row and then runs ShownPostfix.</summary>
        private static void Refresh()
        {
            try
            {
                foreach (UnityEngine.Object panel in UnityEngine.Object.FindObjectsOfType(_panel))
                {
                    if (_current.GetValue(panel) as string == NoGorePlugin.Guid)
                    {
                        string search = "";
                        var box = _search != null ? _search.GetValue(panel) as TMP_InputField : null;
                        if (box != null) search = box.text ?? "";
                        NoGorePlugin.Method(_panel, "FilterOptionList").Invoke(panel, new object[] { search });
                    }
                }
            }
            catch (Exception e) { NoGorePlugin.LogErrorOnce(e); }
        }
    }
}
