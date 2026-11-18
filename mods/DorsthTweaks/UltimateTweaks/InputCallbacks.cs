using System;
using System.Collections.Generic;
using System.Reflection;

namespace UltimateTweaks
{
    /// The game's own input set (InputManager.inputActions) lives for the whole session. Player arms (FPArms.Awake) and the kill camera
    /// (KillCam.OnEnable) add mouse callbacks to it and never take them off, so after their objects are destroyed the callbacks stay: each one
    /// is still called on every mouse movement, and it keeps its dead object in memory. This reads the callback lists (an internal part of
    /// Unity's Input System, by reflection) and can take off the ones whose object is destroyed.
    internal static class InputCallbacks
    {
        static FieldInfo[] lists; static PropertyInfo length, item; static bool looked, ok;
        static readonly List<KeyValuePair<int, Delegate>> dead = new List<KeyValuePair<int, Delegate>>();

        static bool Ready()
        {
            if (looked) return ok; looked = true;
            try
            {
                var t = typeof(UnityEngine.InputSystem.InputAction); const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                lists = new[] { t.GetField("m_OnStarted", F), t.GetField("m_OnPerformed", F), t.GetField("m_OnCanceled", F) };
                if (lists[0] == null || lists[1] == null || lists[2] == null) return false;
                length = lists[0].FieldType.GetProperty("length", F); item = lists[0].FieldType.GetProperty("Item", F);
                ok = length != null && item != null;
            }
            catch { ok = false; }
            return ok;
        }

        /// How many callbacks are on the game's input set, and how many of those belong to a destroyed object. With remove: those are taken off.
        public static void Scan(bool remove, out int total, out int gone)
        {
            total = 0; gone = 0;
            var set = global::InputManager.inputActions; if (set == null || !Ready()) return;
            var index = new object[1];
            foreach (UnityEngine.InputSystem.InputAction a in set.asset)
            {
                dead.Clear();
                for (int k = 0; k < 3; k++)
                {
                    object arr = lists[k].GetValue(a); int n = (int)length.GetValue(arr, null);
                    for (int i = 0; i < n; i++)
                    {
                        index[0] = i; var d = item.GetValue(arr, index) as Delegate; if (d == null) continue;
                        total++;
                        var owner = d.Target as UnityEngine.Object;
                        if (!ReferenceEquals(owner, null) && owner == null) { gone++; if (remove) dead.Add(new KeyValuePair<int, Delegate>(k, d)); }
                    }
                }
                foreach (var kv in dead)
                {
                    var d = kv.Value as Action<UnityEngine.InputSystem.InputAction.CallbackContext>; if (d == null) continue;
                    if (kv.Key == 0) a.started -= d; else if (kv.Key == 1) a.performed -= d; else a.canceled -= d;
                }
            }
            dead.Clear();
        }
    }
}
