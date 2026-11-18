// MONOLITH - match stats: kills, deaths and damage per player, and a few numbers that say more about how a match went.
// Written anew from Dorsth's own Kill Tracker (jachthafen.straftat.killtracker). Dorsth was here.
//
// What is counted (every player, on every client, from what the game already sends everyone):
//  * a hit: a player's health goes down (PlayerHealth.health, its network setter). The damage is on the HUD's 0-100 scale
//    (the game counts health in its own units: fullHealth). It is credited to the player in PlayerHealth.killer, which
//    every weapon sets just before it takes the health off. Falling into the void, a suicide and damage with no attacker
//    are credited to nobody. When the health arrives before the killer in the same frame, it waits for it until the
//    next frame (as Kill Tracker did).
//  * a kill: a hit that takes the health from above 0 to 0 or less.
//  * per take (one fight; a round is one map of takes): the first kill (opening kill and opening
//    death), a trade (killing the enemy who killed your teammate in the 3 seconds before), two or more kills in one take,
//    a clutch (your team wins the round after you were the last one alive on it while two enemies were alive), and who
//    was still alive at the end.
// What is shown, only between rounds and after the match, never while anyone can play (no live health or damage of
// others): a small board on the round-end screen (kills, deaths, damage in that round, that map, only), each name on
// the game's own round-end screen with its kills this match next to it (as Kill Tracker did), and a full board on the
// victory screen. The full one also goes to the BepInEx log.
// Cost: one dictionary write per health change; nothing per frame while you play; the boards draw only while shown.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;

namespace Monolith
{
    internal static class Stats
    {
        internal static ConfigEntry<bool> On;

        internal sealed class P
        {
            public string Name = "";
            public int Kills, Deaths, Takes, Rounds, Survived, OpenK, OpenD, Trades, Multi, Clutch, Self;
            public float Dmg, Taken;
            public int RoundKills; public bool Alive = true, LastAlive;
            public int RK, RD; public float RDmg;   // this round (this map) only: the round-end board
        }

        internal static readonly Dictionary<int, P> All = new Dictionary<int, P>();
        private static readonly Dictionary<int, float> Known = new Dictionary<int, float>();            // health by PlayerHealth instance
        private static readonly Dictionary<int, int> KillerOf = new Dictionary<int, int>();             // victim instance -> attacker id
        private static readonly Dictionary<int, int> IdOfKiller = new Dictionary<int, int>();           // killer transform -> player id
        private struct Hit { public int Victim, Frame; public float Dmg; public bool Lethal; }
        private static readonly Dictionary<int, Hit> Waiting = new Dictionary<int, Hit>();
        private static readonly Dictionary<int, int> IdOf = new Dictionary<int, int>();                 // instance -> player id
        private struct Death { public int Victim, Killer; public float At; }
        private static readonly List<Death> RoundDeaths = new List<Death>();
        private static bool _firstDone, _takeOn;

        internal static void Bind(ConfigFile c, Harmony h)
        {
            On = c.Bind("Match Stats", "Enabled", true, "Count kills, deaths, damage and more for every player; show them between rounds and after the match.");
            Type ph = AccessTools.TypeByName("PlayerHealth");
            h.Patch(AccessTools.Method(ph, "sync___set_value_health"), postfix: new HarmonyMethod(typeof(Stats), nameof(HealthPost)));
            h.Patch(AccessTools.Method(ph, "sync___set_value_killer"), postfix: new HarmonyMethod(typeof(Stats), nameof(KillerPost)));
            h.Patch(AccessTools.Method(typeof(RoundManager), "NextRoundCall"), prefix: new HarmonyMethod(typeof(Stats), nameof(RoundEnd)));
            // a new take: the game tells every player to spawn again (an observers RPC: it runs on every client)
            MethodInfo spawn = AccessTools.GetDeclaredMethods(typeof(GameManager)).Find(m => m.Name.StartsWith("RpcLogic___ObserversRoundSpawn", StringComparison.Ordinal));
            if (spawn != null) h.Patch(spawn, prefix: new HarmonyMethod(typeof(Stats), nameof(TakeStart)));
            h.Patch(AccessTools.Method(typeof(ScoreManager), "ResetScores"), postfix: new HarmonyMethod(typeof(Stats), nameof(MatchReset)));
            SceneManager.sceneLoaded += (s, m) => Scene(s.name);
            var go = new GameObject("MONOLITH.Stats"); UnityEngine.Object.DontDestroyOnLoad(go); go.hideFlags = HideFlags.HideAndDontSave;
            Board.Me = go.AddComponent<Board>(); Board.Me.enabled = false;
        }

        // ------------------------------------------------------------------ counting

        private static void HealthPost(PlayerHealth __instance, float value)
        {
            try
            {
                if (!On.Value || __instance == null) return;
                int key = __instance.GetInstanceID();
                float before;
                bool seen = Known.TryGetValue(key, out before);
                Known[key] = value;
                if (!seen) return;                                                        // the first value of a player on this map: nothing to compare with
                if (before <= 0f && value <= 0f) return;                                   // already dead
                float full = __instance.fullHealth > 0f ? __instance.fullHealth : 100f;
                float drop = Mathf.Min(before, full) - value;
                if (drop <= 0.01f) return;
                int victim = Id(__instance); if (victim < 0) return;
                var hit = new Hit { Victim = victim, Dmg = Mathf.Min(drop, Mathf.Min(before, full)) * 100f / full, Lethal = value <= 0f, Frame = Time.frameCount };
                if (__instance.fellVoid || __instance.suicide) { Credit(hit, -1); return; }
                int atk;
                if (KillerOf.TryGetValue(key, out atk)) { Credit(hit, atk); return; }
                Waiting[key] = hit;
                if (Waiting.Count > 0) Board.Me.Wait();
            }
            catch (Exception e) { Quiet(e); }
        }

        private static void KillerPost(PlayerHealth __instance, Transform value)
        {
            try
            {
                if (!On.Value || __instance == null || value == null) return;
                int key = __instance.GetInstanceID();
                int atk = IdOfTransform(value);
                KillerOf[key] = atk;
                Hit w;
                if (Waiting.TryGetValue(key, out w) && w.Frame >= Time.frameCount - 1) { Waiting.Remove(key); Credit(w, atk); }
            }
            catch (Exception e) { Quiet(e); }
        }

        // health that came without a killer in time: nobody gets it
        internal static void Flush()
        {
            if (Waiting.Count == 0) return;
            List<int> done = null;
            foreach (var kv in Waiting) if (kv.Value.Frame < Time.frameCount - 1) { Credit(kv.Value, -1); (done ?? (done = new List<int>())).Add(kv.Key); }
            if (done != null) foreach (int k in done) Waiting.Remove(k);
        }

        private static void Credit(Hit h, int atk)
        {
            P v = Get(h.Victim);
            if (atk == h.Victim) atk = -1;
            v.Taken += h.Dmg;
            P a = atk >= 0 ? Get(atk) : null;
            if (a != null && Team(atk) != Team(h.Victim)) { a.Dmg += h.Dmg; a.RDmg += h.Dmg; }
            if (!h.Lethal) return;
            v.Deaths++; v.RD++; v.Alive = false;
            if (a == null) v.Self++;
            float now = Time.time;
            if (a != null && Team(atk) != Team(h.Victim))
            {
                a.Kills++; a.RK++; a.RoundKills++;
                if (a.RoundKills == 2) a.Multi++;
                // a trade: the victim killed a teammate of the attacker in the last 3 seconds
                for (int i = RoundDeaths.Count - 1; i >= 0; i--)
                {
                    Death d = RoundDeaths[i];
                    if (now - d.At > 3f) break;
                    if (d.Killer == h.Victim && d.Victim != atk && Team(d.Victim) == Team(atk)) { a.Trades++; break; }
                }
                if (!_firstDone) { _firstDone = true; a.OpenK++; v.OpenD++; }
            }
            else if (!_firstDone) { _firstDone = true; v.OpenD++; }
            RoundDeaths.Add(new Death { Victim = h.Victim, Killer = a != null ? atk : -1, At = now });
            // the last one alive on a team against two or more: a clutch if that team still wins
            int vt = Team(h.Victim), mates = 0, foes = 0; P last = null;
            foreach (var kv in All)
            {
                if (!kv.Value.Alive) continue;
                if (Team(kv.Key) == vt) { mates++; last = kv.Value; } else foes++;
            }
            if (mates == 1 && foes >= 2 && last != null) last.LastAlive = true;
        }

        // STRAFTAT's words: a take is one fight (everyone spawns, one team is left), a round is one map (first to N takes).
        // Per take: who survived, the opening kill, 2k, clutches. A take ends when the next one starts, at the round-end
        // screen and on the victory screen. The team still alive at the end won the take.
        private static void TakeStart() { try { if (On.Value) { FinishTake(); _takeOn = true; } } catch (Exception e) { Quiet(e); } }

        private static void FinishTake()
        {
            Flush();
            if (_takeOn && RoundDeaths.Count > 0)   // a take ends with at least one death; a spawn right after a map load is not a take of its own
            {
                foreach (var kv in ClientInstance.playerInstances) if (kv.Value != null) Get(kv.Key);
                foreach (var kv in All)
                {
                    P p = kv.Value;
                    if (!ClientInstance.playerInstances.ContainsKey(kv.Key)) continue;
                    p.Takes++;
                    if (p.Alive) p.Survived++;
                    if (p.LastAlive && TeamAlive(Team(kv.Key))) p.Clutch++;
                }
            }
            foreach (var kv in All) { P p = kv.Value; p.Alive = true; p.LastAlive = false; p.RoundKills = 0; }
            _firstDone = false; RoundDeaths.Clear(); _takeOn = false;
        }

        private static bool TeamAlive(int team)
        {
            foreach (var kv in All) if (kv.Value.Alive && Team(kv.Key) == team && ClientInstance.playerInstances.ContainsKey(kv.Key)) return true;
            return false;
        }

        // the round-end screen: the last take of the map is over; the small board
        private static void RoundEnd(int winningTeamId)
        {
            try
            {
                if (!On.Value) return;
                FinishTake();
                foreach (var kv in All) if (ClientInstance.playerInstances.ContainsKey(kv.Key)) kv.Value.Rounds++;
                Names();
                if (All.Count > 0) Board.Me.Show(false);
            }
            catch (Exception e) { Quiet(e); }
        }

        private static void MatchReset()
        {
            All.Clear(); Known.Clear(); KillerOf.Clear(); IdOfKiller.Clear(); Waiting.Clear(); IdOf.Clear(); RoundDeaths.Clear();
            _firstDone = false; _takeOn = false;
        }

        private static void Scene(string name)
        {
            Known.Clear(); KillerOf.Clear(); IdOfKiller.Clear(); Waiting.Clear(); IdOf.Clear();
            if (name == "VictoryScene") { FinishTake(); Names(); if (On.Value && All.Count > 0) { Board.Me.Show(true); Log(); } return; }
            Board.Me.Hide();
            if (name == "MainMenu") MatchReset();
            else if (On.Value) { FinishTake(); _takeOn = true; foreach (var kv in All) { kv.Value.RK = 0; kv.Value.RD = 0; kv.Value.RDmg = 0f; } }   // a map: its first take, and a new round
        }

        private static P Get(int id) { P p; if (!All.TryGetValue(id, out p)) { p = new P(); All[id] = p; } return p; }

        private static void Names()
        {
            foreach (var kv in All)
            {
                ClientInstance ci;
                string n = ClientInstance.playerInstances.TryGetValue(kv.Key, out ci) && ci != null ? Untag(ci.PlayerName) : "";
                if (n.Length > 0) kv.Value.Name = n;
                else if (kv.Value.Name.Length == 0) kv.Value.Name = "player " + (kv.Key + 1);
            }
        }

        // A name without the game's text tags (<#FFF>, <b>, <color=red>, <gradient=...>), the same rule as strafth.com's
        // leaderboards; also a tag cut off at the end of a name by Steam's length limit ("<#52A").
        private static readonly System.Text.RegularExpressions.Regex Tag = new System.Text.RegularExpressions.Regex(
            @"</?(?:#[0-9a-f]{3,8}|color(?:=[^<>]{1,20})?|b|i|u|s|size[^<>]{0,12}|alpha[^<>]{0,12}|mark[^<>]{0,20}|font[^<>]{0,30}|sup|sub|noparse|br|cspace[^<>]{0,12}|mspace[^<>]{0,12}|voffset[^<>]{0,12}|rotate[^<>]{0,12}|uppercase|lowercase|smallcaps|gradient[^<>]{0,40}|sprite[^<>]{0,40}|style[^<>]{0,20}|nobr|indent[^<>]{0,12}|pos[^<>]{0,12}|line-height[^<>]{0,12}|width[^<>]{0,12}|margin[^<>]{0,12})>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase), CutTag = new System.Text.RegularExpressions.Regex(@"</?(?:#[0-9a-f]{0,8}|color[^<>]{0,20})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        internal static string Untag(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return CutTag.Replace(Tag.Replace(name, ""), "").Trim();
        }

        internal static int Team(int id)
        {
            int t;
            ScoreManager sm = ScoreManager.Instance;
            if (sm != null && sm.PlayerIdToTeamId.TryGetValue(id, out t)) return t;
            return id;
        }

        private static int Id(PlayerHealth h)
        {
            int key = h.GetInstanceID(), id;
            if (IdOf.TryGetValue(key, out id)) return id;
            id = -1;
            if (h.playerValues != null && h.playerValues.playerClient != null) id = h.playerValues.playerClient.PlayerId;
            if (id >= 0) IdOf[key] = id;
            return id;
        }

        private static int IdOfTransform(Transform t)
        {
            int key = t.GetInstanceID(), id;
            if (IdOfKiller.TryGetValue(key, out id)) return id;
            PlayerHealth h = t.GetComponent<PlayerHealth>() ?? t.GetComponentInChildren<PlayerHealth>() ?? t.GetComponentInParent<PlayerHealth>();
            id = h != null ? Id(h) : -1;
            if (id >= 0) IdOfKiller[key] = id;
            return id;
        }

        private static bool _said;
        private static void Quiet(Exception e) { if (_said) return; _said = true; if (BundlePlugin.Log != null) BundlePlugin.Log.LogWarning("Match stats: " + e.Message); }

        // ------------------------------------------------------------------ the boards

        internal static string Num(float f) { return Mathf.RoundToInt(f).ToString(CultureInfo.InvariantCulture); }

        internal static List<KeyValuePair<int, P>> Sorted()
        {
            var l = new List<KeyValuePair<int, P>>(All);
            l.Sort((a, b) => Team(a.Key) != Team(b.Key) ? Team(a.Key).CompareTo(Team(b.Key)) : b.Value.Kills != a.Value.Kills ? b.Value.Kills.CompareTo(a.Value.Kills) : b.Value.Dmg.CompareTo(a.Value.Dmg));
            return l;
        }

        private static void Log()
        {
            var sb = new StringBuilder("Match stats:\n");
            foreach (var kv in Sorted())
            {
                P p = kv.Value; int r = Math.Max(1, p.Takes);
                sb.Append("  ").Append(p.Name).Append(" (team ").Append(Team(kv.Key) + 1).Append("): ")
                  .Append(p.Kills).Append(" kills, ").Append(p.Deaths).Append(" deaths, ").Append(Num(p.Dmg)).Append(" damage (")
                  .Append(Num(p.Dmg / r)).Append(" a take), ").Append(Num(p.Taken)).Append(" taken, opening kills ").Append(p.OpenK)
                  .Append(", opening deaths ").Append(p.OpenD).Append(", trades ").Append(p.Trades).Append(", 2 or more kills in a take ").Append(p.Multi)
                  .Append(", clutches ").Append(p.Clutch).Append(", survived ").Append(p.Survived).Append(" of ").Append(p.Takes).Append(" takes (").Append(p.Rounds).Append(" rounds), own deaths ").Append(p.Self).Append('\n');
            }
            if (BundlePlugin.Log != null) BundlePlugin.Log.LogInfo(sb.ToString().TrimEnd());
        }

        // The boards in the game's own look: the font of its round-end screen, text with a light shadow on one dark
        // see-through panel (one panel, not a strip per row: strips look like a second kill feed), the names in the feed's colors (your team as
        // the game colors you, the others as it colors enemies), the headings in its mint, the numbers white. Every cell is
        // its own text in a fixed column, names left and numbers centered under their heading, so the columns line up.
        // The round board sits in the top left corner, above the dark band with the game's "First to" line; it stays 3 seconds after the
        // round-end screen closes. Made when a board is shown (cells are reused); the canvas is only active while it is.
        internal sealed class Board : MonoBehaviour
        {
            internal static Board Me;
            private bool _full, _waitOnly; private float _hideAt = -1f;
            private GameObject _canvas; private RectTransform _root, _back;
            private const float Pad = 14f, Edge = 24f;   // panel padding around the text; the round board's distance from the screen's left and top
            private TMP_FontAsset _font; private Material _mat;
            private readonly List<TextMeshProUGUI> _cells = new List<TextMeshProUGUI>();

            private static readonly string[] FullHead = { "player", "k", "d", "adr", "taken", "open", "trade", "2k", "clutch", "alive" };
            private static readonly float[] FullW = { 320f, 70f, 70f, 80f, 100f, 90f, 90f, 70f, 100f, 100f };
            private static readonly string[] RoundHead = { "player", "k", "d", "dmg" };
            private static readonly float[] RoundW = { 250f, 60f, 60f, 80f };
            private static readonly Color Mint = new Color32(0xB4, 0xF2, 0xB4, 0xFF), White = new Color32(0xF8, 0xF8, 0xF8, 0xFF);

            internal void Show(bool full)
            {
                _full = full; _waitOnly = false; _hideAt = -1f;
                if (_canvas == null) Make();
                if (_root == null) return;
                int me = -1;
                try { if (LobbyController.Instance != null && LobbyController.Instance.LocalPlayerController != null) me = LobbyController.Instance.LocalPlayerController.PlayerId; } catch { }
                Color self = new Color32(0xAC, 0xBC, 0xFC, 0xFF), enemy = new Color32(0xE8, 0x80, 0x68, 0xFF), c;
                try
                {
                    PauseManager pm = PauseManager.Instance;
                    if (pm != null && ColorUtility.TryParseHtmlString("#" + (pm.selfNameLogColor ?? "").TrimStart('#'), out c)) self = c;
                    if (pm != null && ColorUtility.TryParseHtmlString("#" + (pm.enemyNameLogColor ?? "").TrimStart('#'), out c)) enemy = c;
                }
                catch { }
                string[] head = full ? FullHead : RoundHead; float[] w = full ? FullW : RoundW;
                float size = full ? 26f : 20f, rowH = full ? 40f : 22f, pad = 0f, width = 0f;
                foreach (float f in w) width += f;
                var rows = Sorted();
                if (!full) rows.Sort((a, b) => Team(a.Key) != Team(b.Key) ? Team(a.Key).CompareTo(Team(b.Key)) : b.Value.RK != a.Value.RK ? b.Value.RK.CompareTo(a.Value.RK) : b.Value.RDmg.CompareTo(a.Value.RDmg));
                int cell = 0;
                Row(ref cell, 0, head, w, size, rowH, pad, Mint, Mint);
                for (int r = 0; r < rows.Count; r++)
                {
                    P p = rows[r].Value; int t = Math.Max(1, p.Takes);
                    Color name = me >= 0 && Team(rows[r].Key) == Team(me) ? self : enemy;
                    string[] v = full
                        ? new[] { p.Name, p.Kills.ToString(), p.Deaths.ToString(), Num(p.Dmg / t), Num(p.Taken), p.OpenK + "-" + p.OpenD, p.Trades.ToString(), p.Multi.ToString(), p.Clutch.ToString(), p.Survived + "/" + p.Takes }
                        : new[] { p.Name, p.RK.ToString(), p.RD.ToString(), Num(p.RDmg) };
                    Row(ref cell, r + 1, v, w, size, rowH, pad, name, White);
                }
                for (int i = cell; i < _cells.Count; i++) _cells[i].gameObject.SetActive(false);
                // the panel fits the text as drawn: from the names' left edge to the right edge of the widest last number,
                // the same padding on every side
                float right = 0f;
                for (int i = 0; i < cell; i++)
                {
                    TextMeshProUGUI t = _cells[i]; RectTransform rt = t.rectTransform;
                    float tw = Mathf.Min(t.preferredWidth, rt.sizeDelta.x);
                    float end = t.alignment == TextAlignmentOptions.MidlineLeft ? rt.anchoredPosition.x + tw : rt.anchoredPosition.x + (rt.sizeDelta.x + tw) * 0.5f;
                    if (end > right) right = end;
                }
                float height = (rows.Count + 1) * (rowH + 4f) - 4f;
                _back.anchoredPosition = new Vector2(-Pad, Pad); _back.sizeDelta = new Vector2(right + 2f * Pad, height + 2f * Pad);
                _root.sizeDelta = new Vector2(right, height);
                if (full) { _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0f, 1f); _root.anchoredPosition = new Vector2(Edge + Pad, -150f); }   // left: the podium is on the right
                else { _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0f, 1f); _root.anchoredPosition = new Vector2(Edge + Pad, -(Edge + Pad)); }   // the panel ends above the dark band of the round-end screen
                _canvas.SetActive(true);
                enabled = true;
            }

            private void Row(ref int cell, int r, string[] v, float[] w, float size, float rowH, float pad, Color name, Color num)
            {
                float y = -r * (rowH + 4f);
                float x = pad;
                for (int i = 0; i < v.Length && i < w.Length; i++)
                {
                    TextMeshProUGUI t = cell < _cells.Count ? _cells[cell] : NewCell(); cell++;
                    string text = v[i] ?? "";
                    t.text = text; t.color = i == 0 ? name : num;
                    // a long name gets smaller first (down to 75%), then ends in "…"
                    t.enableAutoSizing = i == 0; t.fontSizeMax = size; t.fontSizeMin = size * 0.75f; t.fontSize = size;
                    t.overflowMode = i == 0 ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
                    t.alignment = i == 0 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Midline;
                    Place(t.rectTransform, x, y, w[i], rowH); t.gameObject.SetActive(true);
                    x += w[i];
                }
            }

            private static void Place(RectTransform rt, float x, float y, float w, float h)
            {
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h);
            }

            private TextMeshProUGUI NewCell()
            {
                var go = new GameObject("cell"); go.transform.SetParent(_root, false);
                var t = go.AddComponent<TextMeshProUGUI>();
                t.raycastTarget = false; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow; t.richText = false;
                if (_font != null) { t.font = _font; if (_mat != null) t.fontSharedMaterial = _mat; }
                _cells.Add(t); return t;
            }

            private void Make()
            {
                _canvas = new GameObject("MONOLITH.StatsBoard");
                DontDestroyOnLoad(_canvas); _canvas.hideFlags = HideFlags.HideAndDontSave;
                var cv = _canvas.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 400;
                var sc = _canvas.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920f, 1080f); sc.matchWidthOrHeight = 0.5f;
                var go = new GameObject("board"); go.transform.SetParent(_canvas.transform, false);
                _root = go.AddComponent<RectTransform>();
                // one dark see-through panel behind the whole board, like the game's own boxes (kill feed, Tab screen)
                var back = new GameObject("backdrop"); back.transform.SetParent(_root, false);
                var bi = back.AddComponent<Image>(); bi.color = new Color(0f, 0f, 0f, 0.45f); bi.raycastTarget = false;
                _back = bi.rectTransform; _back.anchorMin = _back.anchorMax = _back.pivot = new Vector2(0f, 1f);
                // the font and material of the round-end screen's names, so it reads like the game
                try
                {
                    TMP_Text src = null;
                    if (RoundManager.Instance != null) src = Traverse.Create(RoundManager.Instance).Field("playerName").GetValue() as TMP_Text;
                    if (src == null) src = FindObjectOfType<TextMeshProUGUI>();
                    if (src != null && src.font != null)
                    {
                        _font = src.font;
                        // one copy of its material with a soft shadow under the letters (made once, kept): readable on a bright map
                        _mat = new Material(src.fontSharedMaterial) { name = "MONOLITH stats shadow" };
                        _mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                        _mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.45f));
                        _mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.25f); _mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.25f);
                        _mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.2f);
                    }
                }
                catch { }
                _canvas.SetActive(false);
            }

            internal void Hide() { if (_canvas != null) _canvas.SetActive(false); if (!_waitOnly) enabled = false; }
            internal void Wait() { if (!enabled) { _waitOnly = true; enabled = true; } }

            private void Update()
            {
                if (Waiting.Count > 0) Flush();
                if (_waitOnly) { if (Waiting.Count == 0) { _waitOnly = false; enabled = false; } return; }
                if (!_full)
                {
                    PauseManager pm = PauseManager.Instance;
                    if (pm != null && pm.onEndRoundScreen) _hideAt = -1f;
                    else if (_hideAt < 0f) _hideAt = Time.unscaledTime + 3f;      // 3 seconds more after the round-end screen
                    else if (Time.unscaledTime >= _hideAt) Hide();
                }
            }
        }
    }
}
