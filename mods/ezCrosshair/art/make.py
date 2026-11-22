# Writes src/Images.cs from the crosshair pictures in art/first, exactly as they are.
# The brackets are the game's own reticle (30x18), white where the game's is cream; the rest are the first pictures (1.0).
import base64, os
here = os.path.dirname(os.path.abspath(__file__))
src = os.path.join(here, 'first')
cases = []
for f in sorted(os.listdir(src)):
    if f.endswith('.png'):
        cases.append('                case "%s": return "%s";' % (f[:-4].replace('_', ' '), base64.b64encode(open(os.path.join(src, f), 'rb').read()).decode()))
open(os.path.join(here, '..', 'src', 'Images.cs'), 'w').write('''// ezCrosshair - a STRAFTAT mod (BepInEx 5). Dorsth was here.
namespace EzCrosshair
{
    // The crosshairs (art/first), kept here so the mod is a single dll.
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
