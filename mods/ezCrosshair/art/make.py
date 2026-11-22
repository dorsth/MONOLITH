# Draws the built-in crosshairs pixel by pixel (art/*.png) and writes src/Images.cs from them.
# Every pixel is fully on or off: white (the part the color tints), black (an outline, stays dark) or clear.
# Sizes are even, so a picture sits exactly on the middle of an even screen (1920x1080: between pixels 959 and 960).
import base64, os
from PIL import Image
here = os.path.dirname(os.path.abspath(__file__))
W, K = (255, 255, 255, 255), (0, 0, 0, 255)

def grid(rows):
    return [[c == 'W' for c in r] for r in rows]

def sym(n, inside):
    # a shape from a test on the distance to the middle of an n x n grid
    m = (n - 1) / 2
    return [[inside(x - m, y - m) for x in range(n)] for y in range(n)]

def pad(g, p):
    w = len(g[0])
    e = [[False] * (w + 2 * p) for _ in range(p)]
    return e + [[False] * p + r + [False] * p for r in g] + e

def outline(g, times=1, corners=False):
    # a black edge around the white, one pixel per pass; without corners the edge follows the shape's steps
    g = pad(g, times); h, w = len(g), len(g[0]); edge = [[False] * w for _ in range(h)]
    cur = [r[:] for r in g]
    for _ in range(times):
        nxt = [r[:] for r in cur]
        for y in range(h):
            for x in range(w):
                if cur[y][x]: continue
                n = [(0, 1), (1, 0), (0, -1), (-1, 0)] + ([(1, 1), (1, -1), (-1, 1), (-1, -1)] if corners else [])
                if any(0 <= y + dy < h and 0 <= x + dx < w and cur[y + dy][x + dx] for dx, dy in n): nxt[y][x] = True; edge[y][x] = True
        cur = nxt
    return g, edge

def save(name, white, black=None, invert=False):
    h, w = len(white), len(white[0]); im = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    for y in range(h):
        for x in range(w):
            if white[y][x]: im.putpixel((x, y), K if invert else W)
            elif black and black[y][x]: im.putpixel((x, y), W if invert else K)
    im.save(os.path.join(here, name.replace(' ', '_') + '.png'))

def plus(n, t):
    m = n / 2
    return [[(abs(x + 0.5 - m) < t / 2) or (abs(y + 0.5 - m) < t / 2) for x in range(n)] for y in range(n)]

def disc(n, r): return sym(n, lambda x, y: x * x + y * y <= r * r)

# dot: a small round dot with a dark edge
save('dot', *outline(disc(4, 1.9), corners=False))
# square: four by four
save('square', [[True] * 4 for _ in range(4)])
# circle: a thin ring with a dark edge inside and out
ring = grid(['...WWWW...', '.WW....WW.', '.W......W.', 'W........W', 'W........W', 'W........W', 'W........W', '.W......W.', '.WW....WW.', '...WWWW...'])
save('circle', *outline(ring, corners=False))
# pinhole: a dark disc with a light point in the middle
save('pinhole', disc(8, 0.8), disc(8, 3.7))
# diamond and dark diamond
dia = sym(6, lambda x, y: abs(x) + abs(y) <= 3.0)
save('diamond', *outline(dia))
save('dark diamond', *outline(dia), invert=True)
# plus, outlined plus, chunky plus
save('plus', plus(10, 2))
save('outlined plus', *outline(plus(14, 2), corners=True))
save('chunky plus', *outline(plus(6, 2), 2, corners=True))
# cross: four arms around a gap
c = plus(12, 2); g = [[c[y][x] and not (4 <= x < 8 and 4 <= y < 8) for x in range(12)] for y in range(12)]
save('cross', g)
# star: four points, wide in the middle
save('star', grid(['....WW....', '....WW....', '...WWWW...', '..WWWWWW..', 'WWWWWWWWWW', 'WWWWWWWWWW', '..WWWWWW..', '...WWWW...', '....WW....', '....WW....']))
# brackets: ( . ) with a dot in the middle
B = ['..WW', '.WW.', '.WW.', 'WW..', 'WW..', 'WW..', 'WW..', 'WW..', 'WW..', 'WW..', 'WW..', 'WW..', 'WW..', '.WW.', '.WW.', '..WW']
w, h = 28, len(B); br = [[False] * w for _ in range(h)]
for y, r in enumerate(B):
    for x, ch in enumerate(r):
        if ch == 'W': br[y][x] = True; br[y][w - 1 - x] = True
for y in (7, 8):
    for x in (13, 14): br[y][x] = True
save('brackets', br)

cases = []
for f in sorted(os.listdir(here)):
    if f.endswith('.png'):
        cases.append('                case "%s": return "%s";' % (f[:-4].replace('_', ' '), base64.b64encode(open(os.path.join(here, f), 'rb').read()).decode()))
open(os.path.join(here, '..', 'src', 'Images.cs'), 'w').write('''// ezCrosshair - a STRAFTAT mod (BepInEx 5). Dorsth was here.
namespace EzCrosshair
{
    // The crosshairs, drawn pixel by pixel by art/make.py, kept here so the mod is a single dll.
    internal static class Images
    {
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
''' % '\n'.join(cases))
