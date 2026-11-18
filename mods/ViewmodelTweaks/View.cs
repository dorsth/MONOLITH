// ViewmodelTweaks - the viewmodel itself. Dorsth was here.
//
// Up to 1.2.0 this mod was an add-on for Minimal Viewmodels Reborn by kestrel (MIT; its camera approach is still used
// here, see "the second camera"): that mod placed the weapon, this one gave
// single weapons their own values on top of it, by reading its settings, its camera and its private list of arm meshes
// and putting things back whenever it had changed them. From 2.0.0 the mod does the whole job itself and needs nothing
// else. What it has to do is small and all of it is the game's own machinery:
//
//   - the game draws the weapon you hold with a second camera (it sits on the main camera, FOV 75, and only draws the
//     weapon layer), but only at the higher quality settings. Here that camera is always used for your own weapon, is
//     moved against the viewmodel offset and gets its own FOV (and is flipped for a mirrored viewmodel);
//   - sway and bob are numbers on the game's arms script: multiplied by your scales (two more are written into its
//     Update as plain numbers: those are multiplied too);
//   - what belongs to the weapon but is made in the world (your bullet trail, the muzzle effect, an ejected case, a
//     grenade's smoke) is put on the weapon layer while the game makes it, so the second camera draws it with the weapon;
//     the far end of your own trail is shifted by the camera's offset, so it still ends where you aimed.
//
// Not here on purpose (they overlap with other mods): hiding bullet trails is DorsthTweaks' (Hide My Trails), the FOV
// change when running and sliding is CameraTweaks' (from 1.1.5).
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ViewmodelTweaks
{
    internal static class View
    {
        internal static ConfigEntry<float> Fov, X, Y, Z, Sway, Bob;
        internal static ConfigEntry<bool> NoWeapon, NoArms, Mirror;

        internal static Camera WeaponCam;
        internal static SkinnedMeshRenderer[] Arms = new SkinnedMeshRenderer[0];
        internal static int HeldLayer = 8;

        internal static Vector3 Offset { get { return new Vector3(X.Value, Y.Value, Z.Value); } }

        internal static void Bind(ConfigFile c)
        {
            const string V = "Viewmodel";
            Fov = c.Bind(V, "Viewmodel FOV", 75f, new ConfigDescription("FOV your weapon is drawn with. 75 is the game's.", new AcceptableValueRange<float>(30f, 120f)));
            X = c.Bind(V, "Viewmodel X Offset", 0f, new ConfigDescription("Left (negative) or right (positive).", new AcceptableValueRange<float>(-5f, 5f)));
            Y = c.Bind(V, "Viewmodel Y Offset", 0f, new ConfigDescription("Down (negative) or up (positive).", new AcceptableValueRange<float>(-5f, 5f)));
            Z = c.Bind(V, "Viewmodel Z Offset", 0f, new ConfigDescription("Back (negative) or forward (positive).", new AcceptableValueRange<float>(-5f, 5f)));
            Sway = c.Bind(V, "Viewmodel Sway Scale", 1f, new ConfigDescription("How much your weapon sways when you turn, jump and land. 1 is the game's, 0 is none.", new AcceptableValueRange<float>(0f, 2f)));
            Bob = c.Bind(V, "Viewmodel Bob Scale", 1f, new ConfigDescription("How much your weapon bobs when you walk and stand. 1 is the game's, 0 is none.", new AcceptableValueRange<float>(0f, 2f)));
            NoWeapon = c.Bind(V, "Invisible Viewmodel", false, "Hides your weapon and arms completely.");
            NoArms = c.Bind(V, "Invisible Arms", false, "Hides your arms.");
            Mirror = c.Bind(V, "Mirror Viewmodel", false, "Your weapon on the left.");
        }

        internal static void Install(Harmony h)
        {
            int l = LayerMask.NameToLayer("HeldWeapon");
            if (l >= 0) HeldLayer = l;
            Type me = typeof(View);
            const BindingFlags st = BindingFlags.Static | BindingFlags.NonPublic;
            Func<string, HarmonyMethod> M = n => new HarmonyMethod(me.GetMethod(n, st));

            Try("your player's cameras", () => h.Patch(AccessTools.Method(typeof(PlayerSetup), "OnStartClient"), postfix: M("PlayerStarted")));
            Try("sway and bob", () =>
            {
                ArmFields();
                h.Patch(AccessTools.Method(typeof(FPArms), "Start"), postfix: M("ArmsStarted"));
                h.Patch(AccessTools.Method(typeof(FPArms), "Update"), transpiler: M("BobNumbers"));
                if (_bobHits != 2) ViewmodelTweaksPlugin.Log.LogWarning("Bob scale: " + _bobHits + " of 2 plain numbers found in the arms script (game update?); the bob can be a little off.");
            });
            Try("the placement preview", () => h.Patch(AccessTools.Method(typeof(WeaponHandSpawner), "HandlePlacement"), postfix: M("PreviewShown")));
            // what the weapon makes in the world, onto the weapon layer while it is made
            Try("weapon effects", () =>
            {
                HarmonyMethod done = M("FxDone");
                h.Patch(AccessTools.Method(typeof(global::Weapon), "OnShoot"), prefix: M("FxCase"), finalizer: done);
                h.Patch(AccessTools.Method(typeof(DualLauncher), "Update"), prefix: M("FxSmoke"), finalizer: done);
                foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(global::Weapon).Assembly))
                {
                    if (type == typeof(global::Weapon) || !typeof(global::Weapon).IsAssignableFrom(type)) continue;
                    foreach (MethodInfo m in AccessTools.GetDeclaredMethods(type))
                    {
                        if (!m.Name.StartsWith("RpcLogic___ShootObserversEffect", StringComparison.Ordinal)) continue;
                        // the beam gun has two effects: its first method uses its second flash, its second method the first
                        bool second = type == typeof(BeamGun) && m.Name.StartsWith("RpcLogic___ShootObserversEffect_", StringComparison.Ordinal);
                        try { h.Patch(m, prefix: M(second ? "FxFlash2" : "FxFlash"), finalizer: done); }
                        catch (Exception x) { ViewmodelTweaksPlugin.Log.LogWarning("The muzzle effect of " + type.Name + " stays where the game puts it: " + x.Message); }
                    }
                }
            });
        }

        private static void Try(string what, Action a)
        {
            try { a(); } catch (Exception e) { ViewmodelTweaksPlugin.Log.LogWarning("Could not set up " + what + " (game update?): " + e.Message); }
        }

        // ---- the second camera
        // The camera approach below (taking over the game's second camera when your player starts, placing it against
        // the offset, its own projection with near 0.1 and far 100, mirroring with GL.invertCulling) is adapted from
        // Minimal Viewmodels Reborn by kestrel, MIT License, Copyright (c) 2025 kestrel:
        // https://github.com/kestrel-straftat/minimal-viewmodels-reborn (license text: LICENSE-MinimalViewmodelsReborn.txt).

        private static void PlayerStarted(PlayerSetup __instance, Camera[] ___cameras, LayerMask ___highMask, GameObject[] ___fpArms)
        {
            try
            {
                if (__instance == null || !__instance.IsOwner || ___cameras == null || ___cameras.Length < 2) return;
                // the weapon layer is the second camera's, at every quality setting
                ___cameras[0].cullingMask = ___highMask;
                WeaponCam = ___cameras[1];
                var arms = new List<SkinnedMeshRenderer>();
                if (___fpArms != null)
                    foreach (GameObject g in ___fpArms)
                    {
                        SkinnedMeshRenderer r = g != null ? g.GetComponent<SkinnedMeshRenderer>() : null;
                        if (r != null) arms.Add(r);
                    }
                Arms = arms.ToArray();
                WeaponCam.enabled = true;
                // at the two lowest quality settings the game has that camera off and no post-processing: none on it then
                Settings st = Settings.Instance;
                if (st != null && st.qualitySetting < 2)
                {
                    var post = WeaponCam.GetComponent<UnityEngine.Rendering.PostProcessing.PostProcessLayer>();
                    if (post != null) post.enabled = false;
                }
                Apply();
            }
            catch (Exception e) { ViewmodelTweaksPlugin.Log.LogWarning("Viewmodel: " + e.Message); }
        }

        /// <summary>The general settings onto the camera, the arms and the arms script. A weapon with its own values goes on top (the plugin's next frame).</summary>
        internal static void Apply()
        {
            ApplyArmsScript();
            Camera cam = WeaponCam;
            if (cam == null) return;
            bool hideArms = NoArms.Value;
            foreach (SkinnedMeshRenderer r in Arms) if (r != null) r.enabled = !hideArms;
            cam.enabled = !NoWeapon.Value;
            if (!NoWeapon.Value) Place(cam, Fov.Value, Offset);
            MirrorHook(Mirror.Value);
            ViewmodelTweaksPlugin.Applied();
        }

        /// <summary>The second camera against the offset, with its own FOV. Near 0.1 m, far 100 m.</summary>
        internal static void Place(Camera cam, float fov, Vector3 offset)
        {
            cam.transform.localPosition = -offset;
            Matrix4x4 m = Matrix4x4.Perspective(Mathf.Clamp(fov, 1f, 179f), (float)Screen.width / Screen.height, 0.1f, 100f);
            if (Mirror.Value) m *= Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
            cam.projectionMatrix = m;
        }

        // a flipped picture has its triangles facing the other way: the second camera draws them inside out
        private static bool _mirrorHooked;
        private static void MirrorHook(bool on)
        {
            if (on == _mirrorHooked) return;
            _mirrorHooked = on;
            if (on) { Camera.onPreRender += MirrorPre; Camera.onPostRender += MirrorPost; }
            else { Camera.onPreRender -= MirrorPre; Camera.onPostRender -= MirrorPost; GL.invertCulling = false; }
        }
        private static void MirrorPre(Camera cam) { if ((object)cam == (object)WeaponCam) GL.invertCulling = true; }
        private static void MirrorPost(Camera cam) { if ((object)cam == (object)WeaponCam) GL.invertCulling = false; }

        // ---- sway and bob

        private static FPArms _arms;
        private static Vector3 _jump, _fall;
        private static float _bobScale, _crouch, _sprint, _aim, _idle, _rot;
        private static int _bobHits;

        // the arms script keeps these to itself
        private static AccessTools.FieldRef<FPArms, Vector3> JumpOf, FallOf;
        private static AccessTools.FieldRef<FPArms, float> BobOf, CrouchOf, SprintOf, AimOf, IdleOf, RotOf;
        private static bool _armFields;

        private static void ArmFields()
        {
            JumpOf = AccessTools.FieldRefAccess<FPArms, Vector3>("jumpOffset"); FallOf = AccessTools.FieldRefAccess<FPArms, Vector3>("fallOffset");
            BobOf = AccessTools.FieldRefAccess<FPArms, float>("bobScale"); CrouchOf = AccessTools.FieldRefAccess<FPArms, float>("crouchBobScale");
            SprintOf = AccessTools.FieldRefAccess<FPArms, float>("sprintBobScale"); AimOf = AccessTools.FieldRefAccess<FPArms, float>("aimBobScale");
            IdleOf = AccessTools.FieldRefAccess<FPArms, float>("idleScale"); RotOf = AccessTools.FieldRefAccess<FPArms, float>("maxRotationAmount");
            _armFields = true;
        }

        private static void ArmsStarted(FPArms __instance)
        {
            try
            {
                if (!_armFields) return;
                _arms = __instance;
                _jump = JumpOf(__instance); _fall = FallOf(__instance);
                _bobScale = BobOf(__instance); _crouch = CrouchOf(__instance); _sprint = SprintOf(__instance);
                _aim = AimOf(__instance); _idle = IdleOf(__instance); _rot = RotOf(__instance);
                ApplyArmsScript();
            }
            catch (Exception e) { ViewmodelTweaksPlugin.Log.LogWarning("Sway and bob: " + e.Message); }
        }

        private static void ApplyArmsScript()
        {
            FPArms a = _arms;
            if (a == null || !_armFields) return;
            try
            {
                float sway = Sway.Value, bob = Bob.Value;
                RotOf(a) = _rot * sway; JumpOf(a) = _jump * sway; FallOf(a) = _fall * sway;
                BobOf(a) = _bobScale * bob; CrouchOf(a) = _crouch * bob; SprintOf(a) = _sprint * bob;
                AimOf(a) = _aim * bob; IdleOf(a) = _idle * bob;
            }
            catch { }
        }

        private static float BobNumber(float n) { return n * Bob.Value; }

        // the arms script adds 2 and 1.7 to the bob in two places as plain numbers: those follow the bob scale too
        private static IEnumerable<CodeInstruction> BobNumbers(IEnumerable<CodeInstruction> ins)
        {
            var list = new List<CodeInstruction>(ins);
            MethodInfo scale = typeof(View).GetMethod("BobNumber", BindingFlags.Static | BindingFlags.NonPublic);
            _bobHits = 0;
            for (int i = 0; i + 1 < list.Count; i++)
            {
                if (list[i].opcode != OpCodes.Ldc_R4 || list[i + 1].opcode != OpCodes.Add || list[i + 1].labels.Count != 0) continue;
                list.Insert(i + 1, new CodeInstruction(OpCodes.Call, scale));
                _bobHits++; i++;
            }
            return list;
        }

        // ---- small things

        // placing a weapon by hand shows a see-through copy of it: that copy is the main camera's, not the weapon camera's
        private static void PreviewShown(Transform ___previewObject)
        {
            try { if (___previewObject != null && ___previewObject.childCount > 0) ___previewObject.GetChild(0).gameObject.layer = 0; } catch { }
        }

        // ---- what the weapon makes in the world: on the weapon layer while it is made (yours only), back right after

        private struct Saved { public GameObject Go; public int Layer; }
        private static readonly List<Saved> _saved = new List<Saved>(16);

        private static void Lift(GameObject prefab)
        {
            if (prefab == null) return;
            _saved.Add(new Saved { Go = prefab, Layer = prefab.layer });
            prefab.layer = HeldLayer;
            Transform t = prefab.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                GameObject c = t.GetChild(i).gameObject;
                _saved.Add(new Saved { Go = c, Layer = c.layer });
                c.layer = HeldLayer;
            }
        }

        // The methods these run around can sit inside each other (a launcher's Update fires, and firing makes a trail), so
        // each one puts back only what it lifted itself: Begin notes where its part of the list starts, End undoes from there.
        private static readonly List<int> _marks = new List<int>(8);
        private static readonly List<bool> _own = new List<bool>(8);
        /// <summary>Above 0 while one of these methods runs for a weapon of your own: what is made right now is your weapon's effect.</summary>
        internal static int OwnFx;

        internal static void Begin()
        {
            if (_marks.Count > 64) { _marks.Clear(); _own.Clear(); OwnFx = 0; Undo(0); }      // never: a method that was entered and not left
            _marks.Add(_saved.Count); _own.Add(false);
        }

        internal static void End()
        {
            int n = _marks.Count;
            if (n == 0) return;
            int from = _marks[n - 1];
            _marks.RemoveAt(n - 1);
            if (_own[n - 1] && OwnFx > 0) OwnFx--;
            _own.RemoveAt(n - 1);
            if (_saved.Count > from) Undo(from);
        }

        // this method makes an effect of your own weapon from this prefab: on the weapon layer while it is made, and (for One
        // Camera) the prefab carries the marker, so the copy made now reports itself while OwnFx says it is yours
        private static void Own(GameObject prefab)
        {
            if (prefab == null) return;
            Lift(prefab);
            ViewmodelTweaksPlugin.MarkPrefab(prefab);
            int n = _own.Count;
            if (n > 0 && !_own[n - 1]) { _own[n - 1] = true; OwnFx++; }
        }

        private static void Undo(int from)
        {
            for (int i = _saved.Count - 1; i >= from; i--)
            {
                Saved s = _saved[i];
                try { if (s.Go != null) s.Go.layer = s.Layer; } catch { }
            }
            _saved.RemoveRange(from, _saved.Count - from);
        }

        private static bool Mine(global::Weapon w) { return w != null && w.IsOwner && (object)WeaponCam != null; }

        private static void FxCase(global::Weapon __instance, GameObject ___ejectCaseVfx) { Begin(); try { if (Mine(__instance)) Own(___ejectCaseVfx); } catch { } }
        private static void FxFlash(global::Weapon __instance, GameObject ___muzzleFlash) { Begin(); try { if (Mine(__instance)) Own(___muzzleFlash); } catch { } }
        private static void FxFlash2(global::Weapon __instance, GameObject ___muzzleFlash2) { Begin(); try { if (Mine(__instance)) Own(___muzzleFlash2); } catch { } }
        private static void FxSmoke(DualLauncher __instance, bool ___grenadeOpen, ParticleSystem ___grenadeSmoke)
        {
            Begin();
            try { if (___grenadeOpen && ___grenadeSmoke != null && Mine(__instance)) Own(___grenadeSmoke.gameObject); } catch { }
        }
        private static void FxDone() { End(); }

        /// <summary>Your own trail, while it is made: its prefab on the weapon layer, its far end shifted by where the second camera stands.</summary>
        internal static void TrailLift(global::Weapon w, ref Vector3 hitPoint)
        {
            Camera second = WeaponCam;
            if ((object)second == null || second == null || w.cam == null) return;
            hitPoint = second.transform.position + (hitPoint - w.cam.transform.position);
            if (w.bulletTrailLocal != null) Lift(w.bulletTrailLocal.gameObject);
        }

        // ---- settings taken over from Minimal Viewmodels Reborn, once

        internal static ConfigEntryBase TakeOver(ConfigFile c, string configDir)
        {
            ConfigEntry<bool> done = c.Bind("Internal", "Took Over Minimal Viewmodels", false, "Set once this mod has read your Minimal Viewmodels Reborn settings (2.0.0). Leave it.");
            if (done.Value) return done;
            done.Value = true;
            try
            {
                string path = System.IO.Path.Combine(configDir, "kestrel.straftat.minimalviewmodels.cfg");
                if (!System.IO.File.Exists(path)) return done;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                int n = 0;
                foreach (string raw in System.IO.File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0 || line[0] == '#' || line[0] == '[') continue;
                    string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
                    float f; bool b;
                    bool isF = float.TryParse(val, System.Globalization.NumberStyles.Float, inv, out f), isB = bool.TryParse(val, out b);
                    switch (key)
                    {
                        case "Viewmodel FOV": if (isF) { Fov.Value = Mathf.Clamp(f, 30f, 120f); n++; } break;
                        case "Viewmodel X Offset": if (isF) { X.Value = f; n++; } break;
                        case "Viewmodel Y Offset": if (isF) { Y.Value = f; n++; } break;
                        case "Viewmodel Z Offset": if (isF) { Z.Value = f; n++; } break;
                        case "Viewmodel Sway Scale": if (isF) { Sway.Value = f; n++; } break;
                        case "Viewmodel Bob Scale": if (isF) { Bob.Value = f; n++; } break;
                        case "Invisible Viewmodels": if (isB) { NoWeapon.Value = b; n++; } break;
                        case "Invisible Arms": if (isB) { NoArms.Value = b; n++; } break;
                        case "Mirror Viewmodel": if (isB) { Mirror.Value = b; n++; } break;
                    }
                }
                ViewmodelTweaksPlugin.Log.LogInfo("Took over " + n + " settings from Minimal Viewmodels Reborn (once). Hide Bullet Trails is DorsthTweaks' Hide My Trails; the run and slide FOV are CameraTweaks'.");
            }
            catch (Exception e) { ViewmodelTweaksPlugin.Log.LogWarning("Couldn't read Minimal Viewmodels Reborn's settings: " + e.Message); }
            return done;
        }
    }
}
