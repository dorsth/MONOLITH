// MONOLITH - how other players look on your screen: one outfit and one hat for enemies, one for teammates. Dorsth was here.
//
// Only on your screen, nothing is sent. What it does NOT touch, on purpose:
//  * Outfit: the game reads a player's outfit number in two places (dressing the player, dressing the body it leaves
//    behind). In those two places, for players that are not you, the number is the one you picked. The number the
//    game stores and syncs is never changed.
//  * Hat: the hat you picked is drawn on the hat the player wears, which is only not drawn any more. It rides
//    on the real one, so it flies off when shot like any hat. A player with no hat gets a drawn one that stays on.
//    The real hat is NOT replaced, and this is why (read from the game's code):
//      - every object a shot passes gets a bullet trail end sent to all players (ShootServer); a hat other players do
//        not have would add one for them, or move it;
//      - shooting a hat off counts for the Hats Off achievement (Settings.RecordAchievementHat);
//      - the damage ray takes the first thing it meets on the weapon's player mask; whether a hat is on that mask is
//        set per weapon in the game's files.
//    With the real hat kept, all three are exactly what they are without this.
//  * Outline colors: the game's own two settings (Settings, outline colors), not a second set. The game's saved value
//    is the one that counts when it starts; a change here is written where the game keeps it and shown on the game's
//    own settings page, and a change there comes back here. Whether enemies have an outline at all stays the
//    lobby's rule: this only sets the color the game uses when it draws one.
//  * Enemy outlines in lobbies you host: the game's own lobby switch (the tick box in its lobby settings), which
//    the game forgets at every start. This remembers it and sets it for you, by setting that tick box: the game then
//    does what it does when you click it, and every player in your lobby gets the outlines, as with the game's own
//    option. In somebody else's lobby the host's choice counts and nothing here changes it: an outline only you would
//    see is information the others do not have.
// Applies when a player is dressed: from the next round. Costs nothing between rounds: no script of its own runs.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Monolith
{
    internal static class Players
    {
        internal static ConfigEntry<string> EnemyOutfit, TeamOutfit, EnemyHat, TeamHat;
        internal static ConfigEntry<Color> EnemyOutline, TeamOutline;
        internal static ConfigEntry<bool> HostOutlines;
        private const string TeamKey = "TeamOutlineColor", EnemyKey = "EnemyOutlineColor";   // where the game saves them
        private static bool _fromGame;
        private static int _hits;

        internal static void Bind(ConfigFile c)
        {
            const string S = "Players";
            EnemyOutfit = c.Bind(S, "Enemy Outfit", "", "Every enemy in this outfit, on your screen only. Empty: their own. From the next round.");
            EnemyHat = c.Bind(S, "Enemy Hat", "", "Every enemy with this hat, on your screen only. Empty: their own. From the next round.");
            TeamOutfit = c.Bind(S, "Team Outfit", "", "Every teammate in this outfit, on your screen only. Empty: their own. From the next round.");
            TeamHat = c.Bind(S, "Team Hat", "", "Every teammate with this hat, on your screen only. Empty: their own. From the next round.");
            // the defaults are the game's own
            EnemyOutline = c.Bind(S, "Enemy Outline Color", Color.red, "The outline around enemies, in lobbies that have enemy outlines on. The game's own setting: the same here and in the game's settings. From the next round.");
            TeamOutline = c.Bind(S, "Team Outline Color", new Color(0.5803921f, 2f / 3f, 1f, 1f), "The outline around teammates. The game's own setting: the same here and in the game's settings. From the next round.");
            try
            {
                FromGame(TeamOutline, TeamKey); FromGame(EnemyOutline, EnemyKey);
                OutlineHandler.TeamColor = TeamOutline.Value; OutlineHandler.EnemyColor = EnemyOutline.Value;
            }
            catch (Exception e) { BundlePlugin.Log.LogInfo("Players: the game's outline colors could not be read: " + e.Message); }
            TeamOutline.SettingChanged += (s, e) => ToGame(true);
            EnemyOutline.SettingChanged += (s, e) => ToGame(false);
            HostOutlines = c.Bind(S, "Enemy Outlines In My Lobbies", false, "The game's lobby option for enemy outlines, remembered between launches. It counts in lobbies you host; in somebody else's lobby the host decides.");
            HostOutlines.SettingChanged += (s, e) => { if (!_tickFromGame) Tick(SteamLobby.Instance); };
        }

        // ---- enemy outlines in lobbies you host: the game's own tick box, remembered

        private static int _tickId; private static bool _tickFromGame, _tickFromHere;
        // one delegate for the life of the game: the tick box of each new lobby window gets the same one
        private static readonly UnityAction<bool> Ticked = on =>
        {
            if (_tickFromHere || HostOutlines == null || HostOutlines.Value == on) return;
            _tickFromGame = true;
            try { HostOutlines.Value = on; } finally { _tickFromGame = false; }
        };

        // the lobby window exists: its tick box is set to what you last had, and a click on it is remembered from here on
        private static void Tick(SteamLobby lobby)
        {
            try
            {
                Toggle box = lobby != null ? lobby.enemyOutlineToggle : null;
                if (box == null) return;
                if (box.GetInstanceID() != _tickId) { _tickId = box.GetInstanceID(); box.onValueChanged.AddListener(Ticked); }
                bool want = HostOutlines.Value;
                if (box.isOn == want) return;
                _tickFromHere = true;
                try
                {
                    // with a lobby running the game's own routine passes it on; before that it reads the box when the lobby starts
                    if (GameManager.Instance != null) box.isOn = want; else box.SetIsOnWithoutNotify(want);
                }
                finally { _tickFromHere = false; }
            }
            catch (Exception e) { BundlePlugin.Log.LogInfo("Players: the lobby's outline switch was not set: " + e.Message); }
        }

        private static void LobbyMade(SteamLobby __instance) { Tick(__instance); }

        // ---- outline colors: one setting in two menus

        private static readonly FieldInfo TeamUi = AccessTools.Field(typeof(OutlineHandler), "teamOutline"), EnemyUi = AccessTools.Field(typeof(OutlineHandler), "enemyOutline");

        // at start: what the game has saved is what counts
        private static void FromGame(ConfigEntry<Color> entry, string key)
        {
            if (!PlayerPrefs.HasKey(key)) return;
            Color saved;
            if (!ColorUtility.TryParseHtmlString("#" + PlayerPrefs.GetString(key), out saved)) return;
            if (ColorUtility.ToHtmlStringRGBA(saved) != ColorUtility.ToHtmlStringRGBA(entry.Value)) { _fromGame = true; try { entry.Value = saved; } finally { _fromGame = false; } }
        }

        // changed here (the menu, a loaded profile): into the game, the way its own settings page does it
        private static void ToGame(bool team)
        {
            if (_fromGame) return;
            try
            {
                Color c = team ? TeamOutline.Value : EnemyOutline.Value;
                if (team) OutlineHandler.TeamColor = c; else OutlineHandler.EnemyColor = c;
                PlayerPrefs.SetString(team ? TeamKey : EnemyKey, ColorUtility.ToHtmlStringRGBA(c));
                PlayerPrefs.Save();
                // the game's settings page, if it is loaded, shows the new color too
                FieldInfo f = team ? TeamUi : EnemyUi;
                if (f == null) return;
                foreach (OutlineHandler h in Resources.FindObjectsOfTypeAll<OutlineHandler>())
                {
                    if (h == null || !h.gameObject.scene.IsValid()) continue;
                    ColorInputSetting ui = f.GetValue(h) as ColorInputSetting;
                    if (ui == null) continue;
                    ui.color = c;
                    try { ui.Start(); } catch { }   // its own routine that puts the color into its number boxes and its swatch
                }
            }
            catch (Exception e) { BundlePlugin.Log.LogInfo("Players: the outline color was not passed to the game: " + e.Message); }
        }

        // changed on the game's own settings page: taken over here
        private static void SavedInGame(Color __0, MethodBase __originalMethod)
        {
            try
            {
                ConfigEntry<Color> entry = __originalMethod.Name == "SaveTeamOutline" ? TeamOutline : EnemyOutline;
                if (ColorUtility.ToHtmlStringRGBA(__0) == ColorUtility.ToHtmlStringRGBA(entry.Value)) return;
                _fromGame = true;
                try { entry.Value = __0; } finally { _fromGame = false; }
            }
            catch { }
        }

        internal static void Install(Harmony h)
        {
            MethodInfo died = AccessTools.Method(typeof(PlayerHealth), "sync___set_value_health");
            if (died != null) h.Patch(died, prefix: new HarmonyMethod(typeof(Players).GetMethod(nameof(Died), BindingFlags.Static | BindingFlags.NonPublic)));
            MethodInfo tr = typeof(Players).GetMethod(nameof(MatTr), BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo post = typeof(Players).GetMethod(nameof(Dressed), BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo saved = typeof(Players).GetMethod(nameof(SavedInGame), BindingFlags.Static | BindingFlags.NonPublic);
            foreach (string name in new[] { "SaveTeamOutline", "SaveEnemyOutline" })
            {
                MethodInfo m = AccessTools.Method(typeof(OutlineHandler), name);
                if (m != null) h.Patch(m, postfix: new HarmonyMethod(saved));
                else BundlePlugin.Log.LogInfo("Players: " + name + " was not found; a color set in the game's own settings shows here after a restart.");
            }
            MethodInfo lobby = AccessTools.Method(typeof(SteamLobby), "Start");
            if (lobby != null) h.Patch(lobby, postfix: new HarmonyMethod(typeof(Players).GetMethod(nameof(LobbyMade), BindingFlags.Static | BindingFlags.NonPublic)));
            else BundlePlugin.Log.LogInfo("Players: the lobby window's start was not found; the outline switch is not remembered.");
            MethodInfo dress = Find(typeof(PlayerSetup), "RpcLogic___ChangeDress_"), body = Find(typeof(PlayerHealth), "RpcLogic___ExplodeForAll_");
            if (dress == null) { BundlePlugin.Log.LogWarning("Players: the game's dress routine was not found (game update?). Forced outfits and hats are off."); return; }
            h.Patch(dress, transpiler: new HarmonyMethod(tr), postfix: new HarmonyMethod(post));
            int inDress = _hits;
            if (body != null) h.Patch(body, transpiler: new HarmonyMethod(tr));
            if (inDress != 3 || _hits != 4) BundlePlugin.Log.LogInfo("Players: outfit read in " + inDress + " places of the dress routine (3 expected) and " + (_hits - inDress) + " of the body routine (1 expected).");
        }

        private static MethodInfo Find(Type t, string start)
        {
            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(t)) if (m.Name.StartsWith(start, StringComparison.Ordinal)) return m;
            return null;
        }

        private static IEnumerable<CodeInstruction> MatTr(IEnumerable<CodeInstruction> ins)
        {
            MethodInfo get = AccessTools.Method(typeof(PlayerSetup), "sync___get_value_mat"), mine = AccessTools.Method(typeof(Players), nameof(Mat));
            foreach (CodeInstruction c in ins)
            {
                if (get != null && c.Calls(get)) { c.opcode = OpCodes.Call; c.operand = mine; _hits++; }
                yield return c;
            }
        }

        // 0: you, or not known; 1: teammate; 2: enemy. The same test the game uses for its outlines.
        private static int Side(PlayerSetup ps)
        {
            if (ps == null || ps.IsOwner) return 0;
            LobbyController lc = LobbyController.Instance; ScoreManager sm = ScoreManager.Instance;
            if (lc == null || sm == null || lc.LocalPlayerController == null) return 0;
            PlayerValues pv = ps.GetComponent<PlayerValues>();
            if (pv == null || pv.playerClient == null) return 0;
            return sm.GetTeamId(pv.playerClient.PlayerId) == sm.GetTeamId(lc.LocalPlayerController.PlayerId) ? 1 : 2;
        }

        /// <summary>The outfit number the game dresses this player in: its own, or yours for it.</summary>
        internal static int Mat(PlayerSetup ps)
        {
            int real = ps.mat;
            try
            {
                int side = Side(ps);
                if (side == 0) return real;
                string want = side == 2 ? EnemyOutfit.Value : TeamOutfit.Value;
                if (string.IsNullOrEmpty(want)) return real;
                int i = Array.IndexOf(Outfits(), want);
                return i >= 0 ? i : real;
            }
            catch { return real; }
        }

        // ---- the names to pick from and their pictures (the game's own wardrobe entries), read once it has them

        private static CosmeticsManager _from; private static string[] _outfits = new string[0], _hats = new string[0];
        private static Sprite[] _outfitPics = new Sprite[0], _hatPics = new Sprite[0];
        private static readonly FieldInfo SuitEntries = AccessTools.Field(typeof(CosmeticsManager), "suitsChildren"), HatEntries = AccessTools.Field(typeof(CosmeticsManager), "hatsChildren");

        private static void Names()
        {
            CosmeticsManager cm = CosmeticsManager.Instance;
            if (cm == null || (object)cm == (object)_from) return;
            try
            {
                Material[] mats = cm.mats; GameObject[] hats = cm.hats;
                if (mats == null || hats == null || hats.Length == 0) return;   // not filled yet
                CosmeticInstance[] suitUi = SuitEntries != null ? SuitEntries.GetValue(cm) as CosmeticInstance[] : null, hatUi = HatEntries != null ? HatEntries.GetValue(cm) as CosmeticInstance[] : null;
                var o = new string[mats.Length]; var op = new Sprite[mats.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    CosmeticInstance ui = suitUi != null && i < suitUi.Length ? suitUi[i] : null;
                    o[i] = Named(ui, mats[i] != null ? mats[i].name : "outfit " + i, o, i);
                    op[i] = ui != null ? ui.sprite : null;
                }
                var hs = new string[hats.Length]; var hp = new Sprite[hats.Length];
                for (int i = 0; i < hats.Length; i++)
                {
                    CosmeticInstance ui = hatUi != null && i < hatUi.Length ? hatUi[i] : null;
                    hs[i] = Named(ui, hats[i] != null ? hats[i].name : "no hat", hs, i);
                    hp[i] = ui != null ? ui.sprite : null;
                }
                _outfits = o; _hats = hs; _outfitPics = op; _hatPics = hp; _from = cm;
            }
            catch (Exception e) { BundlePlugin.Log.LogInfo("Players: the game's outfits could not be read: " + e.Message); }
        }

        // the name the game's wardrobe shows, else the object's own; never the same name twice
        private static string Named(CosmeticInstance ui, string fallback, string[] taken, int count)
        {
            string n = ui != null && !string.IsNullOrEmpty(ui.cosmeticName) ? ui.cosmeticName.Trim() : Plain(fallback);
            if (n.Length == 0) n = fallback;
            string name = n;
            for (int k = 2; Array.IndexOf(taken, name, 0, count) >= 0; k++) name = n + " " + k;
            return name;
        }

        // "M_NeonSuits_01" -> "NeonSuits 01", "M_Garage_00" -> "Garage": the object's name without what only its maker needs
        private static string Plain(string name)
        {
            string n = name.Replace("(Clone)", "").Trim();
            foreach (string pre in new[] { "M_", "SM_", "P_", "Hat_" }) if (n.StartsWith(pre, StringComparison.OrdinalIgnoreCase) && n.Length > pre.Length) { n = n.Substring(pre.Length); break; }
            if (n.EndsWith("_00", StringComparison.Ordinal) && n.Length > 3) n = n.Substring(0, n.Length - 3);
            return n.Replace('_', ' ').Trim();
        }

        internal static string[] Outfits() { Names(); return _outfits; }
        internal static string[] Hats() { Names(); return _hats; }

        /// <summary>The game's own wardrobe picture of an outfit or hat, by the name it is picked by; null when there is none.</summary>
        internal static Sprite Picture(bool hat, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Names();
            int i = Array.IndexOf(hat ? _hats : _outfits, name);
            Sprite[] pics = hat ? _hatPics : _outfitPics;
            return i >= 0 && i < pics.Length ? pics[i] : null;
        }

        // ---- the hat: drawn on the real one, which stays as it is and is only not drawn, so it comes off when the real one is shot.
        // A player without a hat gets a drawn one where the game puts hats; there is no real hat to shoot, so it comes off when
        // the player is killed (Knock, Died).

        private static void Dressed(PlayerSetup __instance, Transform ___hatToWearPosition, Vector3 __2)
        {
            try
            {
                int side = Side(__instance);
                if (side == 0) return;
                string want = side == 2 ? EnemyHat.Value : TeamHat.Value;
                if (string.IsNullOrEmpty(want)) return;
                int i = Array.IndexOf(Hats(), want);
                if (i < 0) return;
                GameObject prefab = CosmeticsManager.Instance.hats[i];
                if (prefab == null) return;
                GameObject real = __instance.hat, shown;
                if (real == null)
                {
                    if (___hatToWearPosition == null) return;
                    shown = UnityEngine.Object.Instantiate(prefab, ___hatToWearPosition.position, Quaternion.identity, ___hatToWearPosition);
                    shown.AddComponent<HatPosition>().reference = ___hatToWearPosition;
                    shown.transform.forward = __2;   // the way the game turns a hat: the third value it was called with
                    PlayerHealth ph = __instance.GetComponent<PlayerHealth>() ?? __instance.GetComponentInParent<PlayerHealth>() ?? __instance.GetComponentInChildren<PlayerHealth>();
                    if (ph != null) { Knock k = shown.AddComponent<Knock>(); k.Key = ph.GetInstanceID(); Knock.On[k.Key] = k; }
                }
                else
                {
                    if (real.name.StartsWith(prefab.name, StringComparison.Ordinal)) return;   // wears it already
                    foreach (Renderer r in real.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                    shown = UnityEngine.Object.Instantiate(prefab, real.transform.position, real.transform.rotation, real.transform);
                    // the real hat's own size must not stretch it
                    Vector3 a = prefab.transform.localScale, b = real.transform.localScale;
                    shown.transform.localScale = new Vector3(b.x != 0f ? a.x / b.x : a.x, b.y != 0f ? a.y / b.y : a.y, b.z != 0f ? a.z / b.z : a.z);
                }
                shown.name = "MonolithHat";
                shown.tag = "Untagged";
                shown.layer = 18;
                // only the model: the hat list holds the wardrobe's models, whose own scripts switch them off outside the wardrobe
                foreach (MonoBehaviour m in shown.GetComponentsInChildren<MonoBehaviour>(true)) if (!(m is HatPosition) && !(m is Knock)) { m.enabled = false; UnityEngine.Object.Destroy(m); }
                // nothing a shot or a body can touch
                foreach (Collider c in shown.GetComponentsInChildren<Collider>(true)) { c.enabled = false; UnityEngine.Object.Destroy(c); }
                foreach (Rigidbody rb in shown.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.Destroy(rb);
                shown.SetActive(true);
            }
            catch (Exception e) { BundlePlugin.Log.LogInfo("Players: a hat was left as it was: " + e.Message); }
        }

        // A drawn hat on a player who wears none: flies off when the player is killed, the way a shot hat does, and is gone
        // two seconds later. No collider, so nothing can hit it or stand on it. Told by the health the game sends (Died), so
        // nothing runs between hits.
        internal sealed class Knock : MonoBehaviour
        {
            internal static readonly Dictionary<int, Knock> On = new Dictionary<int, Knock>();   // by the PlayerHealth it sits on
            internal int Key;

            private void OnDestroy() { Knock k; if (On.TryGetValue(Key, out k) && k == this) On.Remove(Key); }

            internal void Off(Vector3 dir)
            {
                On.Remove(Key);
                if (dir.sqrMagnitude < 0.01f) dir = -transform.forward;
                HatPosition pos = GetComponent<HatPosition>(); if (pos != null) Destroy(pos);
                transform.SetParent(null, true);
                var rb = gameObject.AddComponent<Rigidbody>();
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.AddForce(dir.normalized * 10f, ForceMode.Impulse);
                rb.AddTorque(Vector3.Cross(Vector3.up, dir) * 40f, ForceMode.Impulse);
                Destroy(gameObject, 2f);
            }
        }

        // before the new health is set: only a living player going to 0 is a kill. The game also sends 0 to players that
        // are already down (a new round, someone joining), and the hat must stay on then.
        private static void Died(PlayerHealth __instance, float value)
        {
            if (Knock.On.Count == 0 || value > 0f || __instance == null || __instance.sync___get_value_health() <= 0f) return;
            Knock k;
            if (Knock.On.TryGetValue(__instance.GetInstanceID(), out k) && k != null)
                try { k.Off(__instance.sync___get_value_lastKillDirection()); } catch (Exception e) { BundlePlugin.Log.LogInfo("Players: a drawn hat stayed on: " + e.Message); }
        }
    }
}
