#!/usr/bin/env python3
"""Печатает PNG-иконки игры из SVG макета.

Боковой рейл главного меню в макете нарисован инлайновыми SVG, а Unity UI
Toolkit векторов не принимает — только текстуру. До этого инструмента текстуры
были нарисованы отдельно и разошлись с макетом: у шестерни были зубья вместо
обода с лучами, ключ был другой формы, толщина линии не совпадала ни с чем.

Инструмент растеризует ровно те SVG, что стоят в разметке макета, поэтому
расхождение больше не накапливается: правка иконки в макете переносится в игру
одним прогоном. Растр белый — цвет и состояние задаёт USS через
-unity-background-image-tint-color, как у любого другого глифа.

    python3 tools/emit-icon-textures.py          # напечатать
    python3 tools/emit-icon-textures.py --check   # игра совпадает с макетом?

Кнопка рейла может рисовать глиф и напрямую, и ссылкой <use> в общий спрайт;
во втором случае спрайт подклеивается к SVG перед растеризацией, иначе ссылка
никуда не ведёт и получается пустая картинка.
"""

import hashlib
import io
import pathlib
import re
import sys

import cairosvg

ROOT = pathlib.Path(__file__).resolve().parent.parent
REPO = ROOT.parent.parent
INDEX = ROOT / "index.html"
OUT = REPO / "Assets" / "Textures" / "UI"
SIZE = 128

# Кнопка рейла в разметке макета → имя текстуры в игре. Ключ — подстрока,
# по которой кнопка опознаётся однозначно (обработчик клика).
BUTTONS = {
    "openModal('chronicleModal')": "mm_icon_chronicle",
    "openModal('settingsModal')": "mm_icon_settings",
    "openModal('repairModal')": "mm_icon_repair",
    "window.open('https://discord.com'": "mm_icon_discord",
    "window.open('https://telegram.org'": "mm_icon_telegram",
    "window.open('https://vk.com'": "mm_icon_vk",
    "openMandatoryUpdateModal()": "mm_icon_update",
    "confirmQuit()": "mm_icon_exit",
}


def sprite(html):
    start = html.index('<svg class="fdn-sprite"')
    return html[start:html.index("</svg>", start) + len("</svg>")]


def extract():
    html = INDEX.read_text(encoding="utf-8")
    rail = html[html.index('<aside class="fdn-rail">'):html.index("</aside>")]
    library = sprite(html)
    out = {}
    for button in re.split(r"(?=<button )", rail):
        for marker, name in BUTTONS.items():
            if marker not in button:
                continue
            svg = re.search(r"<svg\b.*?</svg>", button, re.S)
            if svg is None:
                raise SystemExit(f"в кнопке {name} нет svg")
            markup = svg.group(0)
            if "<use " in markup:
                markup = markup.replace("<use ", library + "<use ", 1)
            out[name] = markup
    missing = set(BUTTONS.values()) - set(out)
    if missing:
        raise SystemExit(f"не найдены кнопки: {', '.join(sorted(missing))}")
    return out


def render(svg):
    # currentColor берётся из color на корне: печатаем белым, цвет накладывает USS.
    svg = svg.replace("<svg", '<svg color="white" xmlns="http://www.w3.org/2000/svg" '
                      'xmlns:xlink="http://www.w3.org/1999/xlink"', 1)
    # cairosvg понимает только xlink:href, браузер — только href: даём оба.
    svg = re.sub(r'<use href="([^"]+)"', r'<use href="\1" xlink:href="\1"', svg)
    return cairosvg.svg2png(bytestring=svg.encode("utf-8"),
                            output_width=SIZE, output_height=SIZE)


def main() -> None:
    check = "--check" in sys.argv
    diverged = []
    for name, svg in sorted(extract().items()):
        png = render(svg)
        path = OUT / f"{name}.png"
        if check:
            old = path.read_bytes() if path.exists() else b""
            if hashlib.sha256(old).digest() != hashlib.sha256(png).digest():
                diverged.append(name)
            continue
        path.write_bytes(png)
        print(f"  {path.relative_to(REPO)}  {len(png)} байт")

    if check:
        if diverged:
            print("иконки игры разошлись с макетом: " + ", ".join(diverged))
            raise SystemExit(1)
        print(f"иконки игры совпадают с макетом ({len(BUTTONS)} шт.)")
    else:
        print(f"напечатано {len(BUTTONS)} иконок из макета")


if __name__ == "__main__":
    main()
