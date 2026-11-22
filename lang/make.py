# Makes ../Russian.cs from ru.py and checks every Russian text against the room the menu has for it.
# Run: python3 lang/make.py   (build.sh runs it; a text that does not fit stops the build)
import os, re, sys
here = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, here)
import ru
INFO_W, INFO_LINES = 75, 3            # the box under the pages: 3 lines of 75 cells
BAR_W = 80                            # the key bar: the whole width of the menu, shared evenly by its slots
DEFAULT_TAIL = ' По умолчанию: #FFFFFFFF. Сброс'   # the longest thing a row adds after its own line

def wrap(s, width=INFO_W):
    lines, cur = [], ''
    for word in s.split(' '):
        if len(cur) + len(word) + 1 > width and cur != '': lines.append(cur); cur = ''
        cur = word if cur == '' else cur + ' ' + word
    if cur: lines.append(cur)
    return lines

bad = []; table = {}
for en, r, check in ru.T:
    if en in table and table[en] != r: bad.append('twice, differently: ' + en)
    table[en] = r
    if "'" in r or '—' in r or '·' in r: bad.append('apostrophe, dash or middle dot: ' + r)
    if check == 'nav' and len(r) > 16: bad.append('category over 16: %s (%d)' % (r, len(r)))
    m = re.fullmatch(r'[LW](\d+)', check)
    if m and len(r) > int(m.group(1)): bad.append('%s over %s: %s (%d)' % (check[0], m.group(1), r, len(r)))
    if check in ('tip', 'tipb', 'info'):
        text = r + (DEFAULT_TAIL if check in ('tip', 'tipb') else '')
        n = len(wrap(text))
        if n > INFO_LINES: bad.append('%d lines: %s' % (n, r))
        if max(len(w) for w in text.split(' ')) > INFO_W: bad.append('a word over the box: ' + r)
for bar in ru.BARS:
    # each key and its word in its slot: the bar's width shared evenly
    for k, w in bar:
        if len(table.get(k, k)) + len(table.get(w, w)) > BAR_W // len(bar): bad.append('key bar slot over %d: %s %s' % (BAR_W // len(bar), table.get(k, k), table.get(w, w)))
# every English text the menu's own files show should be in the table
missing = []
for f in ['Layout.cs', 'Profiles.cs']:
    src = open(os.path.join(here, '..', f), encoding='utf-8').read()
    for m in re.finditer(r'(?:Set|Pick|Key|All|Head|MenuPage)\(\s*(?:\w+,\s*)?"((?:[^"\\]|\\.)*)"(?:\s*,\s*"((?:[^"\\]|\\.)*)")?', src):
        for g in m.groups():
            if g and g.strip() and g.strip() not in table and g not in table and not re.fullmatch(r'[A-Za-z]+ \d+|search|\.\.|Taunt|Weapon', g.strip()): missing.append(f + ': ' + g)
    for m in re.finditer(r'(?:Label|Info|SideTitle) = "((?:[^"\\]|\\.)+)"', src):
        g = m.group(1)
        if g.endswith('\\"') or g == 'Dorsth': continue          # a line put together with a name; a name
        if g not in table and g.strip() not in table: missing.append(f + ': ' + g)
if bad or missing:
    for b in bad: print('DOES NOT FIT   ' + b)
    for x in sorted(set(missing)): print('NOT TRANSLATED ' + x)
    sys.exit(1)

def cs(s): return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'
out = ['// MONOLITH - the menu in Russian. MADE by lang/make.py from lang/ru.py: change it there, not here. Dorsth was here.',
       'using System.Collections.Generic;', '', 'namespace Monolith', '{', '    internal static partial class Lang', '    {',
       '        private static readonly Dictionary<string, string> Table = new Dictionary<string, string>(%d)' % len(table), '        {']
for en, r in table.items(): out.append('            { %s, %s },' % (cs(en), cs(r)))
out += ['        };', '    }', '}', '']
open(os.path.join(here, '..', 'Russian.cs'), 'w', encoding='utf-8', newline='\n').write('\n'.join(out))
print('Russian.cs: %d texts, all fit' % len(table))
