// MONOLITH - keys the game's own settings do not let you set. Dorsth was here.
//
//  * Extra jump (two of them): a second and third way to jump, the scroll wheel included.
//  * Swap weapons: the game only swaps on the scroll wheel; this gives it a key or a mouse button as well.
//  * Taunts: the game has them fixed on the number keys; each can be moved to another key or switched off.
//
// How, and what it leaves alone:
//  * The game's key set is not touched: nothing is added to it or overridden in it, so nothing of this ends up in the
//    game's saved bindings and it is all gone when MONOLITH is. Each key here is an input action of its own that
//    calls the SAME routine of the game that its own key calls (FirstPersonController.Jump, PlayerPickup.SwitchWeapons).
//    A press here is a press, one for one: nothing is repeated or timed for you.
//  * No key is asked for every frame: the input system calls in when one is pressed. A key that is not set costs nothing.
//  * The scroll wheel swaps weapons in the game. Once you put anything of yours on the wheel, the game's own swap on
//    the wheel is skipped (a prefix on the routine that hears it), so one notch does one thing.
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
        private static Action<PlayerPickup> _switch;
        private static bool _wheelMine;
        private static int _tauntHits;

        internal static void Bind(ConfigFile c)
        {
            const string S = "Keys";
            Jump1 = c.Bind(S, "Extra Jump", "", "Another way to jump, next to your jump key. A key, a mouse button or the scroll wheel.");
            Jump2 = c.Bind(S, "Extra Jump 2", "", "One more, for example scroll up on the first and scroll down on this one.");
            Swap = c.Bind(S, "Swap Weapons", "", "Swaps your two weapons, next to the scroll wheel.");
            for (int i = 0; i < 10; i++)
                Taunt[i] = c.Bind(S, "Taunt " + (i + 1), "", "The taunt the game has on number key " + ((i + 1) % 10) + ". Empty: that key.");
        }

        internal static void Install(Harmony h)
        {
            _switch = AccessTools.MethodDelegate<Action<PlayerPickup>>(AccessTools.Method(typeof(PlayerPickup), "SwitchWeapons"));
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
            yield return Jump1; yield return Jump2; yield return Swap;
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
                    if (e == Swap) a.performed += ctx => Do(false);
                    else if (taunt >= 0) a.performed += ctx => { TauntFrame[taunt] = Time.frameCount; };
                    else a.performed += ctx => Do(true, ctx);
                    a.Enable();
                    Actions[e] = a;
                }
                _wheelMine = false;
                foreach (ConfigEntry<string> k in All()) if (k.Value != null && k.Value.IndexOf("/scroll", StringComparison.Ordinal) >= 0) _wheelMine = true;
            }
            catch (Exception x) { BundlePlugin.Log.LogWarning("Keys: " + e.Definition.Key + " could not be set to " + e.Value + ": " + x.Message); }
        }

        // a press of one of your keys: the game's own routine for it, under the game's own conditions
        private static void Do(bool jump, InputAction.CallbackContext ctx = default(InputAction.CallbackContext))
        {
            try
            {
                if (BundleMenu.Opened) return;
                PauseManager pm = PauseManager.Instance;
                if (pm != null && (pm.pause || pm.chatting)) return;
                Settings st = Settings.Instance;
                FirstPersonController me = st != null ? st.localPlayer : null;
                if (me == null || !me.isActiveAndEnabled) return;
                if (jump) me.Jump(ctx);
                else if (me.playerPickupScript != null) _switch(me.playerPickupScript);
            }
            catch (Exception) { }
        }

        // the game hears "swap" from the wheel (and from a gamepad button): the wheel is skipped once it is yours
        private static bool WheelPrefix(InputAction.CallbackContext __0)
        {
            if (!_wheelMine) return true;
            InputControl c = __0.control;
            return !(c != null && c.device is Mouse);
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
