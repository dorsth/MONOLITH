#if !DORSTH_BUNDLE   // ezCrosshair's own window: MONOLITH has its menu instead
// ezCrosshair - a STRAFTAT mod (BepInEx 5). Dorsth was here.
using UnityEngine;

namespace EzCrosshair
{
    // How every text field in the menu looks (TextBox), the same as in the STRAFTH menu: a shade darker than the
    // window with a thin edge that lights up while typing, and a magnifier for searches.
    internal static class FieldStyle
    {
        // a see-through shade, never a solid block: darker over a dark window, lighter over a light one
        public static Color Fill
        {
            get
            {
                float t = Mathf.InverseLerp(0.1f, 0.6f, Ui.Bg.grayscale);
                return new Color(0f, 0f, 0f, Mathf.Lerp(0.38f, 0.14f, t));
            }
        }
        public static Color EdgeIdle { get { return Ui.Tint(0.2f); } }
        public static Color EdgeTyping { get { return Ui.Tint(0.45f); } }

        private static Texture2D icon;

        // a magnifier: a ring with a short handle down to the right, drawn once
        public static Texture2D Icon()
        {
            if (icon != null) return icon;
            const int N = 64;
            icon = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "ST_SearchIcon" };
            var px = new Color32[N * N];
            Vector2 c = new Vector2(26f, 38f), a = new Vector2(38.5f, 25.5f), b = new Vector2(56f, 8f);
            const float r = 17f, ring = 3.2f, handle = 4.2f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float dRing = Mathf.Abs(Vector2.Distance(p, c) - r) - ring;
                    Vector2 ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    float dHandle = Vector2.Distance(p, a + ab * t) - handle;
                    float d = Mathf.Min(dRing, dHandle);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - d) * 255f));
                }
            icon.SetPixels32(px);
            icon.Apply(false, true);
            return icon;
        }
    }
}
#endif
