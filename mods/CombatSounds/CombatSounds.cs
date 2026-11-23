// CombatSounds - a STRAFTAT mod (BepInEx 5). Dorsth was here.
//
// Pick your own hit, head hit and kill sounds from the sound folders next to the mod, and set how loud guns are.
// Client-side: only you hear it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ComputerysModdingUtilities;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

[assembly: AssemblyTitle("CombatSounds")]
[assembly: AssemblyDescription("Your own hit, head hit, kill and melee kill sounds, and gun volume.")]
[assembly: AssemblyVersion("1.1.9.0")]

// Only changes what you hear, so matchmaking with unmodded players still works.
[assembly: StraftatMod(isVanillaCompatible: true)]

namespace CombatSounds
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class CombatSoundsPlugin : BaseUnityPlugin
    {
#if DORSTH_BUNDLE
        /// <summary>Set while settings are changed by something other than the user picking a sound: no preview is played.</summary>
        internal static bool Quiet;
#endif
        public const string Guid = "dorsth.straftat.combatsounds";
        public const string Name = "CombatSounds";
        public const string Version = "1.1.9";

        internal static ManualLogSource Log;
        internal static SoundSlot Hit, HeadHit, Kill, MeleeKill;
        internal static ConfigEntry<int> GunVolume;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo(Name + " " + Version + " loaded. Dorsth was here.");
            // the volume table also drops removed guns and casings when a scene loads, not only every 256 new ones
            // (a heap walk found ~135 destroyed sound sources of earlier takes still in it)
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => GunSounds.Prune();

            // the sound folders sit next to the mod (in its own folder, not loose in plugins)
            string root = Path.GetDirectoryName(Info.Location);
            if (string.Equals(Path.GetFullPath(root).TrimEnd('\\', '/'), Path.GetFullPath(Paths.PluginPath).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                root = Path.Combine(root, Name);
#if DORSTH_BUNDLE
            // built into the bundle. The folder CombatSounds had on its own comes first: it is where your own sounds are, and
            // a sound you picked that is not found would be forgotten. Without it: the folder the bundle came with.
            string here = Path.GetDirectoryName(Info.Location);
            bool loose = string.Equals(Path.GetFullPath(here).TrimEnd('\\', '/'), Path.GetFullPath(Paths.PluginPath).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
            string own = Path.Combine(Paths.PluginPath, Name), with = loose ? Path.Combine(here, Path.GetFileNameWithoutExtension(Info.Location)) : here;
            root = with;
            foreach (string c in new[] { own, with, Path.Combine(Paths.PluginPath, "MONOLITH"), Path.Combine(Paths.PluginPath, "DorsthBundle") })
                if (Directory.Exists(Path.Combine(c, "HitSounds")) && Directory.GetFiles(Path.Combine(c, "HitSounds")).Length > 0) { root = c; break; }
            Log.LogInfo("Sounds from " + root);
#endif

            string kills = Path.Combine(root, "KillSounds");
            Hit = new SoundSlot(Config, SoundKind.Hit, "Hit Sound", Path.Combine(root, "HitSounds"), "When you hit a player.");
            HeadHit = new SoundSlot(Config, SoundKind.HeadHit, "Head Hit Sound", Path.Combine(root, "HeadHitSounds"), "When you hit a head.", Hit, "Same as Hit");
            Kill = new SoundSlot(Config, SoundKind.Kill, "Kill Sound", kills, "When you kill a player.");
            MeleeKill = new SoundSlot(Config, SoundKind.MeleeKill, "Melee Kill Sound", Path.Combine(root, "MeleeKillSounds"), "When you kill with a melee weapon.", Kill, "Same as Kill");

            // its own section, so it's listed after the sounds
            GunVolume = Config.Bind("Guns", "Gun Sound Volume", 100, new ConfigDescription(
                "Shots, reloads, empty clicks and casings, yours and others'. 100 is normal.",
                new AcceptableValueRange<int>(0, 200)));

            var harmony = new Harmony(Guid);
            try { Hooks.Install(harmony); }
            catch (Exception e) { Log.LogError("Couldn't hook the game's hit sounds, so the mod is off. (" + e.Message + ")"); }
            try { GunSounds.Install(harmony); }
            catch (Exception e) { Log.LogError("Couldn't hook the gun sounds, so Gun Sound Volume is off. (" + e.Message + ")"); }
        }
    }

    internal enum SoundKind { Hit, HeadHit, Kill, MeleeKill }

    /// <summary>
    /// One sound setting: a sound from its folder, the game's own (Default), none, or another setting's sound (Same as
    /// Hit, Same as Kill), and a volume. The volume works on the game's own sound too.
    /// </summary>
    internal sealed class SoundSlot
    {
        private const string Default = "Default", None = "None";

        private static GameObject _speaker;

        private readonly ConfigEntry<string> _choice;
        private readonly ConfigEntry<int> _volume;
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>();
        private readonly string _name;
        private readonly SoundKind _kind;
        private readonly SoundSlot _hit; // the setting this one can follow (head hit: hit, melee kill: kill)
        private readonly string _same;    // "Same as Hit"
        private AudioSource _source;
        private AudioClip _clip, _louder, _louderOf;
        private string _sound;            // the picked sound's name, once loaded
        private bool _none, _following;   // the choice, read once when it changes rather than on every hit
        private int _loadId, _louderVolume;
        private float _nextPreview;

        internal SoundSlot(ConfigFile config, SoundKind kind, string name, string folder, string description, SoundSlot follows = null, string same = null)
        {
            _kind = kind;
            _name = name;
            _hit = follows;
            _same = same;
            // Default first in every list; one that can follow another setting starts on that (Same as Hit)
            var choices = new List<string> { Default, None };
            if (follows != null) choices.Add(same);
            try
            {
                Directory.CreateDirectory(folder);
                string[] files = Directory.GetFiles(folder);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    string sound = Path.GetFileNameWithoutExtension(file);
                    if ((ext != ".wav" && ext != ".ogg" && ext != ".mp3") || choices.Contains(sound)) continue;
                    choices.Add(sound);
                    _files[sound] = file;
                }
            }
            catch (Exception e) { CombatSoundsPlugin.Log.LogWarning("Couldn't read " + folder + ": " + e.Message); }

            string shown = folder.StartsWith(Paths.GameRootPath, StringComparison.OrdinalIgnoreCase) ? folder.Substring(Paths.GameRootPath.Length).TrimStart('\\', '/') : folder;
            _choice = config.Bind("Sounds", name, follows != null ? same : Default, new ConfigDescription(
                description + " Default is the game's own. Add your own sounds in " + shown + ".",
                new AcceptableValueList<string>(choices.ToArray())));
            _volume = config.Bind("Sounds", name + " Volume", 100, new ConfigDescription(
                "100 is normal, 200 is twice as loud.", new AcceptableValueRange<int>(0, 200)));

#if DORSTH_BUNDLE
            // a profile being loaded changes many settings at once: nothing is played for those
            _choice.SettingChanged += (s, e) => Load(!CombatSoundsPlugin.Quiet);
#else
            _choice.SettingChanged += (s, e) => Load(true);
#endif
            _volume.SettingChanged += (s, e) =>
            {
#if DORSTH_BUNDLE
                if (CombatSoundsPlugin.Quiet) return;
#endif
                if (Time.unscaledTime < _nextPreview) return; // the slider fires many changes: preview a few times a second
                _nextPreview = Time.unscaledTime + 0.3f;
                Preview();
            };
            Load(false);
        }

        /// <summary>Where the sound comes from: the followed setting's pick on Same as Hit or Same as Kill (the volume stays this one's).</summary>
        private SoundSlot Pick { get { return _following ? _hit.Pick : this; } }

        /// <summary>
        /// The game's own sound for this setting, at this setting's volume: true when it's played here, false when the
        /// game should play it as it is (at 100).
        /// </summary>
        internal bool PlayGame(AudioSource audio, AudioClip clip)
        {
            int volume = _volume.Value;
            if (volume == 100 || audio == null || clip == null) return false;
            if (volume > 0) audio.PlayOneShot(clip, volume / 100f);
            return true;
        }

        // the game's own sound for previews, found once on a weapon that has it
        private AudioClip _gameA, _gameB;

        // what you hear when you pick a sound or move the volume: the game's own sound too
        private void Preview()
        {
            if (Pick._clip != null) { Play(); return; }
            if (Pick._none) return;
            if (_gameA == null)
            {
                foreach (Weapon w in Resources.FindObjectsOfTypeAll<Weapon>())
                {
                    if (w == null || (_kind == SoundKind.MeleeKill) != (w is MeleeWeapon)) continue;
                    _gameA = _kind == SoundKind.HeadHit ? w.headHitClip : _kind == SoundKind.Hit ? w.bodyHitClip : w.deathClip;
                    _gameB = _kind == SoundKind.HeadHit ? w.bodyHitClip : null;
                    if (_gameA != null) break;
                }
            }
            AudioClip a = _gameA, b = _gameB;
            if (a == null) return;
            if (_source == null) _source = NewSource();
            float v = Mathf.Min(_volume.Value / 100f, 1f);
            _source.Stop();
            _source.PlayOneShot(a, v);
            if (b != null) _source.PlayOneShot(b, v);
        }

        /// <summary>Whether the game's own sound is muted for this slot: a sound of yours is picked, or None.</summary>
        internal bool ReplacesGameSound { get { SoundSlot pick = Pick; return pick._clip != null || pick._none; } }

        internal bool HasSound { get { return Pick._clip != null; } }

        /// <summary>Whether two settings play the same sound (by name, so the same file in two folders counts).</summary>
        internal bool SameSound(SoundSlot other)
        {
            return other != null && string.Equals(Pick._sound, other.Pick._sound, StringComparison.OrdinalIgnoreCase);
        }

        internal void Play()
        {
            AudioClip clip = Pick._clip;
            if (clip == null) return;
            if (_source == null) _source = NewSource();
            int volume = _volume.Value;
            _source.clip = volume > 100 ? Louder(clip, volume) : clip;
            _source.volume = volume > 100 ? 1f : volume / 100f;
            _source.Play();
        }

        internal void Stop()
        {
            if (_source != null) _source.Stop();
        }

        private static AudioSource NewSource()
        {
            if (_speaker == null)
            {
                _speaker = new GameObject("CombatSounds") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(_speaker);
            }
            AudioSource source = _speaker.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.priority = 0;
            source.bypassEffects = source.bypassListenerEffects = source.bypassReverbZones = true;
            return source;
        }

        /// <summary>
        /// Above 100 an AudioSource can't go louder, so it plays a louder copy of the sound. The copy is made when it's first
        /// played at a new volume (dragging the slider doesn't rebuild it on every step). If it can't be made, 100 is used.
        /// </summary>
        private AudioClip Louder(AudioClip clip, int volume)
        {
            if (_louderVolume == volume && _louderOf == clip) return _louder ?? clip;
            _louderVolume = volume;
            _louderOf = clip;
            DropLouder();
            try
            {
                var data = new float[clip.samples * clip.channels];
                if (!clip.GetData(data, 0)) return clip;
                float gain = volume / 100f;
                for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i] * gain, -1f, 1f);
                _louder = AudioClip.Create(clip.name + " (louder)", clip.samples, clip.channels, clip.frequency, false);
                _louder.SetData(data, 0);
                return _louder;
            }
            catch (Exception e)
            {
                CombatSoundsPlugin.Log.LogWarning("Couldn't make " + clip.name + " louder, so it plays at 100. (" + e.Message + ")");
                DropLouder();
                return clip;
            }
        }

        private void DropLouder()
        {
            if (_louder != null) UnityEngine.Object.Destroy(_louder);
            _louder = null;
        }

        private void Load(bool preview)
        {
            int id = ++_loadId;
            string choice = _choice.Value;
            _none = choice == None;
            _following = _hit != null && choice == _same;
            if (_clip != null) UnityEngine.Object.Destroy(_clip);
            _clip = null;
            _sound = null;
            DropLouder();
            _louderVolume = 0;

            string file;
            if (!_files.TryGetValue(choice, out file))
            {
                if (preview) Preview(); // what it will sound like
                return;
            }
            string ext = Path.GetExtension(file).ToLowerInvariant();
            AudioType type = ext == ".ogg" ? AudioType.OGGVORBIS : ext == ".mp3" ? AudioType.MPEG : AudioType.WAV;
            UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(new Uri(file), type);
            request.SendWebRequest().completed += op =>
            {
                try
                {
                    AudioClip clip = string.IsNullOrEmpty(request.error) ? DownloadHandlerAudioClip.GetContent(request) : null;
                    if (id != _loadId) // another sound was picked meanwhile
                    {
                        if (clip != null) UnityEngine.Object.Destroy(clip);
                        return;
                    }
                    if (clip == null || clip.samples == 0)
                    {
                        CombatSoundsPlugin.Log.LogWarning("Couldn't load " + Path.GetFileName(file) + " (" + (request.error ?? "not a sound Unity can play") + "), so the game's own " + _name.ToLowerInvariant() + " plays.");
                        return;
                    }
                    clip.name = choice;
                    _clip = clip;
                    _sound = choice;
                    if (preview) Preview();
                }
                catch (Exception e) { CombatSoundsPlugin.Log.LogWarning("Couldn't load " + Path.GetFileName(file) + ": " + e.Message); }
                finally { request.Dispose(); }
            };
        }
    }

    /// <summary>
    /// The game counts your body shots, headshots and kills in Settings: that's when your sounds play. A shot and its kill
    /// are counted in the same frame, so the kill sound starts in sync on top of the hit sound (or plays once, if it's the
    /// same sound). One hit sound per frame, a head hit over a hit (a shotgun's pellets all land in the same frame).
    /// The game's own sounds come from each weapon's LocalSound: 0 = head layer, 1 = hit, 2 = kill. A headshot plays
    /// 0 then 1, so that 1 belongs to the headshot.
    /// </summary>
    internal static class Hooks
    {
        private static int _frame = -1;
        private static SoundSlot _hit, _kill; // what played this frame
        private static int _headLayerFrame = -1, _meleeKillFrame = -1;
        private static bool _errorLogged;

        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Settings), "IncreaseBodyshotsAmount"), null, new HarmonyMethod(typeof(Hooks), "HitPostfix"));
            harmony.Patch(AccessTools.Method(typeof(Settings), "IncreaseHeadshotsAmount"), null, new HarmonyMethod(typeof(Hooks), "HeadHitPostfix"));
            harmony.Patch(AccessTools.Method(typeof(Settings), "IncreaseKillsAmount"), null, new HarmonyMethod(typeof(Hooks), "KillPostfix"));

            var gameSound = new HarmonyMethod(typeof(Hooks), "GameSoundPrefix");
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(Weapon).Assembly))
            {
                if (!typeof(Weapon).IsAssignableFrom(type)) continue;
                // not every weapon has its own: asked quietly (AccessTools writes a warning for every one that does not)
                MethodInfo localSound = type.GetMethod("LocalSound", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, new[] { typeof(int) }, null);
                if (localSound != null) harmony.Patch(localSound, gameSound);
            }
        }

        private static void HitPostfix() { Play(CombatSoundsPlugin.Hit, false); }

        private static void HeadHitPostfix() { Play(CombatSoundsPlugin.HeadHit, false); }

        // a melee weapon plays its kill sound just before the kill is counted, in the same frame
        private static void KillPostfix()
        {
            Play(_meleeKillFrame == Time.frameCount ? CombatSoundsPlugin.MeleeKill : CombatSoundsPlugin.Kill, true);
        }

        private static void Play(SoundSlot slot, bool kill)
        {
            try
            {
                int frame = Time.frameCount;
                if (frame != _frame)
                {
                    _frame = frame;
                    _hit = _kill = null;
                }
                if (kill)
                {
                    if (_kill != null) return;
                    _kill = slot;
                    if (slot.HasSound && !slot.SameSound(_hit)) slot.Play();
                    return;
                }
                // one hit sound per frame, a head hit over a hit (also when the head hit is the game's own)
                if (_hit == slot || _hit == CombatSoundsPlugin.HeadHit) return;
                if (_hit != null) _hit.Stop();
                _hit = slot;
                if (slot.HasSound && !slot.SameSound(_kill)) slot.Play();
            }
            catch (Exception e) { LogOnce(e); }
        }

        private static void LogOnce(Exception e)
        {
            if (_errorLogged) return;
            _errorLogged = true;
            CombatSoundsPlugin.Log.LogError("Error (further errors are not logged): " + e);
        }

        /// <summary>
        /// The game's own sound: muted where a sound of yours (or None) is picked, otherwise played at the setting's
        /// volume.
        /// </summary>
        private static bool GameSoundPrefix(Weapon __instance, int __0)
        {
            SoundSlot slot;
            AudioClip clip;
            switch (__0)
            {
                case 0:
                    _headLayerFrame = Time.frameCount;
                    slot = CombatSoundsPlugin.HeadHit;
                    clip = __instance != null ? __instance.headHitClip : null;
                    break;
                case 1:
                    bool head = _headLayerFrame == Time.frameCount;
                    _headLayerFrame = -1;
                    slot = head ? CombatSoundsPlugin.HeadHit : CombatSoundsPlugin.Hit;
                    clip = __instance != null ? __instance.bodyHitClip : null;
                    break;
                case 2:
                    bool melee = __instance is MeleeWeapon;
                    if (melee) _meleeKillFrame = Time.frameCount;
                    slot = melee ? CombatSoundsPlugin.MeleeKill : CombatSoundsPlugin.Kill;
                    clip = __instance != null ? __instance.deathClip : null;
                    break;
                default:
                    return true;
            }
            try
            {
                if (slot.ReplacesGameSound) return false;
                return !slot.PlayGame(__instance != null ? __instance.audio : null, clip);
            }
            catch (Exception e) { LogOnce(e); return true; }
        }
    }

    /// <summary>
    /// Gun Sound Volume. Every gun plays its sounds (shots, reloads, empty clicks) from one sound source of its own, and
    /// every bullet casing from its own. Those are noted as guns and casings are made, and their volume is set to the
    /// game's effects volume times this setting. The game keeps resetting some of them to its effects volume every
    /// frame (SetEffectVolume, SetPlayerVolume), so for the noted ones that's done with this setting included.
    /// </summary>
    internal static class GunSounds
    {
        private static readonly Dictionary<AudioSource, float> Noted = new Dictionary<AudioSource, float>();
        private static AccessTools.FieldRef<SetEffectVolume, AudioSource> _effectFrom, _effectTo;
        private static AccessTools.FieldRef<SetPlayerVolume, AudioSource> _playerFrom, _playerTo;
        private static bool _errorLogged;

        private static float Scale { get { return CombatSoundsPlugin.GunVolume.Value / 100f; } }

        internal static void Install(Harmony harmony)
        {
            _effectFrom = AccessTools.FieldRefAccess<SetEffectVolume, AudioSource>("source");
            _effectTo = AccessTools.FieldRefAccess<SetEffectVolume, AudioSource>("audio");
            _playerFrom = AccessTools.FieldRefAccess<SetPlayerVolume, AudioSource>("source");
            _playerTo = AccessTools.FieldRefAccess<SetPlayerVolume, AudioSource>("audio");

            // every gun sets up its sound source when it's made (some gun types do it in their own version of that step)
            var gunMade = new HarmonyMethod(typeof(GunSounds), "GunMadePostfix");
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(Weapon).Assembly))
            {
                if (!typeof(Weapon).IsAssignableFrom(type)) continue;
                MethodInfo awake = AccessTools.DeclaredMethod(type, "Awake___UserLogic");
                if (awake != null) harmony.Patch(awake, null, gunMade);
            }
            harmony.Patch(AccessTools.Method(typeof(EjectedCaseScript), "Awake"), null, new HarmonyMethod(typeof(GunSounds), "CasingMadePostfix"));
            harmony.Patch(AccessTools.Method(typeof(SetEffectVolume), "Update"), new HarmonyMethod(typeof(GunSounds), "EffectVolumePrefix"));
            harmony.Patch(AccessTools.Method(typeof(SetPlayerVolume), "Update"), new HarmonyMethod(typeof(GunSounds), "PlayerVolumePrefix"));
            CombatSoundsPlugin.GunVolume.SettingChanged += (s, e) => ApplyAll();
        }

        private static void GunMadePostfix(Weapon __instance)
        {
            try { if (__instance != null) Note(__instance.audio); }
            catch (Exception e) { LogOnce(e); }
        }

        private static void CasingMadePostfix(EjectedCaseScript __instance)
        {
            try { if (__instance != null) Note(__instance.GetComponent<AudioSource>()); }
            catch (Exception e) { LogOnce(e); }
        }

        private static int _pruneAt = 256;
        private static readonly List<AudioSource> Gone = new List<AudioSource>();

        private static void Note(AudioSource source)
        {
            if (source == null || Noted.ContainsKey(source)) return;
            // removed guns and casings are dropped now and then, not on every new one
            if (Noted.Count >= _pruneAt)
            {
                Prune();
                _pruneAt = Mathf.Max(256, Noted.Count * 2);
            }
            float own = source.volume;
            Noted[source] = own;
            source.volume = own * Scale;
        }

        internal static void Prune()
        {
            foreach (AudioSource s in Noted.Keys) if (s == null) Gone.Add(s);
            foreach (AudioSource s in Gone) Noted.Remove(s);
            Gone.Clear();
        }

        private static void ApplyAll()
        {
            try
            {
                Prune();
                float scale = Scale;
                foreach (var kv in Noted) kv.Key.volume = kv.Value * scale;
            }
            catch (Exception e) { LogOnce(e); }
        }

        // a noted gun or casing: the effects volume with this setting on top (and the original isn't run)
        private static bool EffectVolumePrefix(SetEffectVolume __instance)
        {
            AudioSource to = _effectTo(__instance), from = _effectFrom(__instance);
            return Follow(to, from);
        }

        private static bool PlayerVolumePrefix(SetPlayerVolume __instance)
        {
            AudioSource to = _playerTo(__instance), from = _playerFrom(__instance);
            return Follow(to, from);
        }

        private static bool Follow(AudioSource to, AudioSource from)
        {
            if (to == null || from == null || !Noted.ContainsKey(to)) return true;
            Noted[to] = from.volume;
            float want = from.volume * Scale;
            if (to.volume != want) to.volume = want;
            return false;
        }

        private static void LogOnce(Exception e)
        {
            if (_errorLogged) return;
            _errorLogged = true;
            CombatSoundsPlugin.Log.LogError("Gun sound volume error (further errors are not logged): " + e);
        }
    }
}
