# Draws the built-in crosshairs again (art/*.png) and writes src/Images.cs from them.
# Each keeps the shape and size of its first picture (art/first), measured from it, but is drawn as a clean shape:
# exactly the same on all four sides, with an even soft edge where it is round or slanted and none where it is straight.
# They are drawn at 8 times their size, 64 samples a pixel; the mod shrinks them to the size on screen, so at 1x they are
# the first pictures without the stray pixels, and bigger they stay smooth. White takes the color, black stays dark.
import base64, os
import numpy as np
from PIL import Image
here = os.path.dirname(os.path.abspath(__file__))
D, SS = 8, 8

def rects(*rs): return lambda X, Y: np.any([(X >= a) & (X < c) & (Y >= b) & (Y < d) for a, b, c, d in rs], axis=0)
def blob(cx, cy, r, p): return lambda X, Y: (np.abs(X - cx) / r) ** p + (np.abs(Y - cy) / r) ** p <= 1   # p 1 a diamond, 2 a circle
def union(*fs): return lambda X, Y: np.any([f(X, Y) for f in fs], axis=0)

def draw(name, w, h, white, black=None, swap=False):
    n = SS * D
    xs = (np.arange(w * n) + 0.5) / n; ys = (np.arange(h * n) + 0.5) / n
    X, Y = np.meshgrid(xs, ys)
    Wm = white(X, Y); Km = (black(X, Y) & ~Wm) if black else np.zeros_like(Wm)
    cw = Wm.reshape(h * D, SS, w * D, SS).mean(axis=(1, 3)); ck = Km.reshape(h * D, SS, w * D, SS).mean(axis=(1, 3))
    a = cw + ck; lum = np.where(a > 0, cw / np.maximum(a, 1e-9), 0)
    if swap: lum = np.where(a > 0, 1 - lum, 0)
    o = np.zeros((h * D, w * D, 4), np.uint8)
    o[..., 0] = o[..., 1] = o[..., 2] = np.round(lum * 255); o[..., 3] = np.round(a * 255)
    Image.fromarray(o, 'RGBA').save(os.path.join(here, name.replace(' ', '_') + '.png'))

# straight ones: exactly the first pictures
draw('square', 4, 4, rects((0, 0, 4, 4)))
draw('plus', 10, 10, rects((4, 0, 6, 10), (0, 4, 10, 6)))
draw('cross', 12, 12, rects((5, 0, 7, 4), (5, 8, 7, 12), (0, 5, 4, 7), (8, 5, 12, 7)))
draw('outlined plus', 16, 16, rects((7, 1, 9, 15), (1, 7, 15, 9)), rects((6, 0, 10, 16), (0, 6, 16, 10)))
draw('chunky plus', 10, 10, rects((4, 2, 6, 8), (2, 4, 8, 6)), rects((2, 0, 8, 10), (0, 2, 10, 8)))
# round and slanted ones: sizes and roundness measured from the first pictures
draw('star', 10, 10, union(rects((4, 0, 6, 10), (0, 4, 10, 6)), blob(5, 5, 5.6, 0.75)))
draw('dot', 8, 8, blob(4, 4, 2.05, 3), blob(4, 4, 3.3, 1.6))
draw('circle', 8, 8, blob(4, 4, 2.1, 1.8), blob(4, 4, 4.0, 2))
draw('pinhole', 10, 10, blob(5, 5, 2.8, 0.9), blob(5, 5, 4.2, 1.4))
draw('diamond', 10, 10, rects((4, 2, 6, 8), (2, 4, 8, 6)), blob(5, 5, 4.55, 1.2))
draw('dark diamond', 10, 10, rects((4, 2, 6, 8), (2, 4, 8, 6)), blob(5, 5, 4.45, 1.2), swap=True)
# brackets: two arcs of a circle with round ends, and a dot
R, XIN, T, REACH, CY = 13.5, 3.4, 1.2, 7.25, 11
def bracket(X, Y):
    out = []
    for side in (1, -1):
        cx = XIN + R if side == 1 else 32 - XIN - R
        m = (np.abs(np.hypot(X - cx, Y - CY) - R) <= T) & (np.abs(Y - CY) <= REACH) & ((X - cx) * side < 0)
        ex = cx - side * np.sqrt(R * R - REACH * REACH)
        for ey in (CY - REACH, CY + REACH): m |= np.hypot(X - ex, Y - ey) <= T
        out.append(m)
    return out[0] | out[1]
draw('brackets', 32, 22, union(bracket, blob(16, CY, 1.9, 3)))

cases = []
for f in sorted(os.listdir(here)):
    if f.endswith('.png'):
        cases.append('                case "%s": return "%s";' % (f[:-4].replace('_', ' '), base64.b64encode(open(os.path.join(here, f), 'rb').read()).decode()))
open(os.path.join(here, '..', 'src', 'Images.cs'), 'w').write('''// ezCrosshair - a STRAFTAT mod (BepInEx 5). Dorsth was here.
namespace EzCrosshair
{
    // The crosshairs, drawn by art/make.py at %d times their size, kept here so the mod is a single dll.
    internal static class Images
    {
        internal const int Density = %d;

        public static string Get(string name)
        {
            switch (name)
            {
%s
                default: return null;
            }
        }
    }
}
''' % (D, D, '\n'.join(cases)))
