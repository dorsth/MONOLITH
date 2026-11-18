// MONOLITH - a frame cap with evenly spaced frames. Dorsth was here.
//
// Why: the game's own cap is Unity's (Application.targetFrameRate). On Windows it waits for the next frame by asking the
// system to sleep, and the system wakes the game up to about a millisecond early or late, so the frames come out uneven.
//
// How:
//  * The game's cap is switched off while this one is on (the game sets it again every frame, so it is switched off
//    again at the start and at the end of every frame). The main menu keeps the game's own 90.
//  * The wait is at the very start of the frame, before the game reads the mouse and keyboard, so each frame is made
//    from the newest input. The frames sit on a fixed grid: 1/FPS apart. A frame that comes in late is not waited for;
//    one that comes in more than a whole frame late starts a new grid.
//  * Windows: most of the wait on a high resolution timer, the rest by watching the clock. How long before the frame the
//    timer is set to wake the game (the margin) follows how late this PC's timer wakes it: the latest wake of the last
//    few seconds plus 0.05 ms, between 0.1 and 2 ms. The latest wake fades a little every frame, also in frames where
//    the timer was not used, so a margin that grew can shrink again. (A margin from "995 of 1000 wakes" is worse: once
//    it is larger than the wait, the timer is no longer used, no wakes are measured and the margin stays large.)
//    While it watches the clock it runs the processor's pause instruction, so the core uses less power and its other
//    thread (hyperthreading) gets the time.
//    Where that timer is missing (older Windows): 1 ms sleeps, the last 1.5 ms by the clock. Other systems: the same.
//  * With V-Sync on it waits for nothing: V-Sync spaces the frames already.
//  * Off: nothing is left in the game's frame (the step is taken out of Unity's player loop).
// No garbage per frame: the clock, a few numbers, and calls into Windows and Unity.
// It only sets when the next frame starts, as the game's own cap does: nothing about shooting, damage or what is sent changes.
// Measuring it: Ultimate Diag's benchmark prints the frame pacing (spread, frames off the average) for a run with it on or off.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;

namespace Monolith
{
    internal static class FrameCap
    {
        internal static ConfigEntry<bool> On;
        internal static ConfigEntry<int> Fps;

        private struct StartStep { }
        private struct EndStep { }

        private static bool _in, _menu, _ready, _hiRes, _period1;
        private static long _next, _period, _margin, _peak;
        private static long _f = Stopwatch.Frequency;
        private static IntPtr _timer = IntPtr.Zero;
        // kept here so the delegates live as long as the player loop holds them
        private static readonly PlayerLoopSystem.UpdateFunction StartFn = Start, EndFn = End;

        internal static void Bind(ConfigFile c)
        {
            On = c.Bind("Frame Cap", "Enabled", false, "A frame cap of its own with evenly spaced frames, in place of the game's cap.");
            Fps = c.Bind("Frame Cap", "FPS", 250, "Frames per second for the frame cap (30 to 5000).");
            On.SettingChanged += (s, e) => Apply();
            Fps.SettingChanged += (s, e) => Apply();
            SceneManager.activeSceneChanged += (a, b) => { _menu = b.name == "MainMenu"; _next = 0; };
            _menu = SceneManager.GetActiveScene().name == "MainMenu";
            Apply();
        }

        private static void Apply()
        {
            _period = _f / Mathf.Clamp(Fps.Value, 30, 5000);
            _next = 0;
            if (On.Value) Ready();
            else if (_period1) { try { timeEndPeriod(1); } catch { } _period1 = false; }
            if (On.Value != _in) Loop(On.Value);
        }

        // the waiting tools, once
        private static void Ready()
        {
            if (_ready) { if (!_hiRes && !_period1) Period1(); return; }
            _ready = true;
            _margin = _f * 15 / 10000;   // 1.5 ms
            if (Application.platform == RuntimePlatform.WindowsPlayer)
            {
                try
                {
                    _timer = CreateWaitableTimerExW(IntPtr.Zero, null, 0x2 /* high resolution */, 0x1F0003);
                    if (_timer != IntPtr.Zero) { _hiRes = true; _peak = _f * 4 / 10000; Margin(); }
                }
                catch { _timer = IntPtr.Zero; }
                if (!_hiRes) Period1();
            }
        }

        private static void Period1()
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer) return;
            try { timeBeginPeriod(1); _period1 = true; } catch { }
        }

        private static void Margin()
        {
            long m = _peak + _f * 5 / 100000;                            // + 0.05 ms
            _margin = Math.Max(_f / 10000, Math.Min(_f * 2 / 1000, m));  // 0.1 to 2 ms
        }

        private static void Loop(bool add)
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem[] top = loop.subSystemList;
            if (top == null || top.Length == 0) return;
            for (int i = 0; i < top.Length; i++)
            {
                var subs = new List<PlayerLoopSystem>(top[i].subSystemList ?? new PlayerLoopSystem[0]);
                subs.RemoveAll(s => s.type == typeof(StartStep) || s.type == typeof(EndStep));
                if (add && i == 0) subs.Insert(0, new PlayerLoopSystem { type = typeof(StartStep), updateDelegate = StartFn });
                if (add && top[i].type == typeof(UnityEngine.PlayerLoop.PostLateUpdate)) subs.Add(new PlayerLoopSystem { type = typeof(EndStep), updateDelegate = EndFn });
                top[i].subSystemList = subs.ToArray();
            }
            loop.subSystemList = top;
            PlayerLoop.SetPlayerLoop(loop);
            _in = add;
        }

        private static void End()
        {
            if (On.Value && !_menu && Application.targetFrameRate != -1) Application.targetFrameRate = -1;
        }

        private static void Start()
        {
            if (_menu || !On.Value) return;
            if (Application.targetFrameRate != -1) Application.targetFrameRate = -1;
            if (QualitySettings.vSyncCount > 0) { _next = 0; return; }
            long now = Stopwatch.GetTimestamp();
            if (_next == 0 || now - _next > _period) { _next = now + _period; return; }
            long target = _next;
            _next += _period;
            if (now < target) Wait(target);
        }

        private static void Wait(long target)
        {
            // the latest wake fades: half of it in about 1400 frames
            if (_hiRes) { _peak -= _peak >> 11; Margin(); }
            while (true)
            {
                long now = Stopwatch.GetTimestamp(), left = target - now;
                if (left <= _margin) break;
                if (_hiRes)
                {
                    long wakeAt = target - _margin;
                    long due = -((wakeAt - now) * 10000000L / _f);   // relative, in 100 ns
                    if (due >= 0) break;
                    if (!SetWaitableTimerEx(_timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0)) { _hiRes = false; Period1(); _margin = _f * 15 / 10000; continue; }
                    WaitForSingleObject(_timer, 0xFFFFFFFF);
                    long late = Stopwatch.GetTimestamp() - wakeAt;
                    if (late > _peak) { _peak = late; Margin(); }
                }
                else Thread.Sleep(1);
            }
            while (Stopwatch.GetTimestamp() < target) Thread.SpinWait(4);   // pause instructions, a fraction of a microsecond
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetWaitableTimerEx(IntPtr timer, ref long due, int period, IntPtr routine, IntPtr arg, IntPtr reason, uint delay);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint ms);
        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint ms);
    }
}
