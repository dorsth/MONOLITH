#if !DORSTH_BUNDLE   // ezCrosshair's own window: MONOLITH has its menu instead
// ezCrosshair - a STRAFTAT mod (BepInEx 5). Dorsth was here.
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EzCrosshair
{
    // The menu over a live blur of the screen, the same way as the STRAFTH menu: at the end of each frame the finished
    // screen is copied and blurred, the menu is drawn by its own camera into its own picture (on a layer none of the
    // game's cameras use), and that picture is laid over the frame last. So it's see-through and blurred everywhere
    // (a match, the home screen, a lobby, the game's own menus) and nothing is drawn over it. Only runs while the menu
    // is open.
    internal static class Blur
    {
        // a layer none of the game's cameras draw (STRAFTH's menu uses it too, its camera is somewhere else)
        public const int MenuLayer = 31;

        public static RenderTexture Result;
        private static Glass glass;
        private static Material mat, over, alphaOnly;

        public static Camera MenuCamera()
        {
            if (glass == null)
            {
                var go = new GameObject("ezCrosshair MenuCamera");
                Object.DontDestroyOnLoad(go);
                // close to the middle of the world (far out a float can't place the menu to a whole pixel and text wobbles),
                // apart from the other mods' menu cameras
                go.transform.position = new Vector3(0f, -6100f, 0f);
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.cullingMask = 1 << MenuLayer;
                cam.orthographic = true;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 20f;
                cam.useOcclusionCulling = false;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                glass = go.AddComponent<Glass>();
                glass.Cam = cam;
            }
            return glass.Cam;
        }

        // the canvas the camera draws: set while the menu is up, null when it closes
        public static void DrawMenu(Canvas c)
        {
            MenuCamera();
            glass.Menu = c;
            glass.enabled = c != null;
            if (c != null) glass.Run();
        }

        // lays the menu's picture over the screen by its see-through-ness
        private static Material Over()
        {
            if (over == null) over = new Material(Shader.Find("UI/Default")) { hideFlags = HideFlags.HideAndDontSave };
            return over;
        }

        // for the last thing on the window: makes it solid in the menu's picture (only the alpha, not the color)
        public static Material AlphaOnly()
        {
            if (alphaOnly == null)
            {
                alphaOnly = new Material(Shader.Find("UI/Default")) { hideFlags = HideFlags.HideAndDontSave };
                if (alphaOnly.HasProperty("_ColorMask")) alphaOnly.SetFloat("_ColorMask", 1f);
            }
            return alphaOnly;
        }

        // Behind one panel: the blurred picture, cut to the panel's shape, kept on the panel's spot.
        public static void Behind(Image panel)
        {
            if (panel == null) return;
            var holder = Ui.Node("blur", panel.transform.parent);
            holder.SetSiblingIndex(panel.transform.GetSiblingIndex());
            var shape = holder.gameObject.AddComponent<Image>();
            shape.sprite = panel.sprite;
            shape.type = Image.Type.Sliced;
            shape.color = Color.white;
            shape.raycastTarget = false;
            holder.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var pic = Ui.Node("picture", holder).gameObject.AddComponent<RawImage>();
            pic.raycastTarget = false;
            Ui.Fill(pic.rectTransform);
            var f = holder.gameObject.AddComponent<Follower>();
            f.Panel = panel;
            f.Pic = pic;
        }

        private sealed class Follower : MonoBehaviour
        {
            public Image Panel;
            public RawImage Pic;
            private readonly Vector3[] corners = new Vector3[4];

            private void LateUpdate()
            {
                if (Panel == null) { Destroy(gameObject); return; }
                bool show = Panel.isActiveAndEnabled && Result != null;
                if (Pic.enabled != show) Pic.enabled = show;
                if (!show) return;
                var me = (RectTransform)transform;
                var pr = Panel.rectTransform;
                if (me.anchorMin != pr.anchorMin) me.anchorMin = pr.anchorMin;
                if (me.anchorMax != pr.anchorMax) me.anchorMax = pr.anchorMax;
                if (me.pivot != pr.pivot) me.pivot = pr.pivot;
                if (me.anchoredPosition != pr.anchoredPosition) me.anchoredPosition = pr.anchoredPosition;
                if (me.sizeDelta != pr.sizeDelta) me.sizeDelta = pr.sizeDelta;
                if (Pic.texture != Result) Pic.texture = Result;
                // the part of the picture right behind the panel, in screen pixels
                me.GetWorldCorners(corners);
                var cam = glass != null ? glass.Cam : null;
                Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]), b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
                float w = Mathf.Max(1f, Screen.width), h = Mathf.Max(1f, Screen.height);
                var uv = new Rect(a.x / w, a.y / h, (b.x - a.x) / w, (b.y - a.y) / h);
                if (Pic.uvRect != uv) Pic.uvRect = uv;
            }
        }

        // A 9 tap Gaussian (sigma about 2 pixels) in 5 smooth samples, each drawn at the share of the total it adds.
        private static readonly float[] TapAt = { 0f, 1.3846f, -1.3846f, 3.2308f, -3.2308f };
        private static readonly float[] TapAlpha = MakeAlphas(new[] { 0.2270f, 0.3162f, 0.3162f, 0.0703f, 0.0703f });
        private static float[] MakeAlphas(float[] weights)
        {
            var a = new float[weights.Length];
            float sum = 0f;
            for (int i = 0; i < weights.Length; i++) { sum += weights[i]; a[i] = weights[i] / sum; }
            return a;
        }

        private static bool Ready()
        {
            if (mat != null) return true;
            var sh = Shader.Find("UI/Default");
            if (sh == null) return false;
            mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            if (mat.HasProperty("_ColorMask")) mat.SetFloat("_ColorMask", 14f);
            return true;
        }

        private static void Pass(RenderTexture src, RenderTexture dst, bool across)
        {
            Graphics.Blit(src, dst);
            if (!Ready()) return;
            var prev = RenderTexture.active;
            RenderTexture.active = dst;
            mat.mainTexture = src;
            GL.PushMatrix();
            GL.LoadOrtho();
            mat.SetPass(0);
            float px = 1f / src.width, py = 1f / src.height;
            GL.Begin(GL.QUADS);
            for (int i = 1; i < TapAt.Length; i++)
            {
                float du = across ? TapAt[i] * px : 0f, dv = across ? 0f : TapAt[i] * py;
                GL.Color(new Color(1f, 1f, 1f, TapAlpha[i]));
                GL.TexCoord2(du, dv); GL.Vertex3(0f, 0f, 0f);
                GL.TexCoord2(du, 1f + dv); GL.Vertex3(0f, 1f, 0f);
                GL.TexCoord2(1f + du, 1f + dv); GL.Vertex3(1f, 1f, 0f);
                GL.TexCoord2(1f + du, dv); GL.Vertex3(1f, 0f, 0f);
            }
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        // The copy of the screen to a quarter, down to a sixteenth and back up step by step (fine bright detail spread
        // out first), then five Gaussian rounds: the same blur as the STRAFTH menu.
        private static void Process(Texture src, bool flip)
        {
            int w = Mathf.Max(8, src.width / 4), h = Mathf.Max(8, src.height / 4);
            if (Result == null || Result.width != w || Result.height != h)
            {
                if (Result != null) { Result.Release(); Object.Destroy(Result); }
                Result = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "ezCrosshair_Blur" };
                Object.DontDestroyOnLoad(Result);
            }
            var a = RenderTexture.GetTemporary(Mathf.Max(8, src.width / 2), Mathf.Max(8, src.height / 2), 0, RenderTextureFormat.ARGB32);
            var t = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var e8 = RenderTexture.GetTemporary(Mathf.Max(8, w / 2), Mathf.Max(8, h / 2), 0, RenderTextureFormat.ARGB32);
            var e16 = RenderTexture.GetTemporary(Mathf.Max(8, w / 4), Mathf.Max(8, h / 4), 0, RenderTextureFormat.ARGB32);
            var oldFilter = src.filterMode;
            src.filterMode = a.filterMode = t.filterMode = e8.filterMode = e16.filterMode = FilterMode.Bilinear;
            t.wrapMode = TextureWrapMode.Clamp;
            if (flip) Graphics.Blit(src, a, new Vector2(1f, -1f), new Vector2(0f, 1f));
            else Graphics.Blit(src, a);
            Graphics.Blit(a, Result);
            Graphics.Blit(Result, e8);
            Graphics.Blit(e8, e16);
            Graphics.Blit(e16, e8);
            Graphics.Blit(e8, Result);
            src.filterMode = oldFilter;
            for (int i = 0; i < 5; i++)
            {
                Pass(Result, t, true);
                Pass(t, Result, false);
            }
            RenderTexture.ReleaseTemporary(a);
            RenderTexture.ReleaseTemporary(t);
            RenderTexture.ReleaseTemporary(e8);
            RenderTexture.ReleaseTemporary(e16);
        }

        private sealed class Glass : MonoBehaviour
        {
            public Camera Cam;
            public Canvas Menu;
            private RenderTexture grab, menu;
            private readonly WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();
            private bool running;

            public void Run() { if (!running) StartCoroutine(Loop()); }

            private IEnumerator Loop()
            {
                running = true;
                yield return null;
                while (enabled && Menu != null)
                {
                    yield return endOfFrame;
                    if (Menu == null || !Menu.isActiveAndEnabled) continue;
                    try
                    {
                        int w = Screen.width, h = Screen.height;
                        if (grab == null || grab.width != w || grab.height != h)
                        {
                            if (grab != null) { grab.Release(); Destroy(grab); }
                            grab = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "ezCrosshair_ScreenCopy" };
                            DontDestroyOnLoad(grab);
                        }
                        ScreenCapture.CaptureScreenshotIntoRenderTexture(grab);
                        Process(grab, SystemInfo.graphicsUVStartsAtTop);
                        if (menu == null || menu.width != w || menu.height != h)
                        {
                            if (menu != null) { Cam.targetTexture = null; menu.Release(); Destroy(menu); }
                            menu = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "ezCrosshair_MenuPicture" };
                            DontDestroyOnLoad(menu);
                        }
                        if (Cam.targetTexture != menu) Cam.targetTexture = menu;
                        Cam.Render();
                        Graphics.Blit(menu, (RenderTexture)null, Over());
                    }
                    catch (System.Exception e) { EzCrosshairPlugin.Log.LogWarning("Menu blur: " + e.Message); }
                }
                running = false;
            }
        }
    }
}
#endif
