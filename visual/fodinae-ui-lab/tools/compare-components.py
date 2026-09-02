#!/usr/bin/env python3
"""Сверяет вид компонентов игры с макетом свойство за свойством.

Токены игра и макет уже делят (emit-uss-tokens.py), но токен — это словарь, а
не текст: одним и тем же набором значений можно собрать разные экраны. Пока
сверялся только словарь, кнопка рейла спокойно жила 44 пикселя против 48 в
макете, с рамкой полуторной вместо одинарной и почти нечитаемым наведением.

Здесь сверяется уже сказанное: для каждой пары из component-map.json берутся
правила CSS макета и USS игры и сравниваются по свойствам. Сравнение
осмысленно только там, где свойство есть у обоих: чего игра не сказала, то
она отдала на откуп теме, и это не расхождение.

    python3 tools/compare-components.py            # отчёт
    python3 tools/compare-components.py --check    # расхождений не больше потолка

Потолок живёт в docs/design-component-drift.md — файл машинный, его печатает
этот же инструмент.
"""

import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
REPO = ROOT.parent.parent
MAP = ROOT / "component-map.json"
GAME_STYLES = REPO / "Assets" / "Resources" / "Styles"
OUT = REPO / "docs" / "design-component-drift.md"

# Значение сравнивается разрешённым: var(--space-6) в игре и 12px в макете —
# одно и то же, и ловить такую «разницу» значит топить настоящие находки в
# шуме. Обе палитры раскрываются до конечных значений, циклы обрываются.
def resolve(value, palette, depth=0):
    if depth > 8:
        return value
    def one(match):
        name = match.group(1)
        return resolve(palette[name], palette, depth + 1) if name in palette else match.group(0)
    return re.sub(r"var\(\s*(--[\w-]+)\s*\)", one, value)


def palette_of(paths):
    out = {}
    for path in paths:
        text = re.sub(r"/\*[\s\S]*?\*/", " ", path.read_text(encoding="utf-8"))
        for name, value in re.findall(r"(--[\w-]+)\s*:\s*([^;{}]+)", text):
            out.setdefault(name.strip(), " ".join(value.split()))
    return out


# Свойство CSS → свойство USS. Всё, чего здесь нет, сравнивается по имени.
ALIAS = {
    "background": "background-color",
    "font-family": "-unity-font-definition",
    "font-weight": "-unity-font-style",
    "text-align": "-unity-text-align",
}

# Свойства, которых в USS нет вовсе либо которые здесь заведомо разойдутся:
# сравнивать их — шуметь. Причина у каждого своя, поэтому список именной.
SKIP = {
    "display", "position", "cursor", "overflow", "z-index", "gap",
    # box-shadow и filter больше не пропускаются: с 6000.6 у UI Toolkit есть
    # filter: drop-shadow(), и тень стала переносимой. expand_box приводит
    # запись макета к записи игры, деля радиус размытия пополам.
    "transform", "transition", "animation",
    "pointer-events", "user-select", "content", "grid-template-columns",
    "grid-template-rows", "place-items", "line-height", "letter-spacing",
    "text-transform", "flex", "inset", "will-change", "isolation", "opacity",
}

# Ось текста. Здесь сравнение работает иначе, чем везде: обычно свойство,
# названное макетом и НЕ названное игрой, проходит молча — игра вправе не
# повторять всё подряд, а пересечение свойств отсекает шум сокращений
# (background против background-color, font-family против
# -unity-font-definition).
#
# Для поведения при нехватке места это правило неверно. Молчание здесь — не
# «не сказано», а «текст вылезет»: умолчание USS никогда не обрежет и не
# ужмёт строку. Поэтому по этой оси молчание игры — расхождение.
#
# Ось узкая намеренно: сплошная проверка «макет сказал, игра нет» даёт 189
# строк, и почти все — те самые сокращения.
FIT_AXIS = {
    "white-space", "text-overflow", "-unity-text-overflow-position",
    "max-width", "flex-shrink", "-unity-text-auto-size",
}


def rules(paths):
    """Селектор → накопленные свойства. Селектор нормализуется: тег перед
    классом снимается (Button.mm-btn-gold — тот же компонент, что
    .mm-btn-gold), пробелы схлопываются. Состояния и потомки остаются
    селекторами: сверять их — половина работы, вид складывается из покоя
    и реакции."""
    out = {}
    for path in paths:
        text = re.sub(r"/\*[\s\S]*?\*/", " ", path.read_text(encoding="utf-8"))
        for match in re.finditer(r"([^{}]+)\{([^{}]*)\}", text):
            selectors = match.group(1)
            if "@" in selectors or ":root" in selectors:
                continue
            body = {}
            for decl in match.group(2).split(";"):
                if ":" not in decl:
                    continue
                prop, value = decl.split(":", 1)
                body[prop.strip()] = " ".join(value.split())
            if not body:
                continue
            for selector in selectors.split(","):
                selector = " ".join(selector.split())
                selector = re.sub(r"(^|[\s>+~])[A-Za-z]\w*(?=[.:])", r"\1", selector)
                out.setdefault(selector, {}).update(body)
    return out


CLASS = re.compile(r"\.([\w-]+)")

# Селекторы, которые сравнивать нечем: атрибут и id (в USS их нет вовсе),
# составные псевдоклассы, псевдоэлементы, а также потомок-тег — в макете это
# .side-icon-btn svg, а в игре у глифа свой класс, потому что элемента svg
# в UI Toolkit не существует.
def comparable(selector):
    if any(ch in selector for ch in "[#(") or "::" in selector:
        return False
    return not re.search(r"(^|[\s>+~])[A-Za-z]\w*\s*$", selector)


def to_mirror(selector, translate):
    """Селектор игры, переписанный именами макета. Так семейства двух сторон
    ложатся в одно пространство имён и сравниваются ключ в ключ. Класс вне
    карты пар делает селектор непереводимым — и это честнее догадки."""
    names = CLASS.findall(selector)
    if not names or any(name not in translate for name in names):
        return None
    return CLASS.sub(lambda m: "." + translate[m.group(1)], selector)


def families(game, mirror, pairs):
    """Ключ (селектор в именах макета) → (свойства игры, свойства макета).
    Ключ, названный только макетом, значит отсутствующую реакцию, а не спор
    о значении: править там нечего, правило надо завести."""
    translate = {g: m for section, entries in pairs.items() if section != "_"
                 for g, m in entries.items()}
    # Значение пары может быть составным: mm-nav-tab--active ↔
    # fdn-settings-tab.active. Модификатор игры — отдельный класс, у макета —
    # второй класс на том же узле, и без этого разбора состояния «активно»
    # не сверялось вовсе: там жила золотая вкладка против бирюзовой.
    #
    # Узнаётся при этом НАБОР классов целиком, а не каждое имя по отдельности.
    # Иначе «active», однажды попав в словарь, начинает узнавать и
    # .modal-overlay.active, и .route-item.active — пары, которых никто не
    # заводил, и отчёт требует завести правила там, где игра собрана иначе.
    known = {frozenset(value.split(".")) for value in translate.values()}
    left, right = {}, {}
    for selector, body in game.items():
        if not comparable(selector):
            continue
        key = to_mirror(selector, translate)
        if key:
            left.setdefault(key, {}).update(body)
    for selector, body in mirror.items():
        if not comparable(selector):
            continue
        parts = [frozenset(CLASS.findall(part))
                 for part in re.split(r"[\s>+~]+", selector) if CLASS.search(part)]
        if parts and all(part in known for part in parts):
            right.setdefault(selector, {}).update(body)
    return left, right


def canon_color(value):
    """rgba(11, 20, 30, 0.9) и rgb(11 20 30 / 90%) — одна запись двумя
    синтаксисами; сводим к одному, иначе отчёт состоит из них."""
    def one(match):
        parts = re.split(r"[,\s/]+", match.group(1).strip())
        parts = [p for p in parts if p]
        if len(parts) not in (3, 4):
            return match.group(0)
        try:
            rgb = [str(int(float(p))) for p in parts[:3]]
        except ValueError:
            return match.group(0)
        alpha = "1"
        if len(parts) == 4:
            alpha = parts[3]
            alpha = str(float(alpha[:-1]) / 100) if alpha.endswith("%") else str(float(alpha))
        return f"rgba({', '.join(rgb)}, {alpha})"
    value = re.sub(r"rgba?\(([^()]*)\)", one, value)
    hexed = re.fullmatch(r"#([0-9a-f]{6})", value)
    if hexed:
        r, g, b = (str(int(hexed.group(1)[i:i + 2], 16)) for i in (0, 2, 4))
        value = f"rgba({r}, {g}, {b}, 1)"
    return value


WEIGHT = {"bold": "700", "normal": "400", "bolder": "700", "lighter": "300"}


def normalize(prop, value, palette):
    prop = ALIAS.get(prop, prop)
    value = resolve(value, palette)
    value = value.replace("rgb(", "rgba(").strip().lower()
    value = re.sub(r"\b0px\b", "0", value)
    value = re.sub(r"\s*,\s*", ", ", value)
    value = canon_color(value)
    # USS требует называть и вертикаль: middle-left против css-ного left.
    if prop == "-unity-text-align":
        value = re.sub(r"^(?:upper|middle|lower)-", "", value)
    # 700 и bold — одно начертание, записанное двумя способами.
    value = " ".join(WEIGHT.get(part, part) for part in value.split())
    # Шрифт в игре — путь к SDF-ресурсу, в макете — семейство. Сравнимо только
    # то, какое из трёх начертаний выбрано, поэтому оставляем корень имени.
    if prop == "-unity-font-definition":
        asset = re.search(r"([\w]+)_sdf\.asset", value)
        if asset:
            value = asset.group(1)
        else:
            value = re.split(r"[,]", value)[0].strip("\"' ")
        value = value.split()[0] if value.split() else value
        # Exo2_SDF и "Exo 2" — одно семейство; JetBrainsMono и "JetBrains Mono"
        # тоже. Сравнивается выбор гарнитуры, а не написание её имени.
        value = re.sub(r"[^a-z]", "", value)
        for family in ("unbounded", "jetbrains", "exo"):
            if value.startswith(family):
                value = family
                break
    return prop, value


def owner(key, pairs):
    """Секция и пара, к которым относится селектор: смотрим на последнюю
    составляющую — оформляется именно она, остальное лишь уточняет условие."""
    tail = re.split(r"[\s>+~]", key)[-1]
    names = set(CLASS.findall(tail))
    for section, entries in pairs.items():
        if section == "_":
            continue
        for gname, mname in entries.items():
            if mname in names:
                return section, gname, mname
    return "—", key, key


# Рамку макет пишет сокращением (border: 1px solid X), а USS сокращения не
# знает и требует border-width/border-color по сторонам. Пока стороны не
# приводились к одному виду, ни одна рамка на парных компонентах не
# сравнивалась вовсе — тридцать одно объявление, то есть вся рамочная сетка
# меню: кнопки, шапка, футер, рейл, баннер, колонка настроек.
#
# Обе стороны раскладываются в стороны-долгие свойства. Стиль (solid/none)
# отбрасывается: в USS его нет, рамка либо нулевой ширины, либо есть.
SIDES = ("top", "right", "bottom", "left")


def _border_parts(value):
    """(ширина, цвет) из сокращения. none/0 — рамки нет."""
    value = value.strip()
    if value in ("none", "0", "0px"):
        return "0", None
    width, color = None, None
    for token in re.findall(r"[^\s(]+(?:\([^)]*\))?", value):
        if token in ("solid", "dashed", "dotted", "double", "groove", "ridge", "inset", "outset", "hidden"):
            continue
        if width is None and re.fullmatch(r"[\d.]+(px|em|rem)?", token):
            width = token
        else:
            color = token
    return width, color


# box-shadow макета и filter игры — одна и та же тень, записанная по-разному.
# Третий параметр Unity — СИГМА гауссианы, а CSS задаёт РАДИУС размытия, и по
# спецификации сигма равна его половине. Пока это не было записано в сверке,
# перенос числом в число прошёл бы молча и сделал каждую тень вдвое мягче.
def shadow_to_filter(value):
    out = []
    for part in re.split(r",(?![^()]*\))", value):
        part = " ".join(part.split())
        if not part or part == "none":
            return "none"
        tokens = re.findall(r"var\([^)]*\)|#[0-9a-fA-F]+|rgba?\([^)]*\)|-?[\d.]+px|-?[\d.]+|\S+", part)
        lengths = [t for t in tokens if t.endswith("px") or re.fullmatch(r"-?[\d.]+", t)]
        color = next((t for t in tokens if t not in lengths), None)
        if len(lengths) < 3 or color is None:
            return value
        x, y, blur = lengths[0], lengths[1], lengths[2]
        number = float(re.sub(r"px$", "", blur))
        sigma = number / 2
        sigma = f"{int(sigma)}px" if sigma == int(sigma) else f"{sigma}px"
        out.append(f"drop-shadow({x} {y} {sigma} {color})")
    return " ".join(out)


def expand_box(body):
    """Свойства правила, приведённые к сторонам-долгим именам."""
    out = {}
    for prop, value in body.items():
        if prop == "box-shadow":
            out["filter"] = shadow_to_filter(value)
            continue
        match = re.fullmatch(r"border(?:-(top|right|bottom|left))?", prop)
        if match:
            width, color = _border_parts(value)
            sides = (match.group(1),) if match.group(1) else SIDES
            for side in sides:
                if width is not None:
                    out[f"border-{side}-width"] = width
                if color is not None:
                    out[f"border-{side}-color"] = color
            continue
        match = re.fullmatch(r"border-(width|color)", prop)
        if match and len(value.split()) == 1:
            for side in SIDES:
                out[f"border-{side}-{match.group(1)}"] = value
            continue
        match = re.fullmatch(r"border-(top|right|bottom|left)-(width|color)", prop)
        if match:
            out[prop] = value
            continue
        if prop in ("padding", "margin"):
            # Стороны, а не строка: игра пишет margin-top и margin-bottom, макет
            # одним словом «8px 0 4px -20px», и без разбора это читалось как
            # пропуск. Ноль — умолчание обеих сторон, поэтому сравним честно.
            parts = value.split()
            if 1 <= len(parts) <= 4:
                top, right, bottom, left = (
                    parts * 4 if len(parts) == 1 else
                    (parts * 2 if len(parts) == 2 else
                     (parts + [parts[1]] if len(parts) == 3 else parts)))
                for side, side_value in zip(SIDES, (top, right, bottom, left)):
                    out[f"{prop}-{side}"] = side_value
                continue
        out[prop] = value
    return out


def collapse_sides(rows):
    """Четыре одинаковых расхождения по сторонам — одно расхождение рамки."""
    grouped = {}
    for row in rows:
        match = re.fullmatch(r"border-(top|right|bottom|left)-(width|color)", row[3])
        if not match:
            continue
        grouped.setdefault((row[6], match.group(2), row[4], row[5]), []).append(row)
    dropped, added = set(), []
    for (key, kind, was, want), group in grouped.items():
        if len(group) != len(SIDES):
            continue
        for row in group:
            dropped.add(id(row))
        head = group[0]
        added.append(head[:3] + (f"border-{kind}", was, want) + head[6:])
    return [row for row in rows if id(row) not in dropped] + added


def compare(game, mirror, pairs, game_palette, mirror_palette):
    left, right = families(game, mirror, pairs)
    base = {"." + mname for section, entries in pairs.items() if section != "_"
            for mname in entries.values()}
    drift, missing, checked = [], [], 0
    seen = set()

    for section, entries in pairs.items():
        if section == "_":
            continue
        for gname, mname in entries.items():
            key = "." + mname
            if key in left and key in right:
                continue
            drift.append((section, gname, mname, "—",
                          "нет правила" if key not in left else "есть",
                          "нет правила" if key not in right else "есть", key))

    for key in sorted(set(left) | set(right)):
        section, gname, mname = owner(key, pairs)
        gbody, mbody = left.get(key), right.get(key)
        if mbody is None:
            continue  # игра вправе сказать больше макета
        if gbody is None and " " in key:
            # Макет описывает элемент через родителя (.btn .arrow), игра — сама
            # по себе (.mm-btn-primary-arrow-box). Это одно правило, записанное
            # с разной точностью, а не отсутствующая реакция.
            gbody = left.get("." + re.split(r"[\s>+~]", key)[-1].lstrip("."))
        if gbody is None:
            # Реакция, целиком собранная из невыразимого в USS (одна тень и
            # ничего больше), — не пропущенное правило: заводить нечего.
            # Требовать его значит требовать слово вместо поведения.
            if key not in base and not all(
                    prop in SKIP or why_unfixable(prop, "", " ".join(value.split()))
                    for prop, value in mbody.items()):
                missing.append((section, gname, mname, key))
            continue
        g = dict(normalize(p, v, game_palette) for p, v in expand_box(gbody).items())
        m = dict(normalize(p, v, mirror_palette) for p, v in expand_box(mbody).items())
        for prop in sorted(set(g) & set(m)):
            if prop in SKIP or prop.startswith("--"):
                continue
            checked += 1
            if g[prop] != m[prop] and (key, prop) not in seen:
                seen.add((key, prop))
                drift.append((section, gname, mname, prop, g[prop], m[prop], key))
        for prop in sorted(set(m) - set(g)):
            if prop not in FIT_AXIS or (key, prop) in seen:
                continue
            # Потолок, поставленный жёсткой шириной, — тот же потолок, только
            # строже. Требовать вдобавок max-width значит требовать слово, а
            # не поведение.
            if prop == "max-width" and g.get("width") == m[prop]:
                continue
            checked += 1
            seen.add((key, prop))
            drift.append((section, gname, mname, prop, "не сказано", m[prop], key))
    return collapse_sides(drift), missing, checked


# Часть расхождений не может быть устранена в принципе: USS — не CSS.
# Каждое такое расхождение получает причину, и только они имеют право
# оставаться; всё остальное — долг, который сводится к нулю.
SCENE_SECTION = "фон и планета"
PLACEMENT = {"left", "top", "right", "bottom", "width", "height"}


def why_unfixable(prop, game_value, mirror_value, section=""):
    if "gradient" in mirror_value:
        return "градиентов в USS нет: заливка только сплошная"
    if prop == "-unity-font-style" and mirror_value not in ("400", "700"):
        return f"USS знает только normal и bold; вес {mirror_value} недостижим"
    if re.search(r"\d(vh|vw|em|rem|ch)\b", mirror_value):
        return "относительных единиц в USS нет"
    if game_value in ("нет правила", "есть") or mirror_value in ("нет правила", "есть"):
        return "компонент собран иначе: у одной стороны отдельного правила нет"
    if section == SCENE_SECTION and prop in PLACEMENT:
        # Планета, маркеры и станция стоят там, куда их ставит сцена: код
        # проецирует 3D-точку на кадр и пишет координаты инлайном. В макете
        # той же сцены нет, и числа там — рисунок статичной картинки.
        return "положение задаёт сцена, а не вёрстка"
    return None


# ── Молчание игры ────────────────────────────────────────────────────────────
#
# Свойство, названное макетом и НЕ названное игрой, отчёт долго пропускал:
# «чего игра не сказала, то она отдала теме». Для цвета и рамки это неверно —
# умолчание темы не нарисует ни фона, ни границы, и четыре коробки (карточка
# деталей сервера, шапка профиля, две плашки хроники) жили в игре невидимыми,
# пока сверка молчала вместе с ними.
#
# Обратное тоже верно: значительная часть молчания законна, и без разбора
# причин отчёт состоит из ложных срабатываний. Причин ровно четыре, и каждая
# проверяется, а не предполагается.
UXML = REPO / "Assets" / "Resources" / "UI" / "MainMenu.uxml"

# Шрифт, кегль и цвет макет объявляет на контейнере и раздаёт наследованием,
# игра — на каждом листе: Label внутри кнопки, а не сама кнопка. Один и тот же
# вид, записанный на разной высоте дерева.
INHERITED = {"color", "font-size", "-unity-font-definition", "-unity-font-style"}

# Умолчания USS, отличные от CSS: колонка вместо строки, растяжение вместо
# авто-ширины. Сказать это второй раз — не уточнение, а шум.
USS_DEFAULT = {
    "flex-direction": {"column"},
    "width": {"100%"},
    "height": {"100%"},
    "align-items": {"center"},      # у подписи текст ставит -unity-text-align
    "justify-content": {"center"},
    "list-style": {"none"},
}


def node_siblings():
    """Класс игры → классы, стоящие с ним на одном узле разметки.

    Игра собирает вид из нескольких классов на элементе (mm-card-box плюс
    mm-update-hero-box), и свойство, названное соседом, названо. Без этого
    разбора пять объявлений одной плашки читались как пропуск."""
    if not UXML.exists():
        return {}
    text = UXML.read_text(encoding="utf-8")
    out = {}
    for attr in re.findall(r'class="([^"]+)"', text):
        names = [c for c in attr.split() if c.startswith("mm-")]
        for name in names:
            out.setdefault(name, set()).update(n for n in names if n != name)
    return out


def why_silent_ok(prop, value, gbody, sibling_body):
    if prop in SKIP or prop.startswith("--"):
        return "не сверяется"
    if "gradient" in value or "calc(" in value:
        return "неустранимо в USS"
    if re.search(r"\d(vh|vw|em|rem|ch)\b", value):
        return "относительных единиц в USS нет"
    if prop in ("overflow-y", "overflow-x"):
        return "прокрутка в UI Toolkit — ScrollView, а не свойство"
    if prop == "text-decoration":
        return "в USS нет"
    if prop in INHERITED:
        return "объявлено ниже по дереву: макет наследует, игра пишет на листе"
    if value in USS_DEFAULT.get(prop, ()):
        return "умолчание USS совпадает"
    if prop in sibling_body:
        return "названо соседним классом на том же узле"
    if prop == "max-width" and gbody.get("width") == value:
        return "потолок задан жёсткой шириной — строже, но то же"
    if prop in ("padding-right", "padding-left", "padding-top", "padding-bottom",
                "margin-right", "margin-left", "margin-top", "margin-bottom") \
            and value.strip() in ("0", "0px"):
        return "ноль — умолчание USS"
    return BUILT_DIFFERENTLY.get((gbody.get("__name__", ""), prop))


# Три места, где игра осознанно собрана иначе, чем макет. Список именной: каждая
# запись — решение, принятое и записанное в USS игры, а не забытое объявление.
BUILT_DIFFERENTLY = {
    ("mm-beacon-ping", "left"):
        "маяк сведён с прицелом в один элемент: кольцо центрирует родитель, "
        "а точки, от которой макет отсчитывает -5px, в игре нет",
    ("mm-beacon-ping", "top"):
        "то же: смещение относительно точки, которой в игре не существует",
    ("mm-target-cross-v", "left"):
        "перекрестье центрирует родитель (align-items/justify-content), "
        "а не left: 50% со сдвигом на половину — transform в USS нет",
    ("mm-target-cross-h", "top"):
        "то же: центрирование задано родителем, а не смещением",
    ("mm-planet-body", "border-radius"):
        "диск планеты нарисован в текстуре, а не скруглением коробки",
    ("mm-menu", "padding-top"):
        "колонка меню стоит абсолютно (left/top/bottom), а не отступами",
    ("mm-menu", "padding-bottom"):
        "то же: нижний край задан bottom, а не отступом",
    ("mm-menu", "padding-left"):
        "то же: левый край задан left, а не отступом",
}


def silences(left, right, pairs, mirror_palette, game_palette, game_rules):
    """Свойства, названные только макетом, с причиной у каждого законного."""
    siblings = node_siblings()
    m2g = {m: g for section, entries in pairs.items() if section != "_"
           for g, m in entries.items()}
    found = []
    for key in sorted(right):
        gbody = left.get(key)
        if gbody is None:
            continue
        head = re.split(r"[\s>+~]", key)[-1].lstrip(".").split(".")[0].split(":")[0]
        gname = m2g.get(head, "")
        sibling = {}
        for other in siblings.get(gname, ()):
            body = game_rules.get("." + other)
            if body:
                sibling.update(dict(normalize(p, v, game_palette)
                                    for p, v in expand_box(body).items()))
        g = dict(normalize(p, v, game_palette) for p, v in expand_box(gbody).items())
        g["__name__"] = gname
        m = dict(normalize(p, v, mirror_palette) for p, v in expand_box(right[key]).items())
        for prop, value in sorted(m.items()):
            if prop in g:
                continue
            if prop in ("padding", "margin") and all(
                    f"{prop}-{side}" in g for side in SIDES):
                continue
            if why_silent_ok(prop, value, g, sibling) is None:
                found.append((gname or "?", key, prop, value))
    return found


def main() -> None:
    pairs = json.loads(MAP.read_text(encoding="utf-8"))
    game = rules(sorted(GAME_STYLES.glob("*.uss")))
    mirror = rules(sorted(ROOT.glob("css/*.css")) + sorted(ROOT.glob("css/screens/*.css")))
    game_palette = palette_of(sorted(GAME_STYLES.glob("*.uss")))
    mirror_palette = palette_of(sorted(ROOT.glob("css/*.css")))
    drift, missing, checked = compare(game, mirror, pairs, game_palette, mirror_palette)
    left, right = families(game, mirror, pairs)
    quiet = silences(left, right, pairs, mirror_palette, game_palette, game)

    debt, unfixable = [], []
    for item in drift:
        reason = why_unfixable(item[3], item[4], item[5], item[0])
        (unfixable if reason else debt).append(item + (reason,))

    rows = "\n".join(
        f"| {section} | `{key}` | `.{mname}` | `{prop}` | `{was}` | `{want}` |"
        for section, gname, mname, prop, was, want, key, _ in debt
    ) or "| — | | | | | |"
    stuck = "\n".join(
        f"| {section} | `.{gname}` | `.{mname}` | `{prop}` | {reason} |"
        for section, gname, mname, prop, _, _, _, reason in unfixable
    ) or "| — | | | | |"
    hush = "\n".join(
        f"| `.{gname}` | `{key}` | `{prop}` | `{value}` |"
        for gname, key, prop, value in quiet
    ) or "| — | | | |"
    gone = "\n".join(
        f"| {section} | `.{gname}` | `{key}` |"
        for section, gname, mname, key in missing
    ) or "| — | | |"

    total = sum(len(v) for k, v in pairs.items() if k != "_")
    OUT.write_text(f"""# Расхождения компонентов с макетом

ФАЙЛ МАШИННЫЙ. Правки будут затёрты.
Генератор: `visual/fodinae-ui-lab/tools/compare-components.py`.
Карта пар: `visual/fodinae-ui-lab/component-map.json`.

## Что это

Токены игра и макет делят с точностью до значения, но словарь — не текст:
одними и теми же значениями собираются разные экраны. Этот отчёт сверяет уже
сказанное: {total} пар компонентов, {checked} сравнимых свойств,
**{len(debt)} расхождений** плюс {len(unfixable)}, которые устранить нельзя.

Сравнивается и покой, и реакция: селекторы игры переписываются именами макета
(`.mm-side-btn:hover` → `.side-icon-btn:hover`), и семейства двух сторон
ложатся ключ в ключ. Сравниваются только свойства, названные у обоих: чего
игра не сказала, то она отдала теме — это не расхождение, а умолчание.

| секция | игра | макет | свойство | в игре | в макете |
|---|---|---|---|---|---|
{rows}

## Реакции, которых в игре нет

Состояние, названное только макетом, — не спор о значении: спорить не о чем,
правила нет вовсе, и интерфейс на это действие молчит. Такое чинится не
правкой числа, а заведением правила.

| секция | игра | селектор макета |
|---|---|---|
{gone}

## Макет сказал — игра промолчала

Долгое время такое пропускалось: «чего игра не сказала, то она отдала теме».
Для цвета и рамки это неверно — умолчание темы не нарисует ни фона, ни
границы, и четыре коробки (карточка деталей сервера, шапка профиля и две
плашки хроники) жили в игре невидимыми, пока сверка молчала вместе с ними.

Законное молчание отсеивается по причине, а не по догадке: свойство названо
соседним классом на том же узле разметки; объявлено ниже по дереву, потому что
макет наследует шрифт с контейнера, а игра пишет его на листе; совпадает с
умолчанием USS, отличным от CSS; либо в USS невыразимо. Всё, что осталось, —
здесь, и сборка на этом падает.

| игра | макет | свойство | в макете |
|---|---|---|---|
{hush}

## Чего устранить нельзя

USS — не CSS, и часть расхождений структурная: градиента, дробного веса
шрифта и относительных единиц там нет вовсе, а геометрия планеты и маркеров
в игре считается кодом по 3D-сцене, а не задаётся вёрсткой. Такие строки не
долг, и приводить их «поближе» вручную значит менять вид ради числа.

Сюда же не попадают вовсе селекторы по атрибуту и id (в USS их нет) и потомок
по тегу: `.side-icon-btn svg` в макете — это класс глифа в игре, потому что
элемента `svg` в UI Toolkit не существует.

| секция | игра | макет | свойство | почему |
|---|---|---|---|---|
{stuck}
""", encoding="utf-8")

    print(f"{OUT.relative_to(REPO)}: {len(debt)} расхождений, "
          f"{len(missing)} отсутствующих реакций, {len(quiet)} молчаний "
          f"(+{len(unfixable)} неустранимых) на {checked} сравнимых свойств")
    if "--check" in sys.argv and (debt or missing or quiet):
        for gname, key, prop, value in quiet:
            print(f"  .{gname}: {prop} не сказано, в макете {value}")
        for section, gname, mname, prop, was, want, key, _ in debt:
            print(f"  {key}: {prop} = {was}, в макете {want}")
        for section, gname, mname, key in missing:
            print(f"  {key}: в игре нет правила")
        raise SystemExit(1)


if __name__ == "__main__":
    main()
