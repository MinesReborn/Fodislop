# Запуск из корня репозитория: python3 -I visual/kern-ui/tools/emit-hud-icons.py .
# Иконки HUD из SVG-спрайта макета (visual/kern-ui/index.html, <symbol id="i-…">) → Assets/Resources/UI/Theme/Kern/Icons/Hud/<имя>.svg
# Белым: цвет даёт USS (-unity-background-image-tint-color). Атрибуты корня — правило «svg» из hud.css.
import re, sys, uuid, pathlib
repo = pathlib.Path(sys.argv[1])
html = (repo / 'visual/kern-ui/index.html').read_text()
out = repo / 'Assets/Resources/UI/Theme/Kern/Icons/Hud'
out.mkdir(parents=True, exist_ok=True)
meta_tpl = (repo / 'Assets/Resources/UI/Theme/Kern/Icons/exit.svg.meta').read_text()
FILLED = {'play', 'stop'}   # .prog-run svg, .prog-go svg — заливка, без обводки
n = 0
for m in re.finditer(r'<symbol id="i-([a-z0-9-]+)" viewBox="([^"]+)">(.*?)</symbol>', html, re.S):
    name, vb, body = m.groups()
    body = body.replace('currentColor', '#ffffff')
    if name in FILLED:
        root = f'fill="#ffffff" stroke="none"'
    else:
        root = 'fill="none" stroke="#ffffff" stroke-width="1.5" stroke-linecap="square" stroke-linejoin="miter"'
    svg = f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{vb}" {root}>{body}</svg>\n'
    p = out / f'{name}.svg'
    if not p.exists() or p.read_text() != svg:
        p.write_text(svg)
    meta = p.with_name(p.name + '.meta')
    if not meta.exists():
        meta.write_text(re.sub(r'guid: [0-9a-f]+', 'guid: ' + uuid.uuid4().hex, meta_tpl, count=1))
    n += 1
folder_meta = out.with_name('Hud.meta')
if not folder_meta.exists():
    folder_meta.write_text(f'fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
print(n, 'icons')
# Лист классов: .ico — значок 20×20 цвета текста (тон --ash, как svg макета), .ico--<имя> — картинка
names = sorted(m.group(1) for m in re.finditer(r'<symbol id="i-([a-z0-9-]+)"', html))
uss = ['/* Значки макета visual/kern-ui (SVG-спрайт index.html, <symbol id="i-…">) — векторные картинки Icons/Hud/<имя>.svg.',
       '   Сгенерировано выгрузкой значков; руками не править. Цвет — -unity-background-image-tint-color (белый SVG). */', '',
       '.ico {', '    width: 20px;', '    height: 20px;', '    flex-shrink: 0;', '    -unity-background-scale-mode: scale-to-fit;',
       '    -unity-background-image-tint-color: var(--ash);', '}', '']
uss += [f'.ico--{n} {{ background-image: resource("UI/Theme/Kern/Icons/Hud/{n}"); }}' for n in names]
up = repo / 'Assets/Resources/UI/Theme/Kern/Styles/Icons.uss'
up.write_text('\n'.join(uss) + '\n')
um = up.with_name('Icons.uss.meta')
if not um.exists():
    um.write_text((repo / 'Assets/Resources/UI/Theme/Kern/Styles/Kern.uss.meta').read_text().replace(
        re.search(r'guid: ([0-9a-f]+)', (repo / 'Assets/Resources/UI/Theme/Kern/Styles/Kern.uss.meta').read_text()).group(1), uuid.uuid4().hex))
print('Icons.uss', len(names))
