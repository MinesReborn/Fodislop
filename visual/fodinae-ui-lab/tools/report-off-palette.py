#!/usr/bin/env python3
"""Цвета общего слоя USS, не совпадающие с палитрой — в отчёт.

При переводе общего слоя с литералов на токены подставляются ТОЛЬКО точные
совпадения. Округлять близкое нельзя: это перерисовка интерфейса под видом
наведения порядка, и в макете такое уже ломало вид. Значит несовпавшее
обязано быть названо, иначе оно просто останется незамеченным.

Печатает docs/design-debt-uss.md. Слой main game не считается: у него свой
счётчик в scripts/check-architecture.js и свой заход.
"""

import collections
import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
REPO = ROOT.parent.parent
PALETTE = REPO / "Assets" / "Resources" / "Styles" / "token-palette.json"
STYLES = REPO / "Assets" / "Resources" / "Styles"
OUT = REPO / "docs" / "design-debt-uss.md"

# Общий слой: всё, что не main game и не печатается генератором.
SHARED = ["Theme.uss", "SciFi.uss", "Animations.uss", "Panel.uss",
          "Button.uss", "Input.uss", "Auth.uss"]

COLOR = re.compile(r"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)")

# Ступени, которые тиры переопределяют. Подставить такую ступень вместо
# литерала — не переименование, а включение механизма: в compact и wide
# значение станет другим. Поэтому они не подставляются автоматически и
# выносятся в отчёт отдельным разделом.
TIERED = {
    "--size-md": 15, "--size-lg": 18, "--size-xl": 24,
    "--size-2xl": 32, "--size-3xl": 42,
    "--space-10": 24, "--space-11": 28, "--space-12": 32,
    "--space-13": 40, "--space-14": 48,
}
GEOMETRY = {"width", "height", "min-width", "min-height", "max-width",
            "max-height", "top", "left", "right", "bottom", "flex-basis"}

PX = re.compile(r"([a-z-]+)\s*:\s*([^;{}]*?)\s*;")


def norm(value):
    return int(value[0]), int(value[1]), int(value[2]), round(float(value[3]), 3)


def main() -> None:
    colors = json.loads(PALETTE.read_text(encoding="utf-8"))["colors"]
    known = {norm(v) for v in colors.values()}

    off = collections.Counter()
    where = collections.defaultdict(set)
    for name in SHARED:
        text = re.sub(r"/\*[\s\S]*?\*/", " ", (STYLES / name).read_text(encoding="utf-8"))
        for match in COLOR.finditer(text):
            key = norm((match.group(1), match.group(2), match.group(3), match.group(4) or "1"))
            if key in known:
                continue
            off[match.group(0)] += 1
            where[match.group(0)].add(name)

    def nearest(key):
        """Токен того же RGB с ближайшей альфой: почти все расхождения — по ней."""
        best = None
        for token, value in colors.items():
            other = norm(value)
            if other[:3] != key[:3]:
                continue
            distance = abs(other[3] - key[3])
            if best is None or distance < best[0]:
                best = (distance, token, other[3])
        return best

    rows = []
    for literal, count in off.most_common():
        match = COLOR.fullmatch(literal)
        key = norm((match.group(1), match.group(2), match.group(3), match.group(4) or "1"))
        near = nearest(key)
        hint = f"`{near[1]}` при альфе {near[2]}" if near else "нет токена с таким цветом"
        rows.append(f"| `{literal}` | {count} | {', '.join(sorted(where[literal]))} | {hint} |")

    # Второй раздел: литералы, совпавшие с тир-зависимой ступенью.
    tiered = collections.Counter()
    tiered_where = collections.defaultdict(set)
    for name in SHARED:
        text = re.sub(r"/\*[\s\S]*?\*/", " ", (STYLES / name).read_text(encoding="utf-8"))
        for prop, value in PX.findall(text):
            for px in re.findall(r"(?<![\w-])(\d+)px", value):
                for token, step in TIERED.items():
                    # Шкалы не взаимозаменяемы: кегль сопоставляется только с
                    # size-, отступ только с space-. Без этого отчёт предлагал
                    # бы font-size из шкалы отступов — совпадение по числу,
                    # бессмыслица по существу.
                    family = "size" if token.startswith("--size-") else "space"
                    if family == "size" and prop != "font-size":
                        continue
                    if family == "space" and not prop.startswith(("padding", "margin")):
                        continue
                    if int(px) == step:
                        tiered[(prop, f"{px}px", token)] += 1
                        tiered_where[(prop, f"{px}px", token)].add(name)

    tier_rows = [
        f"| `{prop}: {lit}` | {count} | {', '.join(sorted(tiered_where[(prop, lit, token)]))} | `{token}` |"
        for (prop, lit, token), count in tiered.most_common()
    ] or ["| — | | | |"]

    # Третий раздел: почему остальные px нельзя привести к макету.
    # Число из потолка «литерал в общем слое» складывается в основном из них,
    # и без разбора оно читается как незакрытый долг, хотя это не долг.
    buckets = {
        "рамка в один пиксель": lambda prop, px: prop.endswith("width") and prop.startswith("border") and float(px) <= 2,
        "трекинг": lambda prop, px: prop == "letter-spacing",
        "геометрия компонента": lambda prop, px: prop in GEOMETRY,
    }
    stayed = collections.Counter()
    stayed_where = collections.defaultdict(set)
    for name in SHARED:
        text = re.sub(r"/\*[\s\S]*?\*/", " ", (STYLES / name).read_text(encoding="utf-8"))
        for prop, value in PX.findall(text):
            for px in re.findall(r"(?<![\w-])(\d+(?:\.\d+)?)px", value):
                label = next((k for k, test in buckets.items() if test(prop, px)), "прочее")
                stayed[label] += 1
                stayed_where[label].add(name)
    stayed_rows = [
        f"| {label} | {count} | {', '.join(sorted(stayed_where[label]))} |"
        for label, count in stayed.most_common()
    ] or ["| — | | |"]

    OUT.write_text(f"""# Цвета общего слоя вне палитры

ФАЙЛ МАШИННЫЙ. Правки будут затёрты.
Генератор: `visual/fodinae-ui-lab/tools/report-off-palette.py`.
Потолки этих чисел держит `DEBT_BUDGET` в `scripts/check-architecture.js`.

## Что это

При переводе общего слоя USS с литералов на токены подставлялись **только
точные совпадения**: значение заменялось именем, если совпадало с токеном до
последнего разряда. Подставилось 10 цветов и 103 отступа, и построчная сверка
показала, что ни одно значение при этом не изменилось.

Ниже — то, что не подставилось: **{sum(off.values())} записей,
{len(off)} различных значений**. Они близки к палитре, но не равны ей.
Округлить их молча означало бы перерисовать интерфейс под видом наведения
порядка, поэтому они оставлены как есть и записаны сюда.

Больше всего расхождений — по альфе: цвет тот же, прозрачность своя. Это и
есть главный вопрос к предстоящей переделке макета — либо шкала
прозрачностей расширяется и значения приходят к ней, либо часть из них
признаётся осмысленно уникальной.

Слой main game сюда не входит: у него свой счётчик и свой заход.

| значение | раз | файлы | ближайший токен того же цвета |
|---|---|---|---|
{chr(10).join(rows)}

## Значения, которые могли бы следовать тиру

Отдельный вопрос, и решать его человеку. Пять ступеней кегля и пять ступеней
шкалы отступов тиры переопределяют: `--size-md` это 15px в стандартном тире,
14px в compact и 16px в wide. Литерал, совпавший с такой ступенью, подставить
можно — но это не переименование, а **включение механизма**: в двух тирах из
трёх вид изменится.

Поэтому автоматически они не подставлены. Ниже — {sum(tiered.values())} мест,
где литерал точно совпал с тир-зависимой ступенью. По каждому нужно решить,
должен ли размер следовать тиру или он осмысленно постоянный.

| место | раз | файлы | ступень |
|---|---|---|---|
{chr(10).join(tier_rows)}

## Остальные px: почему они остаются литералами

Потолок `литерал в общем слое` считает и цвета, и пиксели, поэтому его число
заметно больше таблиц выше. Разница — {sum(stayed.values())} значений, которые
токеном не выражаются в принципе, а не «ещё не дошли руки»:

| разряд | раз | файлы |
|---|---|---|
{chr(10).join(stayed_rows)}

- **рамка в один пиксель** — в макете это тоже литерал (`border: 1px solid`),
  токена ширины рамки там нет. Приводить не к чему.
- **трекинг** — в макете `--tracking-base/wide/widest` заданы в `em`, то есть
  зависят от кегля. USS единицы `em` не поддерживает, поэтому одним числом эти
  токены не переносятся; каждое место держит свой пиксельный эквивалент.
- **геометрия компонента** — ширины, высоты и координаты конкретных элементов.
  Шкалы для них нет ни здесь, ни в макете.

Остаток строки «прочее» — единственное, что стоит пересматривать дальше.
""", encoding="utf-8")
    print(f"{OUT.relative_to(REPO)}: {sum(off.values())} записей, {len(off)} различных")


if __name__ == "__main__":
    main()
