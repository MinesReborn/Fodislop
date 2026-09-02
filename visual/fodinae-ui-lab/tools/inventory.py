#!/usr/bin/env python3
"""Инвентаризация дизайн-системы FODINAE.

Печатает только измерения. Ни оценок, ни кода возврата: приговор выносит
lint-design-system.py, а этот файл даёт числа, из которых выводятся шкалы.

    python3 tools/inventory.py
"""

from __future__ import annotations

import collections
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
TOKENS = ROOT / "css" / "tokens.css"

CSS_FILES = sorted(ROOT.glob("css/**/*.css")) + [ROOT / "styles.css"]
MARKUP = [ROOT / "index.html", ROOT / "styleguide.html"]
SCRIPTS = [ROOT / "app.js", ROOT / "js" / "styleguide.js"]
ALL_FILES = CSS_FILES + MARKUP + SCRIPTS

# Слои токенов. Порядок важен: имя проверяется по первому подошедшему правилу.
LAYERS = [
    ("примитив", lambda n: n.startswith(("--rgb-", "--hex-"))),
    ("материал", lambda n: n.startswith("--mat-")),
    ("семантика", lambda n: n.startswith((
        "--surface-", "--border-", "--text-", "--accent-", "--state-",
        "--rarity-", "--focus-",
    ))),
    ("шкала", lambda n: n.startswith((
        "--space-", "--size-", "--radius-", "--dur-", "--ease-", "--blur-",
        "--leading-", "--tracking-", "--weight-", "--layer-", "--alpha-",
    ))),
    ("гарнитура", lambda n: n.startswith(("--face-", "--font-"))),
]


def layer_of(name: str) -> str:
    for label, test in LAYERS:
        if test(name):
            return label

    return "прочее"


def read(path: pathlib.Path) -> str:
    return path.read_text(encoding="utf-8") if path.exists() else ""


def rel(path: pathlib.Path) -> str:
    return str(path.relative_to(ROOT))


def head(title: str) -> None:
    print(f"\n{'=' * 70}\n{title}\n{'=' * 70}")


def table(rows: list[tuple[str, object]], indent: str = "  ") -> None:
    if not rows:
        print(f"{indent}—")
        return

    width = max(len(str(k)) for k, _ in rows)
    for key, value in rows:
        print(f"{indent}{str(key):<{width}}  {value}")


def histogram(counter: collections.Counter, indent: str = "  ") -> None:
    """Печатает распределение, отсортированное по значению, с полосой частоты."""
    if not counter:
        print(f"{indent}—")
        return

    peak = max(counter.values())
    width = max(len(str(k)) for k in counter)
    for key in sorted(counter, key=lambda k: (len(str(k)), str(k)) if not str(k).lstrip("-").isdigit() else (0, f"{int(k):09d}")):
        n = counter[key]
        bar = "█" * max(1, round(n / peak * 32))
        print(f"{indent}{str(key):>{width}}  {n:>3}  {bar}")


# --------------------------------------------------------------------------
# 1. Токены: объявления и использования по слоям
# --------------------------------------------------------------------------

def declared_tokens() -> dict[str, str]:
    """Имя токена -> файл, в котором он объявлен."""
    found: dict[str, str] = {}
    for f in CSS_FILES:
        for name in re.findall(r"(--[a-z0-9-]+)\s*:", read(f), re.I):
            found.setdefault(name, rel(f))

    return found


def token_uses() -> dict[str, collections.Counter]:
    """Имя токена -> Counter{файл: сколько раз}."""
    uses: dict[str, collections.Counter] = collections.defaultdict(collections.Counter)
    for f in ALL_FILES:
        for name in re.findall(r"var\(\s*(--[a-z0-9-]+)", read(f), re.I):
            uses[name][rel(f)] += 1

    return uses


def report_tokens() -> None:
    declared = declared_tokens()
    uses = token_uses()

    head("1. ТОКЕНЫ ПО СЛОЯМ")
    per_layer: collections.Counter = collections.Counter()
    used_per_layer: collections.Counter = collections.Counter()
    for name in declared:
        per_layer[layer_of(name)] += 1

    for name, counter in uses.items():
        used_per_layer[layer_of(name)] += sum(counter.values())

    print(f"  всего объявлено: {len(declared)}")
    table(
        [
            (label, f"объявлено {per_layer.get(label, 0):>3}   использований {used_per_layer.get(label, 0):>4}")
            for label, _ in LAYERS + [("прочее", None)]
        ]
    )

    head("2. ПРОТЕЧКА СЛОЯ ПРИМИТИВОВ")
    print("  tokens.css §1: «Компоненты их НЕ используют».")
    print("  Ниже — обращения к примитивам и материалам ВНЕ tokens.css.\n")
    leaks: collections.Counter = collections.Counter()
    for name, counter in uses.items():
        if layer_of(name) not in ("примитив", "материал"):
            continue

        for filename, n in counter.items():
            if filename != rel(TOKENS):
                leaks[filename] += n

    table(sorted(leaks.items(), key=lambda kv: -kv[1]))
    print(f"\n  итого протечек: {sum(leaks.values())}")


# --------------------------------------------------------------------------
# 3. Распределение альф — основание для шкалы --alpha-*
# --------------------------------------------------------------------------

ALPHA_USE = re.compile(r"rgb\(\s*var\(\s*(--[a-z0-9-]+)\s*\)\s*/\s*(\d+)%", re.I)


def report_alpha() -> None:
    head("3. ПРОЗРАЧНОСТЬ: rgb(var(--x) / N%) ВНЕ tokens.css")
    print("  В USS такой записи нет: каждое место станет рукописным литералом")
    print("  в ThemeTokens.uss. Это прямая мера будущего расхождения.\n")

    per_alpha: collections.Counter = collections.Counter()
    per_primitive: collections.Counter = collections.Counter()
    combos: collections.Counter = collections.Counter()
    for f in ALL_FILES:
        if f == TOKENS:
            continue

        for name, alpha in ALPHA_USE.findall(read(f)):
            per_alpha[int(alpha)] += 1
            per_primitive[name] += 1
            combos[f"{name} / {alpha}%"] += 1

    print(f"  использований: {sum(per_alpha.values())}   различных сочетаний: {len(combos)}")

    print("\n  Распределение по значению альфы (основание шкалы):")
    histogram(per_alpha, indent="    ")

    print("\n  По примитивам — сколько разных альф у каждого:")
    spread = collections.defaultdict(set)
    for f in ALL_FILES:
        if f == TOKENS:
            continue

        for name, alpha in ALPHA_USE.findall(read(f)):
            spread[name].add(int(alpha))

    table(
        sorted(
            ((n, f"{len(a):>2} различных: {', '.join(str(x) for x in sorted(a))}") for n, a in spread.items()),
            key=lambda kv: -len(spread[kv[0]]),
        ),
        indent="    ",
    )

    print("\n  Сочетания, для которых семантический токен УЖЕ существует:")
    token_text = read(TOKENS)
    semantic: dict[str, str] = {}
    for line in token_text.splitlines():
        m = re.match(r"\s*(--[a-z0-9-]+)\s*:\s*(rgb\(\s*var\([^)]+\)\s*/\s*\d+%\s*\))", line, re.I)
        if m and layer_of(m.group(1)) == "семантика":
            semantic[re.sub(r"\s+", "", m.group(2))] = m.group(1)

    hits = []
    for combo, n in combos.items():
        name, alpha = combo.split(" / ")
        key = re.sub(r"\s+", "", f"rgb(var({name})/{alpha})")
        if key in semantic:
            hits.append((combo, f"{n:>3} × → {semantic[key]}"))

    table(sorted(hits, key=lambda kv: kv[0]), indent="    ")


# --------------------------------------------------------------------------
# 4. Литералы против шкал
# --------------------------------------------------------------------------

def scale_steps(prefix: str) -> dict[str, str]:
    """Ступени шкалы из tokens.css: значение -> имя токена."""
    steps: dict[str, str] = {}
    for name, value in re.findall(rf"({re.escape(prefix)}[a-z0-9-]+)\s*:\s*([^;]+);", read(TOKENS), re.I):
        steps.setdefault(value.strip(), name)

    return steps


def literals(pattern: str) -> collections.Counter:
    found: collections.Counter = collections.Counter()
    for f in CSS_FILES + MARKUP:
        if f == TOKENS:
            continue

        for value in re.findall(pattern, read(f), re.I):
            found[value] += 1

    return found


def report_literals() -> None:
    head("4. ЛИТЕРАЛЫ ПРОТИВ ШКАЛ")
    print("  Литерал, совпавший со ступенью, — «токен есть, но не использован».")
    print("  Литерал вне шкалы — значение вне системы.\n")

    for label, prefix, pattern in [
        ("кегли (font-size)", "--size-", r"font-size:\s*(\d+px)"),
        ("длительности переходов", "--dur-", r"transition[^;:]*:[^;]*?(\d*\.?\d+s)"),
        ("радиусы", "--radius-", r"border-radius:\s*([\d.]+(?:px|%))"),
        ("размытие", "--blur-", r"blur\((\d+px)\)"),
    ]:
        steps = scale_steps(prefix)
        found = literals(pattern)
        on = {v: n for v, n in found.items() if v in steps}
        off = {v: n for v, n in found.items() if v not in steps}
        print(f"  {label}: всего {sum(found.values())}, "
              f"на шкале {sum(on.values())}, вне шкалы {sum(off.values())}")
        if on:
            print("    на шкале (замена механическая):")
            table(sorted(((v, f"{n:>3} × → {steps[v]}") for v, n in on.items()),
                         key=lambda kv: -found[kv[0]]), indent="      ")

        if off:
            print("    вне шкалы (нужно решение):")
            table(sorted(((v, f"{n:>3} ×") for v, n in off.items()),
                         key=lambda kv: -found[kv[0]]), indent="      ")

        print()

    ambient = literals(r"animation[^;:]*:[^;]*?(\d*\.?\d+s)")
    print("  Длительности фоновых циклов (animation). Своей шкалы у них нет:")
    print("  --dur-* покрывает 0.1–0.8s и предназначена для переходов, а не")
    print("  для бесконечных петель. Это отдельное измерение, не нарушение.")
    table(sorted(((v, f"{n:>3} ×") for v, n in ambient.items()), key=lambda kv: -ambient[kv[0]]),
          indent="    ")

    print()
    print("  z-index (в USS не существует; должен идти через --layer-*):")
    table(sorted(literals(r"z-index:\s*(\d+)").items(), key=lambda kv: -kv[1]), indent="    ")


# --------------------------------------------------------------------------
# 5. Покрытие состояний
# --------------------------------------------------------------------------

def report_states() -> None:
    head("5. ПОКРЫТИЕ СОСТОЯНИЙ")
    print("  Пять состояний интерактива: покой / наведение / нажатие /")
    print("  недоступно / выбрано.\n")

    rows = []
    for label, pattern in [
        (":hover", r":hover\b"),
        (":active", r":active\b"),
        (":disabled", r":disabled\b"),
        (":focus-visible", r":focus-visible\b"),
        (".active (выбор)", r"\.active\b"),
        (".selected (выбор)", r"\.selected\b"),
    ]:
        per_file = {rel(f): len(re.findall(pattern, read(f))) for f in CSS_FILES}
        total = sum(per_file.values())
        where = ", ".join(f"{k} {v}" for k, v in sorted(per_file.items(), key=lambda kv: -kv[1]) if v)
        rows.append((label, f"{total:>3}   {where or '—'}"))

    table(rows)


# --------------------------------------------------------------------------
# 6. Разметка: инлайн-стили и обработчики
# --------------------------------------------------------------------------

def report_markup() -> None:
    head("6. РАЗМЕТКА")
    text = read(ROOT / "index.html")

    inline = re.findall(r'\sstyle="([^"]*)"', text)
    props: collections.Counter = collections.Counter()
    for chunk in inline:
        for decl in chunk.split(";"):
            if ":" in decl:
                props[decl.split(":")[0].strip()] += 1

    print(f"  index.html: инлайн-стилей {len(inline)}, объявлений в них {sum(props.values())}")
    print("  Какие свойства задаются инлайном:")
    table(sorted(props.items(), key=lambda kv: -kv[1]), indent="    ")

    print(f"\n  styleguide.html: инлайн-стилей {len(re.findall(r'\\sstyle=\"', read(ROOT / 'styleguide.html')))}")

    handlers = re.findall(r'on[a-z]+="([a-zA-Z_$][\w$]*)', text)
    print(f"\n  Инлайн-обработчиков: {len(handlers)}, различных функций: {len(set(handlers))}")
    table(sorted(collections.Counter(handlers).items(), key=lambda kv: -kv[1])[:12], indent="    ")

    literal_args = re.findall(r'on[a-z]+="[^"]*?\'([^\']{3,})\'', text)
    print(f"\n  Обработчиков, которым передают строковый литерал (контент в разметке): {len(literal_args)}")


# --------------------------------------------------------------------------
# 7. Экраны
# --------------------------------------------------------------------------

def report_screens() -> None:
    head("7. ЭКРАНЫ")
    text = read(ROOT / "index.html")
    states = re.findall(r'data-state="([a-z]+)"', text)
    print("  Значения [data-state] (машина состояний #appViewport):")
    table(sorted(collections.Counter(states).items()), indent="    ")
    print(f"\n  Модальных окон (.modal-overlay): {len(re.findall(r'class=.modal-overlay', text))}")
    print(f"  styles.css: {len(read(ROOT / 'styles.css').splitlines())} строк неразобранного слоя")
    screens = sorted((ROOT / "css" / "screens").glob("*.css"))
    print(f"  css/screens/: {len(screens)} файлов")
    print(f"  css/effects.css: {'есть' if (ROOT / 'css' / 'effects.css').exists() else 'НЕТ'}")


def main() -> None:
    print("ИНВЕНТАРИЗАЦИЯ ДИЗАЙН-СИСТЕМЫ FODINAE")
    print("Только измерения. Приговор выносит lint-design-system.py.")
    report_tokens()
    report_alpha()
    report_literals()
    report_states()
    report_markup()
    report_screens()
    print()


if __name__ == "__main__":
    main()
