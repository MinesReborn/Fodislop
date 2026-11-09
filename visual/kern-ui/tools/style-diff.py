# Снимает отпечаток стилей (tools/style-snap.html) и сравнивает с базой.
# python3 -I tools/style-diff.py base  — сохранить базу; без аргумента — сравнить с базой.
import html, json, pathlib, re, subprocess, sys
from collections import Counter

here = pathlib.Path(__file__).resolve().parent
base = here.parent / 'shots/style-base.json'
PROPS = ['color', 'background-color', 'background-image', 'border-top-color', 'border-right-color', 'border-bottom-color', 'border-left-color',
         'border-top-left-radius', 'border-bottom-right-radius', 'box-shadow', 'text-shadow', 'outline-color', 'filter', 'font-size', 'font-weight', 'line-height',
         'z-index', 'transition', 'opacity',
         # раскладка и рамка элемента (x,y,ш,в) — как в tools/style-snap.html
         'display', 'position', 'width', 'height', 'padding-top', 'padding-right', 'padding-bottom', 'padding-left', 'margin-top', 'margin-right', 'margin-bottom', 'margin-left', 'row-gap', 'column-gap', 'grid-template-columns', 'grid-template-rows', 'flex-direction', 'flex-wrap', 'flex-grow', 'flex-shrink', 'flex-basis', 'align-items', 'justify-content', 'border-top-width', 'border-left-width', 'border-bottom-width', 'letter-spacing', 'font-family', 'text-transform', 'clip-path', 'transform', 'visibility', 'overflow-x', 'white-space', 'box']

dom = subprocess.run(['/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '--headless=new', '--disable-gpu', '--virtual-time-budget=240000',
                      '--dump-dom', 'http://127.0.0.1:8765/Fodislop/visual/kern-ui/tools/style-snap.html'], capture_output=True, text=True).stdout
snap = json.loads(html.unescape(re.search(r'<pre id="out">(.*?)</pre>', dom, re.S).group(1)))
print('элементов', sum(len(r) for r in snap.values()))
if sys.argv[1:] == ['base']:
    base.parent.mkdir(exist_ok=True)
    base.write_text(json.dumps(snap), 'utf-8')
    sys.exit()
old = json.loads(base.read_text('utf-8'))
kinds, missing = Counter(), 0
for u, rows in old.items():
    for k, v in rows.items():
        n = snap.get(u, {}).get(k)
        if n is None:
            missing += 1
            continue
        for p, a, b in zip(PROPS, v.split('|'), n.split('|')):
            if a != b:
                kinds[(p, a, b)] += 1
print('пропало элементов', missing)
for (p, a, b), c in kinds.most_common():
    print(f'{c:5} {p}: {a}  →  {b}')
# Пути элементов для одного вида: python3 -I tools/style-diff.py where <свойство> <было>
if sys.argv[1:2] == ['where']:
    i = PROPS.index(sys.argv[2])
    for u, rows in old.items():
        for k, v in rows.items():
            n = snap.get(u, {}).get(k)
            if n and v.split('|')[i] != n.split('|')[i] and v.split('|')[i].startswith(sys.argv[3]):
                print(u, k[-120:], n.split('|')[i])
