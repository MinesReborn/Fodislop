#!/usr/bin/env python3
"""Перенос инлайн-оформления из разметки в правила экранов.

Почему не утилитами. Замер: 98 применений дают 92 РАЗНЫХ набора объявлений —
повторяется почти ничего, и правило components.css «класс заводим на паттерн,
встреченный дважды» здесь не даёт почти ни одного класса. Попытка разложить
наборы на существующие утилиты (.fdn-text--*, .fdn-font-*) была проведена и
измерена: 1354 расхождения вычисленных стилей. Причина не в утилитах, а в
специфичности — инлайн выигрывает у всего, утилита проигрывает правилу экрана.

Поэтому перенос по принадлежности: каждый набор становится ИМЕНОВАННЫМ
правилом в файле того экрана, которому элемент принадлежит. Имя выводится из
контекста (экран/модалка + роль элемента), а не придумывается.

Граница «оформление / данные». Инлайн остаётся только там, где значение
вычисляет программа, а не выбирает автор: ширина полосы прогресса, координаты
точки на радаре, начальное display у скрытой ветки. Это не тема, это состояние.
"""
import json
import pathlib
import re
import sys

# .parent.parent.parent: файл лежит в tools/derived, корень макета — на два выше.
ROOT = pathlib.Path(__file__).resolve().parent.parent.parent
HTML = ROOT / "index.html"

# Свойства, значение которых вычисляет программа, а не выбирает автор.
DATA_PROPS = {"width", "height", "left", "top", "transform"}


def is_data(decls):
    """Набор — данные, если все свойства геометрические И хотя бы одно значение
    относительное или нулевое: авторская ширина карточки (680px) — это тема,
    ширина полосы 71% — это состояние."""
    props = {d.split(":")[0].strip() for d in decls}
    if not props <= DATA_PROPS | {"display", "position"}:
        return False
    return any("%" in d for d in decls) or decls == ["display:none"]


def slug(text):
    text = re.sub(r"[^a-zA-Zа-яА-Я0-9]+", "-", text.strip().lower())
    return re.sub(r"^-+|-+$", "", text)


TRANSLIT = str.maketrans({
    "а": "a", "б": "b", "в": "v", "г": "g", "д": "d", "е": "e", "ё": "e", "ж": "zh",
    "з": "z", "и": "i", "й": "y", "к": "k", "л": "l", "м": "m", "н": "n", "о": "o",
    "п": "p", "р": "r", "с": "s", "т": "t", "у": "u", "ф": "f", "х": "h", "ц": "c",
    "ч": "ch", "ш": "sh", "щ": "sch", "ъ": "", "ы": "y", "ь": "", "э": "e",
    "ю": "yu", "я": "ya",
})


class Scan(HTMLParser := __import__("html.parser", fromlist=["HTMLParser"]).HTMLParser):
    """Собирает элементы со style= вместе с их происхождением.

    Происхождение нужно для имени: класс называется по владельцу (экран или
    модалка) и по роли элемента, а не по порядковому номеру — иначе имя не
    сообщает ничего, и мы просто переименовали проблему.
    """

    def __init__(self):
        super().__init__(convert_charrefs=False)
        self.stack = []
        self.hits = []

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        owner = None
        for t, ta in reversed(self.stack):
            if ta.get("id"):
                owner = ta["id"]
                break
        if "style" in a:
            self.hits.append({
                "tag": tag,
                "line": self.getpos()[0],
                "owner": owner,
                "cls": a.get("class", ""),
                "key": a.get("data-i18n", ""),
                "id": a.get("id", ""),
                "style": a["style"],
                "parent_cls": self.stack[-1][1].get("class", "") if self.stack else "",
            })
        if tag not in ("br", "hr", "img", "input", "use", "meta", "link", "path", "circle"):
            self.stack.append((tag, a))

    def handle_endtag(self, tag):
        for i in range(len(self.stack) - 1, -1, -1):
            if self.stack[i][0] == tag:
                del self.stack[i:]
                break


def main():
    src = HTML.read_text(encoding="utf-8")
    p = Scan()
    p.feed(src)
    rows = []
    for h in p.hits:
        decls = [d.strip() for d in h["style"].split(";") if d.strip()]
        rows.append({**h, "decls": decls, "data": is_data(decls)})
    print(json.dumps(rows, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    main()
