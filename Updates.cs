// MONOLITH - says in the game's main menu when a newer MONOLITH is on Thunderstore. Dorsth was here.
//
// Thunderstore's rules: a package leaves updating to the mod manager and never downloads code itself. So this only
// asks Thunderstore for the newest version number, once at start, and when there is a newer one a line in the main
// menu says so. A click on it opens the package's page; installed with a mod manager, the line says to update there.
// Nothing of it runs during a match, and it shows nowhere but the main menu.
using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace Monolith
{
    internal static class Updates
    {
        private const string Package = "Dorsth/MONOLITH";
        private const string Api = "https://thunderstore.io/api/experimental/package/" + Package + "/";
        private const string Page = "https://thunderstore.io/c/straftat/p/" + Package + "/";
        private const string Me = "MONOLITH";
        // line heights up from the bottom of the screen: MONOLITH lowest, then STRAFTH, then Straftat Tournaments, so they never overlap
        private const float Spot = 1.6f;

        private static string _latest, _manager;

        internal static void Start(MonoBehaviour host)
        {
            _manager = Manager(typeof(BundlePlugin).Assembly.Location);
            host.StartCoroutine(Ask(host));
        }

        private static bool Newer { get { return _latest != null && new Version(_latest) > new Version(BundlePlugin.Version); } }

        private sealed class Notice : MonoBehaviour
        {
            private GUIStyle _st;
            private string _line;
            private void Awake() { SceneManager.activeSceneChanged += (a, b) => Check(); Check(); }
            private void Check() { enabled = Newer && SceneManager.GetActiveScene().name == "MainMenu"; }

            private void OnGUI()
            {
                if (_line == null)
                    _line = _manager != null
                        ? Me + " " + _latest + Lang.T(" is out. Update it in ") + _manager + "."
                        : Me + " " + _latest + Lang.T(" is out. Click to get it on Thunderstore.");
                if (_st == null)
                {
                    _st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height / 50f), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic, richText = false };
                    _st.normal.textColor = _manager == null ? new Color(0.62f, 0.95f, 0.75f) : new Color(0.85f, 0.85f, 0.85f);
                }
                float h = _st.fontSize * 2f;
                Rect r = new Rect(0f, Screen.height - h * Spot, Screen.width, h);
                GUI.Label(r, _line, _st);
                if (_manager == null && Event.current.type == EventType.MouseUp && r.Contains(Event.current.mousePosition))
                {
                    Event.current.Use();
                    Application.OpenURL(Page);
                }
            }
        }

        // the files of a mod manager's profile sit under its own folder, in a "profiles" folder
        private static string Manager(string path)
        {
            string p = (path ?? "").Replace('/', '\\').ToLowerInvariant();
            if (p.Contains("r2modman")) return "r2modman";
            if (p.Contains("thunderstore mod manager")) return "Thunderstore Mod Manager";
            if (p.Contains("gale") && p.Contains("\\profiles\\")) return "Gale";
            if (p.Contains("\\profiles\\")) return Lang.T("your mod manager");
            return null;
        }

        private static IEnumerator Ask(MonoBehaviour host)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(Api))
            {
                req.timeout = 10;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { BundlePlugin.Log.LogInfo("Updates: Thunderstore did not answer: " + req.error); yield break; }
                Match m = Regex.Match(req.downloadHandler.text, "\"latest\"\\s*:\\s*\\{[^{}]*?\"version_number\"\\s*:\\s*\"(\\d+\\.\\d+\\.\\d+)\"");
                if (m.Success) _latest = m.Groups[1].Value;
            }
            if (!Newer) yield break;
            BundlePlugin.Log.LogMessage(Me + " " + _latest + " is out (you have " + BundlePlugin.Version + ").");
            host.gameObject.AddComponent<Notice>();
        }
    }
}
