// MONOLITH - keys the game's own settings do not let you set. Dorsth was here.
//
//  * A second and a third jump key, the scroll wheel included. Two, because scroll up and scroll down are two inputs:
//    one on each is how a jump on the whole wheel is set.
//  * Swap weapons: the game's own swap binding (Settings, Controls, Swap), set from here. Both menus show the same key
//    and either one's reset puts it back on the scroll wheel. Scrolling while the row asks for a key does that too.
//  * Taunts: the game has them fixed on the number keys; each can be moved to another key or switched off.
//
// How, and what it leaves alone:
//  * The extra jumps and the taunts leave the game's key set alone: nothing of them ends up in the game's saved
//    bindings and they are gone when MONOLITH is. Each is an input action of its own that calls the SAME routine of the
//    game that its own key calls (FirstPersonController.Jump). Swap weapons is the one exception: it IS the game's
//    binding, changed and saved the way the game's own settings change and save it.
//    A press here is a press, one for one: nothing is repeated or timed for you.
//  * No key is asked for every frame: the input system calls in when one is pressed. A key that is not set costs nothing.
//  * While a jump of yours is on a wheel direction, the game's swap does not hear that direction (a prefix on the routine
//    that hears it), so one notch does one thing: jump on scroll down and swap on the wheel swaps on scroll up.
//  * Taunts: the game asks for the ten number keys every frame. Those questions are answered here: for a taunt you
//    did not change, with the game's own answer.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Monolith
{
    internal static class Keys
    {
        internal const string Off = "none";
        internal static ConfigEntry<string> Jump1, Jump2, Swap;
        internal static readonly ConfigEntry<string>[] Taunt = new ConfigEntry<string>[10];

        private static readonly Dictionary<ConfigEntry<string>, InputAction> Actions = new Dictionary<ConfigEntry<string>, InputAction>();
        private static readonly int[] TauntFrame = { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
        private static bool _jumpUp, _jumpDown;   // a jump of yours on scroll up, on scroll down (the whole wheel is both)
        private static ConfigFile _swapFile;
        private static string _oldSwap;          // a Swap weapons key from before 0.9.74, moved into the game's binding once
        private static bool _swapSync;
        private static int _tauntHits;

        internal static void Bind(ConfigFile c)
        {
            const string S = "Keys";
            Jump1 = c.Bind(S, "Extra Jump", "", "A second jump key, next to your own. A key, a mouse button, scroll up or scroll down.");
            Jump2 = c.Bind(S, "Extra Jump 2", "", "A third jump key. Scroll up on one and scroll down on the other puts jump on the whole wheel.");
            // Swap weapons was a key of MONOLITH's own until 0.9.74. Now it is the game's binding, shown through an entry that
            // is never saved (the game saves the binding); an old value is moved over once.
            ConfigEntry<string> old = c.Bind(S, "Swap Weapons", "", "");
            _oldSwap = old.Value; c.Remove(old.Definition); c.Save();
            _swapFile = new ConfigFile(System.IO.Path.Combine(BepInEx.Paths.CachePath, "MONOLITH.swap.cfg"), false) { SaveOnConfigSet = false };
            Swap = _swapFile.Bind(S, "Swap Weapons", Wheel, "The game's own swap weapons key.");
            for (int i = 0; i < 10; i++)
                Taunt[i] = c.Bind(S, "Taunt " + (i + 1), "", "The taunt the game has on number key " + ((i + 1) % 10) + ". Empty: that key.");
        }

        internal static void Install(Harmony h)
        {
            Swap.SettingChanged += (s, a) => { if (!_swapSync) WriteSwap(Swap.Value); };
            if (!string.IsNullOrEmpty(_oldSwap)) UnityEngine.SceneManagement.SceneManager.sceneLoaded += MoveOldSwap;
            MethodInfo heard = AccessTools.Method(typeof(PlayerPickup), "HandleDrop");
            if (heard != null) h.Patch(heard, prefix: new HarmonyMethod(typeof(Keys).GetMethod(nameof(WheelPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
            MethodInfo taunts = AccessTools.Method(typeof(FirstPersonController), "HandleTaunt");
            if (taunts != null)
            {
                h.Patch(taunts, transpiler: new HarmonyMethod(typeof(Keys).GetMethod(nameof(TauntTr), BindingFlags.Static | BindingFlags.NonPublic)));
                if (_tauntHits != 20) BundlePlugin.Log.LogInfo("Keys: the game asks for a taunt key in " + _tauntHits + " places (20 expected).");
            }
            foreach (ConfigEntry<string> e in All())
            {
                ConfigEntry<string> each = e;
                each.SettingChanged += (s, a) => Listen(each);
                Listen(each);
            }
        }

        private static IEnumerable<ConfigEntry<string>> All()
        {
            yield return Jump1; yield return Jump2;
            foreach (ConfigEntry<string> t in Taunt) yield return t;
        }

        // one input action per key that is set; made again when the key changes
        private static void Listen(ConfigEntry<string> e)
        {
            try
            {
                InputAction old;
                if (Actions.TryGetValue(e, out old)) { old.Disable(); old.Dispose(); Actions.Remove(e); }
                string path = e.Value;
                if (!string.IsNullOrEmpty(path) && path != Off)
                {
                    var a = new InputAction("MONOLITH " + e.Definition.Key, InputActionType.Button, path);
                    int taunt = Array.IndexOf(Taunt, e);
                    if (taunt >= 0) a.performed += ctx => { TauntFrame[taunt] = Time.frameCount; };
                    else a.performed += ctx => Jump(ctx);
                    a.Enable();
                    Actions[e] = a;
                }
                _jumpUp = _jumpDown = false;
                foreach (ConfigEntry<string> k in All())
                {
                    if (k.Value == null || k.Value.IndexOf("/scroll", StringComparison.Ordinal) < 0) continue;
                    bool up = k.Value.EndsWith("/up", StringComparison.Ordinal), down = k.Value.EndsWith("/down", StringComparison.Ordinal);
                    if (up || !down) _jumpUp = true;     // the whole wheel takes both
                    if (down || !up) _jumpDown = true;
                }
            }
            catch (Exception x) { BundlePlugin.Log.LogWarning("Keys: " + e.Definition.Key + " could not be set to " + e.Value + ": " + x.Message); }
        }

        // a press of an extra jump key: the game's own jump, under the game's own conditions
        private static void Jump(InputAction.CallbackContext ctx)
        {
            try
            {
                if (BundleMenu.Opened) return;
                PauseManager pm = PauseManager.Instance;
                if (pm != null && (pm.pause || pm.chatting)) return;
                Settings st = Settings.Instance;
                FirstPersonController me = st != null ? st.localPlayer : null;
                if (me == null || !me.isActiveAndEnabled) return;
                me.Jump(ctx);
            }
            catch (Exception) { }
        }

        // While a jump of yours is on a wheel direction, the game's swap does not hear that direction. The other one still
        // swaps: a jump on scroll down leaves scroll up to swap.
        private static bool WheelPrefix(InputAction.CallbackContext __0)
        {
            if (!_jumpUp && !_jumpDown) return true;
            InputControl c = __0.control;
            if (c == null || !(c.device is Mouse) || c.path.IndexOf("/scroll", StringComparison.Ordinal) < 0) return true;
            bool up;
            if (c.path.EndsWith("/up", StringComparison.Ordinal)) up = true;
            else if (c.path.EndsWith("/down", StringComparison.Ordinal)) up = false;
            else { float y = 0f; try { y = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f; } catch { } if (y == 0f) return true; up = y > 0f; }
            return up ? !_jumpUp : !_jumpDown;
        }

        internal const string Wheel = "<Mouse>/scroll";

        // the game's swap action and the index of its keyboard and mouse binding (the other one is the gamepad's)
        private static InputAction SwapAction(out int index)
        {
            index = -1;
            if (InputManager.inputActions == null) return null;
            InputAction a = InputManager.inputActions.Player.ChangeWeapon;
            for (int i = 0; i < a.bindings.Count; i++)
            {
                InputBinding b = a.bindings[i];
                if (!b.isComposite && !b.isPartOfComposite && (b.path ?? "").IndexOf("Gamepad", StringComparison.Ordinal) < 0) { index = i; return a; }
            }
            return null;
        }

        /// <summary>Reads the game's swap binding into the Swap weapons row (it may have been changed in the game's own
        /// settings). Called while the menu shows the row; always true.</summary>
        internal static bool SyncSwap()
        {
            try
            {
                int i; InputAction a = SwapAction(out i);
                if (a == null) return true;
                if (_oldSwap != null) { string o = _oldSwap; _oldSwap = null; if (o.Length > 0) WriteSwap(o); }
                string p = a.bindings[i].effectivePath;
                if (p != Swap.Value) { _swapSync = true; try { Swap.Value = p; } finally { _swapSync = false; } }
            }
            catch (Exception) { }
            return true;
        }

        // the old Swap weapons key goes into the game's binding as soon as the game has its bindings (by the first scene)
        private static void MoveOldSwap(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            SyncSwap();
            if (_oldSwap == null) UnityEngine.SceneManagement.SceneManager.sceneLoaded -= MoveOldSwap;
        }

        // Sets the game's binding and saves it as the game does (InputManager.SaveBindingOverride: one PlayerPrefs value
        // per binding, read back at start). The game's default (the wheel) is no override at all.
        private static void WriteSwap(string path)
        {
            try
            {
                int i; InputAction a = SwapAction(out i);
                if (a == null) return;
                if (string.IsNullOrEmpty(path) || path == a.bindings[i].path) a.RemoveBindingOverride(i);
                else a.ApplyBindingOverride(i, path);
                PlayerPrefs.SetString((a.actionMap != null ? a.actionMap.ToString() : null) + a.name + i, a.bindings[i].overridePath ?? "");
                PlayerPrefs.Save();
            }
            catch (Exception x) { BundlePlugin.Log.LogWarning("Keys: Swap weapons could not be set to " + path + ": " + x.Message); }
        }

        /// <summary>The value a key just pressed gives a row. On Swap weapons either wheel direction means the whole wheel.</summary>
        internal static string Taken(ConfigEntryBase row, string got)
        {
            if (row != Swap || got == null || !got.StartsWith(Wheel, StringComparison.Ordinal)) return got;
            return Wheel;
        }

        private static IEnumerable<CodeInstruction> TauntTr(IEnumerable<CodeInstruction> ins)
        {
            MethodInfo ask = AccessTools.Method(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(KeyCode) }), mine = AccessTools.Method(typeof(Keys), nameof(TauntDown));
            foreach (CodeInstruction c in ins)
            {
                if (ask != null && c.Calls(ask)) { c.operand = mine; _tauntHits++; }
                yield return c;
            }
        }

        /// <summary>The game's question "was this number key pressed", answered for the taunt that sits on it.</summary>
        internal static bool TauntDown(KeyCode asked)
        {
            int i = asked == KeyCode.Alpha0 ? 9 : asked - KeyCode.Alpha1;
            if (i < 0 || i > 9) return Input.GetKeyDown(asked);
            string set = Taunt[i].Value;
            if (string.IsNullOrEmpty(set)) return Input.GetKeyDown(asked);
            return set != Off && TauntFrame[i] == Time.frameCount;
        }

        // ---- for the menu: what a key reads as, and which key is being pressed right now

        internal static string Name(string path, string empty)
        {
            if (string.IsNullOrEmpty(path)) return empty;
            if (path == Off) return "off";
            if (path == Wheel) return "scroll wheel";
            if (path == "<Mouse>/scroll/up") return "scroll up";
            if (path == "<Mouse>/scroll/down") return "scroll down";
            int slash = path.LastIndexOf('/');
            string n = slash >= 0 ? path.Substring(slash + 1) : path;
            if (path.StartsWith("<Mouse>", StringComparison.Ordinal))
                return n == "middleButton" ? "mouse 3" : n == "backButton" ? "mouse 4" : n == "forwardButton" ? "mouse 5" : n;
            return n;
        }

        internal static bool EnterHeld()
        {
            Keyboard k = Keyboard.current;
            return k != null && (k.enterKey.isPressed || k.numpadEnterKey.isPressed);
        }

        /// <summary>The key, mouse button or wheel direction that went down this frame, as a path; null when none did.
        /// Left and right mouse button are not taken: they fire and aim.</summary>
        internal static string Pressed()
        {
            try
            {
                Mouse m = Mouse.current;
                if (m != null)
                {
                    if (m.middleButton.wasPressedThisFrame) return "<Mouse>/middleButton";
                    if (m.backButton.wasPressedThisFrame) return "<Mouse>/backButton";
                    if (m.forwardButton.wasPressedThisFrame) return "<Mouse>/forwardButton";
                    float y = m.scroll.ReadValue().y;
                    if (y > 0f) return "<Mouse>/scroll/up";
                    if (y < 0f) return "<Mouse>/scroll/down";
                }
                Keyboard k = Keyboard.current;
                if (k != null && k.anyKey.wasPressedThisFrame)
                    foreach (KeyControl key in k.allKeys)
                        if (key != null && key.wasPressedThisFrame) return "<Keyboard>/" + key.name;   // (Enter: the menu reads that as "no key")
            }
            catch (Exception) { }
            return null;
        }
    }
}
