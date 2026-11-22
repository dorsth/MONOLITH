# Writes src/Images.cs from the crosshair pictures in art/first (the pictures as they were made, 1.0).
# Each is made even on both sides: a pixel and its mirror images left-right and up-down are averaged, so a soft edge
# is as soft on one side as on the other. Nothing else changes: same size, same pixels where they were already even.
import base64, io, os
from PIL import Image
here = os.path.dirname(os.path.abspath(__file__))
src, out = os.path.join(here, 'first'), here

def even(im):
    im = im.convert('RGBA'); w, h = im.size; p = im.load(); o = Image.new('RGBA', (w, h)); q = o.load()
    for y in range(h):
        for x in range(w):
            cs = [p[x, y], p[w - 1 - x, y], p[x, h - 1 - y], p[w - 1 - x, h - 1 - y]]
            a = sum(c[3] for c in cs)
            q[x, y] = tuple(round(sum(c[i] * c[3] for c in cs) / a) for i in range(3)) + (round(a / 4),) if a else (0, 0, 0, 0)
    return o

cases = []
for f in sorted(os.listdir(src)):
    if not f.endswith('.png'): continue
    im = even(Image.open(os.path.join(src, f)))
    b = io.BytesIO(); im.save(b, 'PNG'); im.save(os.path.join(out, f))
    cases.append('                case "%s": return "%s";' % (f[:-4].replace('_', ' '), base64.b64encode(b.getvalue()).decode()))
open(os.path.join(here, '..', 'src', 'Images.cs'), 'w').write('''// ezCrosshair - a STRAFTAT mod (BepInEx 5). Dorsth was here.
namespace EzCrosshair
{
    // The crosshairs (art/first, made even on both sides by art/make.py), kept here so the mod is a single dll.
    internal static class Images
    {
        internal const int Density = 1;

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
