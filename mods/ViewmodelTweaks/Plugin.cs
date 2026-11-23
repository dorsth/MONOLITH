// ViewmodelTweaks (was SwordStance) - a STRAFTAT mod (BepInEx 5). Up to 1.2.0 an add-on for Minimal Viewmodels Reborn by kestrel; from 2.0.0 it needs nothing else (see View.cs). Dorsth was here.

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using ComputerysModdingUtilities;
using HarmonyLib;
using UnityEngine;
using UnityEngine.LowLevel;

[assembly: AssemblyTitle("ViewmodelTweaks")]
[assembly: AssemblyDescription("Viewmodel FOV, position, sway, bob and arms, with its own values for any weapon you add.")]
[assembly: AssemblyVersion("2.0.8.0")]
[assembly: StraftatMod(isVanillaCompatible: true)]

namespace ViewmodelTweaks
{
    /// <summary>
    /// The weapons you can give their own viewmodel: melee first, then the guns A to Z. The name is the weapon's object
    /// name in the game (what's matched); the label is how the game calls it.
    /// </summary>
    public enum Weapon
    {
        None, Katana, Flamberge,
        AAA12, AK_K, AR15, Bayshore, BeamLoad, Bender, BlankState, Bukanee, Crisis, DF_Torrent, DualLauncher, Elephant,
        FG42, Gamma, GammaGen2, GlaiveGun, Glock, Gun, Gust, HandCanon, Havoc, Hill_H15, HK_Caws, HK_G11, Impetus, Kanye,
        Keso, Kusma, M2000, Mac10, Minigun, Mortini, Oklahoma, Phoenix, Prophet, QCW05, Revolver, RocketLauncher, SawedOff,
        Serac, Shotgun, Silenzzio, SmithCarbine, SMG, Tromblonj, Warden, Webley, Yangtse,
    }

    /// <summary>One weapon's own viewmodel: which weapon, its FOV, X/Y/Z offset and whether your arms show.</summary>
    internal sealed class Slot
    {
        internal ConfigEntry<Weapon> Weapon;
        internal ConfigEntry<float> Fov, X, Y, Z;
        internal ConfigEntry<bool> NoArms;
        internal bool Used => Weapon.Value != ViewmodelTweaks.Weapon.None;
        internal IEnumerable<ConfigEntryBase> Entries => new ConfigEntryBase[] { Weapon, Fov, X, Y, Z, NoArms };
    }

    /// <summary>
    /// The gun you hold is placed by moving the camera that draws it (Viewmodel X, Y and Z Offset) and giving that
    /// camera its own field of view (Viewmodel FOV): View.cs. Here any weapon you add gets its own: while you hold it,
    /// that camera goes where its settings say and your arms show or hide by its own Invisible Arms; any other weapon
    /// gets the general settings back. Only changes anything when what you hold changes, or when a setting does.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("kestrel.straftat.modmenu", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class ViewmodelTweaksPlugin : BaseUnityPlugin
    {
        public const string Guid = "dorsth.straftat.viewmodeltweaks";
        public const string Name = "ViewmodelTweaks";
        public const string Version = "2.0.8";
        internal const int MaxSlots = 10;

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Detail;
        // what One Camera does, for measuring: only with Detailed Log on
        internal static void Note(string s) { if (Detail != null && Detail.Value) Log.LogInfo(s); }
        // This mod's code runs every frame and inside the game's own methods: a problem is written once, not every frame.
        private static readonly HashSet<string> Said = new HashSet<string>();
        internal static void WarnOnce(string s) { if (Said.Count < 32 && Said.Add(s)) Log.LogWarning(s); }
        internal static readonly List<Slot> Slots = new List<Slot>();
        private static bool _dirty = true;
        private static Slot _shown;
        private static PlayerPickup _me;

        // ---- One Camera
        // Your weapon is drawn with the game's second camera (View.cs). Having that camera on costs about 4% of the
        // frame rate whatever it draws (measured: 35 us a frame, with two different settings of that camera making no
        // difference). With One Camera on, that camera is off and your main camera draws the weapon, and the weapon is
        // made to look as it does through the second camera:
        //   - only while the frame is drawn (from after the last script of the frame until the main camera has drawn), the
        //     one object your arms and weapon hang under ("BobPosition", straight under the camera, never rotated) is moved
        //     and scaled: sideways and up by tan(main FOV / 2) / tan(viewmodel FOV / 2), which on screen is exactly what a
        //     camera with the viewmodel FOV shows; shifted by the viewmodel offset; and pulled towards your eye to a tenth
        //     of its distance, which changes nothing on screen but keeps it in front of walls (the second camera did that
        //     by drawing on top of everything).
        //   - right after the camera has drawn it is put back exactly as it was. No script of the game runs in between
        //     (every Update, LateUpdate and the network's sending are done by then): your shots, hits, melee swings and what
        //     is sent to other players use the weapon where it really is.
        internal static ConfigEntry<bool> OneCamera, HideEffects;
#if DORSTH_BUNDLE
        // in MONOLITH the two are one switch ("Performance viewmodel"): with One Camera on, the loose effects are never drawn
        private static bool HidesEffects { get { return true; } }
#else
        private static bool HidesEffects { get { return HideEffects.Value; } }
#endif
        // How far the weapon is pulled towards your eye: to a tenth of its distance. The second camera cuts off
        // what is nearer than 0.1 m; at a tenth, with the main camera's own 0.01 m, the cut is in the same place. If another mod
        // has given the main camera a larger near distance, the weapon is pulled in less, so nothing more is cut off.
        private static float Near(Camera cam) { return Mathf.Clamp(cam.nearClipPlane * 10f, 0.1f, 1f); }
        private static Camera _main; private static Transform _bob; private static Camera _second;
        private static bool _oneOn, _posed, _hooked;
        private static Vector3 _bobPos, _bobScale;
        private static int _heldLayerBit;

        // The values the weapon is drawn with right now: the weapon's own (if you added it) or the general ones.
        private static float ViewFov() { Slot s = _shown; return s != null ? s.Fov.Value : View.Fov.Value; }
        private static Vector3 ViewOffset() { Slot s = _shown; return s != null ? new Vector3(s.X.Value, s.Y.Value, s.Z.Value) : View.Offset; }

        // Your arms hang under that object too (BobPosition/FPArms), and a mesh with bones takes its bones' places once a
        // frame, before any camera starts drawing. A move made only while the main camera draws leaves the arms at their
        // real size. So the move is made one step earlier: after every script of the frame has run and just before the
        // engine takes the bones' places. Unity's own frame loop gets two steps of this mod: one there, one right after
        // the frame is drawn.
        private sealed class PoseStep { }
        private sealed class UnposeStep { }
        private sealed class TrailStep { }
        private static int _poseFrame = -1, _loopTries;
        private static bool _loopIn, _loopBroken;

        /// <summary>Puts this mod's two steps into Unity's frame loop (or takes them out). False when the loop is not built as expected.</summary>
        private static bool LoopSet(bool on)
        {
            try
            {
                PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
                if (loop.subSystemList == null) return false;
                var phases = (PlayerLoopSystem[])loop.subSystemList.Clone();
                bool placed = false, changed = false;
                for (int i = 0; i < phases.Length; i++)
                {
                    PlayerLoopSystem[] subs = phases[i].subSystemList;
                    if (subs == null) continue;
                    var l = new List<PlayerLoopSystem>(subs);
                    if (l.RemoveAll(x => x.type == typeof(PoseStep) || x.type == typeof(UnposeStep) || x.type == typeof(TrailStep)) > 0) changed = true;
                    if (on && !placed && phases[i].type != null && phases[i].type.Name == "PostLateUpdate")
                    {
                        int skin = l.FindIndex(x => x.type != null && x.type.Name == "UpdateAllSkinnedMeshes");
                        int draw = l.FindIndex(x => x.type != null && x.type.Name == "FinishFrameRendering");
                        if (skin >= 0 && draw > skin)
                        {
                            l.Insert(draw + 1, new PlayerLoopSystem { type = typeof(UnposeStep), updateDelegate = UnposeNow });
                            l.Insert(skin, new PlayerLoopSystem { type = typeof(PoseStep), updateDelegate = PoseNow });
                            // your bullet trails a little earlier: before the engine works out what is in view
                            int rend = l.FindIndex(x => x.type != null && x.type.Name == "UpdateAllRenderers");
                            _trailsInPose = !(rend >= 0 && rend < skin);
                            if (!_trailsInPose) l.Insert(rend, new PlayerLoopSystem { type = typeof(TrailStep), updateDelegate = FrameNow });
                            placed = true; changed = true;
                        }
                    }
                    phases[i].subSystemList = l.ToArray();
                }
                if (on && !placed) return false;
                if (changed) { loop.subSystemList = phases; PlayerLoop.SetPlayerLoop(loop); }
                _loopIn = on; _poseFrame = Time.frameCount;
                return true;
            }
            catch (Exception e) { Log.LogWarning("One Camera, frame loop: " + e.Message); return false; }
        }

        // after every script's LateUpdate, before the engine takes the bones' places for this frame
        private static void PoseNow()
        {
            _poseFrame = Time.frameCount;
            if (!_oneOn) return;
            if (_trailsInPose) FrameNow();
            if (_posed) Unpose();
            if (_two) return;       // this frame the second camera draws the weapon, where it really is
            Camera cam = _main; Transform b = _bob;
            if (cam == null || b == null || !cam.isActiveAndEnabled) return;
            Pose(cam, b);
        }

        // right after the frame is drawn, also when the main camera did not draw (then its own hook below never ran)
        private static void UnposeNow() { if (_posed) Unpose(); }

        /// <summary>The weapon's move for this frame: scale per axis and the offset, in the camera's space. False when there is none.</summary>
        private static bool Move(Camera cam, out float sx, out float sy, out float sz, out Vector3 off)
        {
            sx = sy = sz = 1f; off = Vector3.zero;
            float k = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(Mathf.Clamp(ViewFov(), 1f, 179f) * 0.5f * Mathf.Deg2Rad);
            if (float.IsNaN(k) || k <= 0f || k > 50f) return false;
            float near = Near(cam);
            sx = k * near * (View.Mirror.Value ? -1f : 1f); sy = k * near; sz = near;
            off = ViewOffset();
            return true;
        }

        /// <summary>Switches One Camera on or off to match the setting; run whenever something changed.</summary>
        private static void SyncOneCamera()
        {
            Camera second = View.WeaponCam;
            bool want = OneCamera.Value && second != null && !View.NoWeapon.Value;
            if (want && ((object)second != (object)_second || _main == null || _bob == null))
            {
                // a new player (a new round): find its cameras again
                if (_posed) Unpose();
                ModeReport();
                LooseClear();
                _second = second; _oneOn = false;
                Transform parent = second.transform.parent;
                _main = parent != null ? parent.GetComponent<Camera>() : null;
                _bob = parent != null ? parent.Find("BobPosition") : null;
                if (_main == null || _bob == null)
                {
                    WarnOnce("One Camera: the player's camera is not built as expected (a game update?), left off.");
                    want = false;
                }
            }
            if (want)
            {
                if (_heldLayerBit == 0) { int l = LayerMask.NameToLayer("HeldWeapon"); _heldLayerBit = 1 << (l < 0 ? 8 : l); }
                if (!_hooked) { Camera.onPreCull += PreCull; Camera.onPostRender += PostRender; _hooked = true; }
                // which camera draws the weapon is decided every frame (ModeNow): the main one, unless a loose effect is showing
                // every weapon there is gets its effect prefabs marked now: one held before One Camera came on would
                // show its effect, big, at the real muzzle
                if (!_oneOn)
                    foreach (global::Weapon w in UnityEngine.Object.FindObjectsOfType<global::Weapon>()) if (w != null) MarkEffects(w.gameObject);
                if (!_loopIn && !_loopBroken && !LoopSet(true))
                {
                    _loopBroken = true;
                    Log.LogWarning("One Camera: Unity's frame loop is not built as expected. Your weapon is still drawn right, your arms can be the wrong size.");
                }
                if (!_oneOn) { _oneOn = true; Note("One Camera on: the main camera draws your weapon and arms; the second camera only comes on while a loose effect of a weapon is showing."); }
            }
            else if (_oneOn || _hooked)
            {
                if (_posed) Unpose();
                if (_loopIn) LoopSet(false);
                if (_hooked) { Camera.onPreCull -= PreCull; Camera.onPostRender -= PostRender; _hooked = false; }
                if (_oneOn)
                {
                    _oneOn = false; _two = false;
                    ModeReport();
                    LooseClear();
                    if (_main != null) _main.cullingMask &= ~_heldLayerBit;
                    // the second camera, its place and its FOV back as the general settings say (and a weapon's own on top, next frame)
                    try { View.Apply(); } catch (Exception e) { WarnOnce("One Camera, switching off: " + e.Message); }
                    Note("One Camera off: the second camera draws your weapon again.");
                }
            }
        }

        // Just before the main camera decides what is visible. Normally the move is already made (PoseNow); only in the first
        // frame after switching on, or when the frame loop could not be used, it is made here (the gun right, the arms not yet).
        private static void PreCull(Camera cam)
        {
            if (!_oneOn || _posed || (object)cam != (object)_main) return;
            if (_loopBroken) FrameNow();
            if (_two) return;
            Transform b = _bob;
            if (b == null) return;
            Pose(cam, b);
        }

        private static void Pose(Camera cam, Transform b)
        {
            try
            {
                float sx, sy, sz; Vector3 off;
                if (!Move(cam, out sx, out sy, out sz, out off)) return;
                Loosen(b);
                _bobPos = b.localPosition; _bobScale = b.localScale;
                Vector3 p = _bobPos + off;
                b.localPosition = new Vector3(p.x * sx, p.y * sy, p.z * sz);
                b.localScale = new Vector3(_bobScale.x * sx, _bobScale.y * sy, _bobScale.z * sz);
                _posed = true;
            }
            catch (Exception e) { WarnOnce("One Camera: " + e.Message); }
        }

        private static void PostRender(Camera cam)
        {
            if (_posed && (object)cam == (object)_main) Unpose();
        }

        private static void Unpose()
        {
            _posed = false;
            try { if (_bob != null) { _bob.localPosition = _bobPos; _bob.localScale = _bobScale; } }
            catch (Exception e) { WarnOnce("One Camera, putting the weapon back: " + e.Message); }
            Fasten();
        }

        // The placement preview of a mine or claymore hangs under the weapon but stands in the world, where your crosshair
        // points. Moved with the weapon it showed closer and lower than where the mine goes, so it is taken out from under
        // the weapon while the frame is drawn and put back after.
        private static Transform _prev, _prevParent;
        private static int _prevIndex;
        private static Vector3 _prevPos, _prevScale;
        private static Quaternion _prevRot;

        private static void Loosen(Transform b)
        {
            _prev = null;
            try
            {
                Transform t = View.Preview;
                if (t == null || !t.gameObject.activeInHierarchy || t.parent == null || !t.IsChildOf(b)) return;
                _prevParent = t.parent; _prevIndex = t.GetSiblingIndex();
                _prevPos = t.localPosition; _prevRot = t.localRotation; _prevScale = t.localScale;
                t.SetParent(null, true);
                _prev = t;
            }
            catch (Exception e) { _prev = null; WarnOnce("One Camera, placement preview: " + e.Message); }
        }

        private static void Fasten()
        {
            Transform t = _prev;
            _prev = null;
            if (t == null) return;
            try
            {
                t.SetParent(_prevParent, false); t.SetSiblingIndex(_prevIndex);
                t.localPosition = _prevPos; t.localRotation = _prevRot; t.localScale = _prevScale;
            }
            catch (Exception e) { WarnOnce("One Camera, placement preview: " + e.Message); }
        }

        // ---- Loose effects of a weapon under One Camera: the second camera on demand
        // A muzzle effect, an ejected case or pin and a launcher's smoke are particle systems made in the world (not under
        // the object that is moved), on the weapon layer: only the second camera draws them, from its place. Drawn by the
        // main camera they stand at the real muzzle, a few times too big and too close, and particles that live in the
        // world cannot be given the weapon's move.
        // So for as long as such an effect has live particles the frame is drawn exactly as without One Camera: second
        // camera on, the weapon not moved, trails back at their real points. The frames without one are One Camera's.
        //   - every prefab a weapon makes such effects from (the fields below) gets a marker; an instance reports itself;
        //   - at the end of the frame it was made in, an instance with a renderer on the weapon layer is kept on a list;
        //   - a frame is a two-camera frame while any instance on the list is alive (its particles, or the object itself
        //     when it has none).
        private static readonly string[] EffectFields = { "ejectCaseVfx", "reloadEjectVfx", "muzzleFlash", "muzzleFlash2", "grenadeSmoke" };
        private static readonly Dictionary<Type, FieldInfo[]> _effectFields = new Dictionary<Type, FieldInfo[]>();
        private static readonly HashSet<int> _markedFx = new HashSet<int>();
        private static readonly OwnLoose[] _born = new OwnLoose[64], _loose = new OwnLoose[64];
        private static int _bornCount, _looseCount, _framesOne, _framesTwo;
        private static bool _two;
        private static readonly List<Renderer> _rends = new List<Renderer>();

        private static void WeaponAwakePostfix(global::Weapon __instance)
        {
            try { if (__instance != null) MarkEffects(__instance.gameObject); }
            catch (Exception e) { WarnOnce("One Camera, a weapon's effects: " + e.Message); }
        }

        private static void MarkEffects(GameObject weapon)
        {
            if (weapon == null) return;
            global::Weapon w = weapon.GetComponent<global::Weapon>();
            if (w == null) return;
            Type t = w.GetType();
            FieldInfo[] fs;
            if (!_effectFields.TryGetValue(t, out fs))
            {
                var found = new List<FieldInfo>();
                const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (string name in EffectFields)
                    for (Type x = t; x != null && x != typeof(MonoBehaviour); x = x.BaseType)
                    {
                        FieldInfo f = x.GetField(name, all);
                        if (f != null) { found.Add(f); break; }
                    }
                _effectFields[t] = fs = found.ToArray();
            }
            for (int i = 0; i < fs.Length; i++)
            {
                object v = fs[i].GetValue(w);
                GameObject go = v as GameObject;
                if (go == null) { Component c = v as Component; if (c != null) go = c.gameObject; }
                MarkPrefab(go);
            }
        }

        internal static void LooseBorn(OwnLoose m)
        {
            if (!_oneOn) return;
            // made right now by one of your own weapon's effect methods (View.OwnFx): with "hide" on it is never drawn.
            // Its distance to your eye is no test for "yours": a long gun's muzzle is further away than a short one's.
            m.Own = View.OwnFx > 0;
            if (m.Own && HidesEffects) { HideNow(m.gameObject); return; }
            if (_bornCount < _born.Length) _born[_bornCount++] = m;
        }

        internal static void MarkPrefab(GameObject prefab)
        {
            if (prefab == null) return;
            if (_markedFx.Count > 1024) _markedFx.Clear();
            if (_markedFx.Add(prefab.GetInstanceID()) && prefab.GetComponent<OwnLoose>() == null) prefab.AddComponent<OwnLoose>();
        }

        private static readonly HashSet<string> _hidSaid = new HashSet<string>();
        private static void HideNow(GameObject go)
        {
            go.GetComponentsInChildren(true, _rends);
            int n = _rends.Count;
            for (int r = 0; r < n; r++) _rends[r].enabled = false;
            _rends.Clear();
            if (_hidSaid.Count < 40 && _hidSaid.Add(go.name)) Note("One Camera: your weapon's effect " + go.name.Replace("(Clone)", "").Trim() + " is not drawn (" + n + " parts).");
        }

        // ---- Particle effects that are part of the weapon you hold (the Prophet has one at its muzzle, always on)
        // They are not made when you fire, so nothing above sees them. Their particles are worked out at the weapon's real
        // place and size, and the main camera draws them there: big, around the gun. With "hide" on they are not drawn
        // while you hold the weapon and come back when you let go of it; with "hide" off, holding such a weapon means
        // two-camera frames.
        private static GameObject _heldR, _heldL;
        private static bool _heldHide, _heldFx, _heldOne;
        // A melee weapon hits with a collider that hangs under the object One Camera moves (MeleeChildCollision, a child of
        // the weapon, swung by its animation), and its swing trail is made from that collider's place. While one is in your
        // hands nothing is moved: every frame is a two-camera frame, drawn and hit-tested exactly as without One Camera.
        // Moved like a gun, the trail is drawn as a wide white bar, and a moved collider could change a hit.
        private static bool _heldMelee;
        private static bool Melee(GameObject weapon) { return weapon != null && weapon.GetComponent<MeleeWeapon>() != null; }
        private static readonly List<ParticleSystemRenderer> _heldParts = new List<ParticleSystemRenderer>(), _partBuf = new List<ParticleSystemRenderer>();

        private static void TrackHeld()
        {
            PlayerPickup me = _me;
            GameObject r = null, l = null;
            if (me != null) { r = me.objInHand; l = me.objInLeftHand; }
            bool hide = _oneOn && HidesEffects;
            if ((object)r == (object)_heldR && (object)l == (object)_heldL && hide == _heldHide && _oneOn == _heldOne) return;
            _heldOne = _oneOn;
            for (int i = 0; i < _heldParts.Count; i++) if (_heldParts[i] != null) _heldParts[i].enabled = true;
            _heldParts.Clear();
            _heldR = r; _heldL = l; _heldHide = hide; _heldFx = false;
            _heldMelee = Melee(r) || Melee(l);
            if (!_oneOn) return;
            HeldParts(r, hide); HeldParts(l, hide);
        }

        private static void HeldParts(GameObject weapon, bool hide)
        {
            if (weapon == null) return;
            weapon.GetComponentsInChildren(false, _partBuf);
            int n = 0;
            for (int i = 0; i < _partBuf.Count; i++)
            {
                ParticleSystemRenderer p = _partBuf[i];
                if (p == null || !p.enabled) continue;
                n++;
                if (hide) { p.enabled = false; _heldParts.Add(p); }
            }
            _partBuf.Clear();
            if (n == 0) return;
            if (!hide) _heldFx = true;
            string name = weapon.name.Replace("(Clone)", "").Trim();
            if (_hidSaid.Count < 40 && _hidSaid.Add("held " + name))
                Note("One Camera: " + name + " has " + n + " particle effect" + (n == 1 ? "" : "s") + " of its own; " + (hide ? "not drawn while you hold it." : "the second camera stays on while you hold it."));
        }

        private static void LooseClear()
        {
            Array.Clear(_born, 0, _born.Length); Array.Clear(_loose, 0, _loose.Length);
            _bornCount = 0; _looseCount = 0;
        }

        private static void ModeReport()
        {
            int n = _framesOne + _framesTwo;
            if (n > 600) Note("One Camera: of the last " + n + " frames, " + (100f * _framesTwo / n).ToString("0.0") + "% needed the second camera (a weapon effect was showing, or a melee weapon was in your hands).");
            _framesOne = 0; _framesTwo = 0;
        }

        // once a frame, after the last script: which camera draws the weapon this frame, then the trails for that
        private static void FrameNow()
        {
            if (!_oneOn) return;
            try { ModeNow(); } catch (Exception e) { _two = false; WarnOnce("One Camera, effects: " + e.Message); }
            TrailsNow();
        }

        // Yours: part of your player, or made within 1.5 m of your eye (your muzzle and where your cases come out are well
        // inside that). Another player's effect on the weapon layer (the game does that for one gun's flash) is never hidden;
        // it gets a two-camera frame instead, so it shows as always.
        private static bool Mine(Transform t)
        {
            Camera main = _main;
            if (main == null) return false;
            Transform c = main.transform;
            return t.IsChildOf(c.root) || (t.position - c.position).sqrMagnitude < 2.25f;
        }

        private static void ModeNow()
        {
            // this frame's new effects: kept when something of them is on the weapon layer (the game puts that on after making them)
            int held = _heldLayerBit;
            for (int i = 0; i < _bornCount; i++)
            {
                OwnLoose m = _born[i]; _born[i] = null;
                if (m == null || _looseCount >= _loose.Length) continue;
                GameObject go = m.gameObject;
                if (!go.scene.IsValid()) continue;          // the prefab itself
                go.GetComponentsInChildren(false, _rends);
                bool onLayer = false;
                for (int r = 0; r < _rends.Count; r++) if (((1 << _rends[r].gameObject.layer) & held) != 0) { onLayer = true; break; }
                if (onLayer && HidesEffects && (m.Own || Mine(go.transform)))
                {
                    // your own weapon's effect, with "hide" on: what only the second camera could draw is not drawn at all.
                    // The object, its light and its sounds stay as the game made them.
                    for (int r = 0; r < _rends.Count; r++) if (((1 << _rends[r].gameObject.layer) & held) != 0) _rends[r].enabled = false;
                    _rends.Clear();
                    continue;
                }
                _rends.Clear();
                if (!onLayer) continue;
                m.Root = go.GetComponentInChildren<ParticleSystem>();
                _loose[_looseCount++] = m;
            }
            _bornCount = 0;
            bool two = false;
            for (int i = _looseCount - 1; i >= 0; i--)
            {
                OwnLoose m = _loose[i];
                if (m == null) { _loose[i] = _loose[--_looseCount]; _loose[_looseCount] = null; continue; }
                if (two) continue;
                ParticleSystem ps = m.Root;
                if (ps != null ? ps.IsAlive(true) : m.gameObject.activeInHierarchy) two = true;
            }
            if (_heldFx) two = true;        // a weapon in your hands with a particle effect of its own, and "hide" off
            if (_heldMelee) two = true;     // a melee weapon in your hands: never moved
            Camera main = _main, second = _second;
            if (main == null || second == null || !main.isActiveAndEnabled) { _two = false; return; }   // dead or gone: the game's own camera states
            if (second.enabled != two) second.enabled = two;
            bool has = (main.cullingMask & held) != 0;
            if (has == two) main.cullingMask = two ? (main.cullingMask & ~held) : (main.cullingMask | held);
            _two = two;
            if (two) _framesTwo++; else _framesOne++;
        }

        // ---- Your own bullet trails under One Camera
        // With two cameras a trail of yours is a line in the world from the real muzzle to the hit (shifted by where
        // the second camera stands), drawn by the second camera only: from that camera's place, with the viewmodel FOV,
        // on top of the world. Drawn by the main camera as it is, its start sits about two metres ahead and, with a wide
        // FOV, at or under the floor.
        // So the trail is made exactly as with two cameras, and every frame its two points get the weapon's move for
        // that frame: where the second camera would have shown them. Only your own trails; nothing but the line's two
        // points and its width is written.
        private struct Trail { public LineRenderer Line; public Vector3 W0, W1; public float Width; public bool Seen, Shrinks, Mapped; }
        private static readonly Trail[] _trails = new Trail[128];
        private static int _trailCount;
        private static bool _ownSpawn, _trailsInPose;
        private static readonly HashSet<int> _marked = new HashSet<int>();
        // last frame's move (to read back a trail the game itself shortens every frame)
        private static bool _mapOk; private static Vector3 _mPos, _mOff, _mScale; private static Quaternion _mRot;

        // While one of your trails is made (the game's method, or a mod's in its place): the new line reports itself.
        private static void TrailPrefix(global::Weapon __instance, ref Vector3 hitPoint)
        {
            _ownSpawn = false;
            View.Begin();
            try
            {
                if (__instance == null || !__instance.IsOwner) return;
                // with or without One Camera: the trail belongs to the weapon's picture, and ends where you aimed
                View.TrailLift(__instance, ref hitPoint);
                if (!_oneOn || _main == null) return;
                LineRenderer pre = __instance.bulletTrailLocal;
                if (pre == null) return;
                // the line every trail of this gun is copied from carries a marker (once): a copy made right now is yours
                if (_marked.Count > 512) _marked.Clear();
                if (_marked.Add(pre.GetInstanceID()) && pre.GetComponent<OwnTrail>() == null) pre.gameObject.AddComponent<OwnTrail>();
                _ownSpawn = true;
            }
            catch (Exception e) { WarnOnce("One Camera, a bullet trail: " + e.Message); }
        }
        private static void TrailFinalizer() { _ownSpawn = false; View.End(); }

        // a grenade's pin: once it has come off your weapon it is a loose thing on the weapon layer
        private static readonly HashSet<int> _pins = new HashSet<int>();
        private static void PinPostfix(DualLauncher __instance, GameObject ___douilleGrenade)
        {
            try
            {
                if (!_oneOn || (object)___douilleGrenade == null || !__instance.grenadeOpen || !__instance.IsOwner) return;
                if (___douilleGrenade == null || ___douilleGrenade.transform.parent != null) return;
                if (_pins.Count > 256) _pins.Clear();
                if (!_pins.Add(___douilleGrenade.GetInstanceID())) return;
                if (HidesEffects) HideNow(___douilleGrenade);
                else if (___douilleGrenade.GetComponent<OwnLoose>() == null) ___douilleGrenade.AddComponent<OwnLoose>();
            }
            catch (Exception e) { WarnOnce("One Camera, a grenade pin: " + e.Message); }
        }

        internal static void TrailBorn(OwnTrail t)
        {
            if (!_ownSpawn || _trailCount >= _trails.Length) return;
            LineRenderer line = t.GetComponent<LineRenderer>();
            if (line == null) return;
            _trails[_trailCount++] = new Trail { Line = line };
        }

        // every frame, after the last script and before the engine looks at what it has to draw
        private static void TrailsNow()
        {
            int n = _trailCount;
            if (n == 0) return;
            try
            {
                Camera cam = _main;
                float sx = 1f, sy = 1f, sz = 1f; Vector3 off = Vector3.zero;
                if (!_oneOn || cam == null || !cam.isActiveAndEnabled || !Move(cam, out sx, out sy, out sz, out off))
                {
                    Array.Clear(_trails, 0, n); _trailCount = 0; _mapOk = false;
                    return;
                }
                Vector3 cp; Quaternion cr;
                cam.transform.GetPositionAndRotation(out cp, out cr);
                Quaternion inv = Quaternion.Inverse(cr);
                Quaternion pinv = _mapOk ? Quaternion.Inverse(_mRot) : inv;
                for (int i = n - 1; i >= 0; i--)
                {
                    LineRenderer line = _trails[i].Line;
                    if (line == null || line.positionCount != 2)
                    {
                        _trails[i] = _trails[--_trailCount]; _trails[_trailCount] = default(Trail);
                        continue;
                    }
                    if (!_trails[i].Seen)
                    {
                        // first frame: the line as the game (or a mod) made it, in the world
                        _trails[i].W0 = line.GetPosition(0); _trails[i].W1 = line.GetPosition(1);
                        _trails[i].Width = line.widthMultiplier;
                        _trails[i].Seen = true; _trails[i].Mapped = false;
                        LineFade fade = line.GetComponent<LineFade>();
                        _trails[i].Shrinks = fade != null && fade.decreaseInSize;
                    }
                    else if (_trails[i].Shrinks)
                    {
                        // the game pulled its end in a little this frame: read it back (through the last move, if it was moved)
                        if (!_trails[i].Mapped) { _trails[i].W0 = line.GetPosition(0); _trails[i].W1 = line.GetPosition(1); }
                        else if (_mapOk) { _trails[i].W0 = Back(line.GetPosition(0), pinv); _trails[i].W1 = Back(line.GetPosition(1), pinv); }
                    }
                    if (_two)
                    {
                        // a two-camera frame: the line where it really is, as thick as it was made
                        if (_trails[i].Mapped)
                        {
                            line.SetPosition(0, _trails[i].W0); line.SetPosition(1, _trails[i].W1);
                            line.widthMultiplier = _trails[i].Width; _trails[i].Mapped = false;
                        }
                        continue;
                    }
                    Vector3 a = inv * (_trails[i].W0 - cp) + off, b = inv * (_trails[i].W1 - cp) + off;
                    line.SetPosition(0, cp + cr * new Vector3(a.x * sx, a.y * sy, a.z * sz));
                    line.SetPosition(1, cp + cr * new Vector3(b.x * sx, b.y * sy, b.z * sz));
                    if (!_trails[i].Mapped)
                    {
                        line.widthMultiplier = _trails[i].Width * Mathf.Abs(sx);     // as thick on screen as through the second camera
                        _trails[i].Mapped = true;
                    }
                }
                if (_two) return;
                _mPos = cp; _mRot = cr; _mOff = off; _mScale = new Vector3(sx, sy, sz); _mapOk = true;
            }
            catch (Exception e) { _trailCount = 0; WarnOnce("One Camera, trails: " + e.Message); }
        }

        private static Vector3 Back(Vector3 shown, Quaternion pinv)
        {
            Vector3 l = pinv * (shown - _mPos);
            return _mPos + _mRot * (new Vector3(l.x / _mScale.x, l.y / _mScale.y, l.z / _mScale.z) - _mOff);
        }

        // Everything this mod needs the first time you hold a weapon is made at game start instead: the weapon name tables
        // (51 names worked out from the list of weapons) and the machine code of its own methods. Measured before: 2.2 ms in
        // the frame of the first weapon of a session, in the middle of a take.
        private static void Ready()
        {
            try
            {
                Weapons.Touch();
                const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (Type t in new[] { typeof(ViewmodelTweaksPlugin), typeof(Weapons) })
                    foreach (MethodInfo m in t.GetMethods(all))
                    {
                        if (m.IsAbstract || m.ContainsGenericParameters || m.GetMethodBody() == null) continue;
                        try { m.MethodHandle.GetFunctionPointer(); } catch (Exception) { }   // then it is made when first used
                    }
            }
            catch (Exception e) { Log.LogWarning("Getting ready early failed, it happens at the first weapon instead: " + e.Message); }
        }

        // Minimal Viewmodels Reborn does the same job with the same camera: the two cannot both run. If it is still installed
        // (everyone who had this mod before 2.0.0 has it), its hooks are taken out here, after every mod has loaded, and the log
        // says so. Its settings were read once (View.TakeOver). Nothing of it is changed on disk.
        private void Start()
        {
            const string mvr = "kestrel.straftat.minimalviewmodels";
            try
            {
                if (!Chainloader.PluginInfos.ContainsKey(mvr)) return;
                Harmony.UnpatchID(mvr);
                Log.LogWarning("Minimal Viewmodels Reborn is still installed. ViewmodelTweaks 2.0 does its job now and has switched its hooks off for this session; your settings were taken over. You can remove Minimal Viewmodels Reborn.");
            }
            catch (Exception e) { Log.LogWarning("Minimal Viewmodels Reborn is installed and could not be switched off (" + e.Message + "): remove it, the two cannot both run."); }
        }

        private void Awake()
        {
            Log = Logger;
#if !DORSTH_BUNDLE
            // in MONOLITH only MONOLITH says it loaded, so the line shows once
            Log.LogInfo(Name + " " + Version + " loaded. Dorsth was here.");
#endif
            Detail = Config.Bind("Internal", "Detailed Log", false, "Writes what One Camera does to the log (for measuring). Off: only problems are written.");
            for (int i = 1; i <= MaxSlots; i++)
            {
                string s = "Weapon " + i;
                Slots.Add(new Slot
                {
                    Weapon = Config.Bind(s, "Weapon", Weapon.None, "The weapon these settings are for. None: not used."),
                    NoArms = Config.Bind(s, "Invisible Arms", false, "Hides your arms while holding it."),
                    Fov = Config.Bind(s, "Viewmodel FOV", 75f, new ConfigDescription("FOV of the viewmodel while holding it.", new AcceptableValueRange<float>(30f, 120f))),
                    X = Config.Bind(s, "Viewmodel X Offset", 0f, new ConfigDescription("Left (negative) or right (positive).", new AcceptableValueRange<float>(-5f, 5f))),
                    Y = Config.Bind(s, "Viewmodel Y Offset", -0.1f, new ConfigDescription("Down (negative) or up (positive).", new AcceptableValueRange<float>(-5f, 5f))),
                    Z = Config.Bind(s, "Viewmodel Z Offset", 0f, new ConfigDescription("Back (negative) or forward (positive).", new AcceptableValueRange<float>(-5f, 5f))),
                });
            }
            View.Bind(Config);
            Hide(View.TakeOver(Config, Paths.ConfigPath));
            OneCamera = Config.Bind("Viewmodel", "One Camera", false, "Your weapon and arms are drawn by the main camera and the game's second camera is switched off; they look the same (viewmodel FOV, offset, never inside a wall). Having the second camera on costs about 4% of the frame rate. The loose effects of your own weapon (muzzle effect, ejected case or pin, a launcher's smoke) can only be drawn by the second camera: see the next setting. Your shots, hits and what other players see are not changed.");
            HideEffects = Config.Bind("Viewmodel", "One Camera Hides Weapon Effects", true, "Only matters with One Camera on. On (default): the loose effects of your own weapon (muzzle effect, ejected case or pin, a launcher's smoke at the gun) are not drawn, so the second camera never has to come on. Their sounds and the muzzle light stay. Off: they are drawn as always, by switching the second camera back on for the frames in which one is showing; in those frames One Camera gains nothing. Effects of other players, explosions, smoke grenades and everything else in the world are never hidden by this.");
            // under Experimental until 2.0.3
            FromOldName(new ConfigDefinition("Experimental", "One Camera"), OneCamera);
            FromOldName(new ConfigDefinition("Experimental", "One Camera Hides Weapon Effects"), HideEffects);
            // switched at once, not on the next frame: whoever flips the setting (the benchmark does) can rely on the cameras being as it says
            OneCamera.SettingChanged += (s, e) => { try { SyncOneCamera(); } catch (Exception x) { Log.LogWarning("One Camera: " + x.Message); } };
            // a changed setting is on the camera and the arms at once; a weapon's own values go back on top the next frame
            Config.SettingChanged += (s, e) =>
            {
                try { View.Apply(); } catch (Exception x) { Log.LogWarning(x.Message); }
                Changed();
            };
            View.Install(new Harmony(Guid));
            // your own bullet trails, while they are made (every gun has its own trail method)
            try
            {
                var h = new Harmony(Guid + ".onecamera");
                var pre = new HarmonyMethod(typeof(ViewmodelTweaksPlugin), nameof(TrailPrefix)) { priority = Priority.First };
                var fin = new HarmonyMethod(typeof(ViewmodelTweaksPlugin), nameof(TrailFinalizer));
                foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(global::Weapon).Assembly))
                {
                    if (!typeof(global::Weapon).IsAssignableFrom(type)) continue;
                    foreach (MethodInfo m in AccessTools.GetDeclaredMethods(type))
                    {
                        if (!m.Name.StartsWith("RpcLogic___SpawnBulletTrail_", StringComparison.Ordinal)) continue;
                        try { h.Patch(m, prefix: pre, finalizer: fin); }
                        catch (Exception x) { Log.LogWarning("The bullet trail of " + type.Name + " will not line up with the weapon: " + x.Message); }
                    }
                }
                // every weapon that appears (yours or not): the prefabs of its loose effects get their marker
                MethodInfo awake = AccessTools.DeclaredMethod(typeof(global::Weapon), "Awake");
                if (awake != null) h.Patch(awake, postfix: new HarmonyMethod(typeof(ViewmodelTweaksPlugin), nameof(WeaponAwakePostfix)));
                // a grenade's pin comes off the weapon and flies on its own (One Camera has to know it)
                h.Patch(AccessTools.Method(typeof(DualLauncher), "Update"), postfix: new HarmonyMethod(typeof(ViewmodelTweaksPlugin), nameof(PinPostfix)));
            }
            catch (Exception e) { Log.LogWarning("Your bullet trails will not line up with the weapon: " + e.Message); }
#if !DORSTH_BUNDLE
            // in Mod Menu: a list of the weapons you added, each with its settings, and a button to add one
            if (Chainloader.PluginInfos.ContainsKey("kestrel.straftat.modmenu"))
            {
                try { Menu.Register(); }
                catch (Exception e) { Log.LogWarning("Mod Menu page left as a plain list: " + e.Message); }
            }
#endif
            Ready();
            // BepInEx's manager object (every plugin's home) is a plain object the game destroys on the way into a lobby, unless something hides it (Mod Menu does): hidden here too, so this works without Mod Menu.
            gameObject.hideFlags = HideFlags.HideAndDontSave;
        }

        // a moved setting keeps its value: BepInEx keeps the line under the old name until something takes it
        private void FromOldName(ConfigDefinition old, ConfigEntry<bool> now)
        {
            try
            {
                var orphans = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(Config, null) as Dictionary<ConfigDefinition, string>;
                string v; bool b;
                if (orphans == null || !orphans.TryGetValue(old, out v)) return;
                if (bool.TryParse(v, out b)) now.Value = b;
                orphans.Remove(old); Config.Save();
            }
            catch (Exception e) { WarnOnce("Moving a setting to its new name: " + e.Message); }
        }

        private static void Hide(params ConfigEntryBase[] entries)
        {
            if (Chainloader.PluginInfos.ContainsKey("kestrel.straftat.modmenu")) Menu.HideOld(entries);
        }

        internal static void Applied() { _dirty = true; _shown = null; }

        private void LateUpdate()
        {
            // never left posed past a frame (the camera was switched off while it drew, an error in another mod's hook)
            if (_posed) Unpose();
            // another mod (or the diagnostics) put Unity's frame loop back without this mod's steps: in again, a few times at most
            if (_oneOn && _loopIn && _poseFrame >= Time.frameCount - 1) _loopTries = 0;
            else if (_oneOn && _loopIn)
            {
                if (++_loopTries > 5) { _loopIn = false; _loopBroken = true; Log.LogWarning("One Camera: this mod's steps keep being taken out of Unity's frame loop. Your weapon is still drawn right, your arms can be the wrong size."); }
                else LoopSet(true);
            }
            try
            {
                Slot held = Held();
                TrackHeld();
                if (held == _shown && !_dirty) return;
                _dirty = false;
                if (held != null) Show(held);
                else if (_shown != null) { _shown = null; View.Apply(); _dirty = false; }
                SyncOneCamera();
                // SyncOneCamera can call View.Apply (switching off), which marks everything changed again:
                // one more pass next frame then puts a weapon's own settings back on the second camera
            }
            catch (Exception e) { Log.LogWarning(e.Message); _dirty = false; }
            Menu.Keep();
        }

        private void OnDisable() { if (_posed) Unpose(); if (_loopIn) LoopSet(false); }

        // this weapon's place and FOV on the second camera
        private static void Show(Slot s)
        {
            Camera cam = View.WeaponCam;
            if (cam == null || View.NoWeapon.Value) return;
            // (also under One Camera: the second camera's place shifts the end of your bullet trails, and it draws again
            // whenever a loose effect of a weapon shows)
            View.Place(cam, s.Fov.Value, new Vector3(s.X.Value, s.Y.Value, s.Z.Value));
            foreach (SkinnedMeshRenderer r in View.Arms) if (r != null) r.enabled = !s.NoArms.Value;
            _shown = s;
        }

        // the weapon you added that's in your right hand, or else your left
        private static Slot Held()
        {
            if (_me == null)
            {
                // none yet, or the last one is gone (a new round): your own player, as the game itself keeps it
                Settings st = Settings.Instance;
                FirstPersonController mine = st != null ? st.localPlayer : null;
                if (mine == null) return null;
                PlayerPickup p = mine.playerPickupScript != null ? mine.playerPickupScript : mine.GetComponent<PlayerPickup>();
                if (p == null || !p.IsOwner) return null;
                _me = p;
            }
            return For(_me.objInHand) ?? For(_me.objInLeftHand);
        }

        // Which of your weapons an object is, remembered per object: both hands are asked every frame, and working it out
        // again each time (the object's name, cleaned up and compared) made about 2 KB of garbage a frame.
        private static readonly Dictionary<int, Slot> SlotOf = new Dictionary<int, Slot>();

        private static Slot For(GameObject held)
        {
            if (held == null) return null;
            int id = held.GetInstanceID();
            if (SlotOf.TryGetValue(id, out Slot found)) return found;
            MarkEffects(held);      // its effect prefabs report their copies (for One Camera, whenever it is switched on)
            Weapon w = Weapons.Of(held);
            found = null;
            if (w != Weapon.None)
                for (int i = 0; i < Slots.Count; i++) if (Slots[i].Weapon.Value == w) { found = Slots[i]; break; }
            if (SlotOf.Count > 256) SlotOf.Clear();   // objects from earlier rounds
            SlotOf[id] = found;
            return found;
        }

        internal static void Changed() { _dirty = true; SlotOf.Clear(); }
    }

    /// <summary>Sits on the line a gun's bullet trails are copied from; a copy tells One Camera it exists. Does nothing else.</summary>
    internal sealed class OwnTrail : MonoBehaviour
    {
        private void Awake() { ViewmodelTweaksPlugin.TrailBorn(this); }
    }

    /// <summary>Sits on the prefab of a weapon's loose effect (muzzle effect, case, smoke); a copy tells One Camera it exists. Does nothing else.</summary>
    internal sealed class OwnLoose : MonoBehaviour
    {
        internal ParticleSystem Root;
        internal bool Own;
        private void Awake() { ViewmodelTweaksPlugin.LooseBorn(this); }
    }

    internal static class Weapons
    {
        /// Builds the name tables now (they are built the first time this class is used; without this that is your first weapon in a match).
        internal static void Touch() { FromName("pistol"); }

        // a few weapons go by another name in the game
        private static readonly Dictionary<string, Weapon> Aliases = new Dictionary<string, Weapon>(StringComparer.OrdinalIgnoreCase)
        {
            { "sword", Weapon.Flamberge }, { "pistol", Weapon.Gun }, { "ak", Weapon.AK_K }, { "ak-k", Weapon.AK_K }, { "ar", Weapon.AR15 }, { "torrent", Weapon.DF_Torrent },
            { "golden impetus", Weapon.Impetus }, { "smith carbine", Weapon.SmithCarbine }, { "sawed off", Weapon.SawedOff },
            { "beam load", Weapon.BeamLoad }, { "hand canon", Weapon.HandCanon }, { "blank state", Weapon.BlankState },
            { "dual launcher", Weapon.DualLauncher }, { "glaive gun", Weapon.GlaiveGun }, { "rocket launcher", Weapon.RocketLauncher },
            { "gamma gen 2", Weapon.GammaGen2 }, { "hk caws", Weapon.HK_Caws }, { "hill h15", Weapon.Hill_H15 },
        };

        // every weapon by its own name, made once
        private static readonly Dictionary<string, Weapon> ByName = Names();
        private static Dictionary<string, Weapon> Names()
        {
            var d = new Dictionary<string, Weapon>(StringComparer.OrdinalIgnoreCase);
            foreach (Weapon w in Enum.GetValues(typeof(Weapon))) if (w != Weapon.None) d[w.ToString()] = w;
            return d;
        }

        internal static string Label(Weapon w) => w == Weapon.AK_K ? "AK-K" : w == Weapon.Gun ? "pistol" : w.ToString().Replace('_', ' ');

        internal static Weapon FromName(string n)
        {
            if (string.IsNullOrEmpty(n)) return Weapon.None;
            n = n.Replace("(Clone)", "").Trim();
            if (Aliases.TryGetValue(n, out Weapon a)) return a;
            string key = n.Replace("-", "_").Replace(" ", "");
            return ByName.TryGetValue(key, out Weapon w) ? w : Weapon.None;
        }

        // by the object's name, else by the weapon's own name
        internal static Weapon Of(GameObject held)
        {
            Weapon w = FromName(held.name);
            if (w != Weapon.None) return w;
            ItemBehaviour item = held.GetComponent<ItemBehaviour>();
            return item != null ? FromName(item.weaponName) : Weapon.None;
        }
    }
}
