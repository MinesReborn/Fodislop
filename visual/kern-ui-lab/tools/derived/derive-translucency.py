#!/usr/bin/env python3
"""Вывод семантических токенов полупрозрачности и карты замены.

ЗАЧЕМ. tokens.css §1 запрещает компонентам обращаться к примитивам, но они
делают это сотнями мест, каждый раз дописывая альфу руками. Это вернуло ту же
болезнь, ради которой строилась рампа высот: вместо четырёх названных ступеней
глубины — россыпь безымянных значений, теперь по оси прозрачности.

ПОЧЕМУ НЕ ШКАЛА --alpha-*. Соблазн — объявить шкалу альф и писать
`rgb(var(--rgb-void) / var(--alpha-90))`. Это ничего не решает: в UI Toolkit
такой записи нет ровно так же, как нет и исходной. Переносимым является только
НАЗВАННОЕ СОЧЕТАНИЕ — оно становится в USS обычным rgba(). Поэтому шкала здесь
не результат, а инструмент: она говорит, какие сочетания называть.

КАК. Альфы одного семейства квантуются по логит-оси (см. derive-alpha-scale.py
о том, почему логит). Ступени именуются по роли, которую эта прозрачность
играет, а роли уже заданы семантическим слоем: подложка, линия, свечение,
вуаль, блик.

    python3 tools/derive-translucency.py           — отчёт и карта
    python3 tools/derive-translucency.py --tokens  — блок для tokens.css
"""

from __future__ import annotations

import collections
import math
import pathlib
import re
import sys

# .parent.parent.parent: файл лежит в tools/derived, корень макета — на два выше.
ROOT = pathlib.Path(__file__).resolve().parent.parent.parent
TOKENS = ROOT / "css" / "tokens.css"
TARGETS = [
    ROOT / "styles.css",
    ROOT / "index.html",
    ROOT / "css" / "components.css",
    ROOT / "css" / "base.css",
    ROOT / "css" / "shell.css",
]

COMBO = re.compile(r"rgb\(\s*var\(\s*(--rgb-[a-z0-9-]+)\s*\)\s*/\s*(\d+)%\s*\)", re.I)
OPAQUE = re.compile(r"rgb\(\s*var\(\s*(--rgb-[a-z0-9-]+)\s*\)\s*\)", re.I)

# Семейства. Роль определяет, ЧТО означает прозрачность, и потому определяет
# имена ступеней. Одна и та же альфа в разных семействах — разное явление:
# 12% циана поверх панели это подсветка, 12% пустоты — почти ничто.
#
# Здесь задаётся ТОЛЬКО состав семейства. Сами ступени не назначаются руками,
# а выводятся из фактического распределения этого семейства (см. steps_for).
FAMILIES: dict[str, dict[str, object]] = {
    "accent": {
        "primitives": ["--rgb-gold", "--rgb-cyan"],
        "role": "акцент: подложка, линия, свечение, заливка",
    },
    "state": {
        "primitives": ["--rgb-ok", "--rgb-warn", "--rgb-danger", "--rgb-anomaly",
                       "--rgb-magma"],
        "role": "состояние: подложка, линия, свечение",
    },
    "surface": {
        "primitives": ["--rgb-void", "--rgb-abyss", "--rgb-slate", "--rgb-shelf",
                       "--rgb-crisis", "--rgb-ember"],
        "role": "поверхность: вуаль поверх сцены",
    },
    "steel": {
        "primitives": ["--rgb-steel"],
        "role": "сталь: границы",
    },
    "light": {
        "primitives": ["--rgb-light"],
        "role": "блик на гранях",
    },
    "shadow": {
        "primitives": ["--rgb-shadow"],
        "role": "тень: только под элементами",
    },
}

# Полосы значения. Имя ступени говорит, ЧЕМ прозрачность является, а не
# сколько её: это то, ради чего вообще заводится семантический слой.
#
# Границы полос — точки, где прозрачность меняет роль, а не круглые числа:
#   10%  ниже этого слой едва различим и работает как намёк, а не как краска;
#   25%  здесь подложка начинает читаться как собственная поверхность;
#   55%  здесь слой перестаёт быть подсветкой и становится заливкой;
#   85%  выше этого фон за слоем уже почти не читается;
#   95%  выше этого прозрачности нет вовсе. 96% и 98% — это не «почти
#        непрозрачно», а неудавшаяся попытка быть непрозрачным: разницу
#        никто не задумывал и никто её не увидит. Система говорит об этом
#        прямо — берётся сплошной цвет, без альфы. Заодно и в USS значение
#        выходит чище: обычный hex вместо rgba с альфой 0.98.
#
# Пять полос — не «пять уровней громкости», а пять разных явлений. Внутри
# одной полосы разница (85% против 90%) смысла не несёт: её никто не задумывал,
# она натекла руками. Между полосами разница смысл несёт всегда.
BANDS = [
    (0, 10, {"accent": "tint", "state": "tint", "surface": "mist",
             "steel": "hairline", "light": "film", "shadow": "cast"}),
    (10, 25, {"accent": "wash", "state": "wash", "surface": "haze",
              "steel": "line", "light": "sheen", "shadow": "cast"}),
    (25, 55, {"accent": "glow", "state": "glow", "surface": "shade",
              "steel": "line-strong", "light": "edge", "shadow": "cast"}),
    (55, 85, {"accent": "fill", "state": "fill", "surface": "veil",
              "steel": "fill", "light": "gleam", "shadow": "cast"}),
    (85, 95, {"accent": "dense", "state": "dense", "surface": "dense",
              "steel": "dense", "light": "dense", "shadow": "cast"}),
]

# Выше этого порога альфа отбрасывается: значение объявляется сплошным.
OPAQUE_AT = 95

FAMILY_OF = {p: name for name, spec in FAMILIES.items() for p in spec["primitives"]}


def logit(alpha: int) -> float:
    a = min(max(alpha, 1), 99) / 100
    return math.log(a / (1 - a))


def partition(counts: collections.Counter, k: int) -> list[list[int]]:
    """Оптимальное разбиение по логит-оси (динамическое программирование).
    Жадная нарезка режет непрерывный ряд произвольно и заведомо хуже."""
    values = sorted(counts)
    n = len(values)
    inf = float("inf")

    def cost(i: int, j: int) -> float:
        best = inf
        for rep in values[i:j]:
            best = min(best, sum(counts[v] * (logit(v) - logit(rep)) ** 2
                                 for v in values[i:j]))

        return best

    dp = [[inf] * (n + 1) for _ in range(k + 1)]
    cut = [[0] * (n + 1) for _ in range(k + 1)]
    dp[0][0] = 0.0
    for c in range(1, k + 1):
        for j in range(c, n + 1):
            for i in range(c - 1, j):
                if dp[c - 1][i] == inf:
                    continue

                total = dp[c - 1][i] + cost(i, j)
                if total < dp[c][j]:
                    dp[c][j] = total
                    cut[c][j] = i

    groups: list[list[int]] = []
    j = n
    for c in range(k, 0, -1):
        i = cut[c][j]
        groups.append(values[i:j])
        j = i

    return list(reversed(groups))


def steps_for(counts: collections.Counter) -> dict[int, str]:
    """Лестница семейства: по одной ступени на каждую использованную полосу.

    Здесь принято главное решение всей системы, и принято оно в пользу
    ЗНАЧЕНИЯ, а не сохранности. Можно было подобрать ступени так, чтобы ничего
    не сдвинулось — но тогда их вышло бы 86 на 92 сочетания, то есть система
    осталась бы россыпью чисел с новыми именами. Разброс в макете (33 альфы)
    и есть болезнь, а не данность, которую надо аккуратно перенести.

    Поэтому лестница задаётся полосами (BANDS), а не точностью приближения:
    у прозрачности есть три роли — подложка, свечение, заливка — и внутри роли
    разница между 85% и 90% не несёт смысла, её никто не задумывал.

    Значение ступени — самое частое в полосе: оно уже написано в макете
    десятки раз, и замена там, где она механическая, остаётся механической.
    """
    chosen: list[int] = []
    for lo, hi, _ in BANDS:
        inside = {v: n for v, n in counts.items() if lo <= v < hi}
        if inside:
            chosen.append(max(inside, key=lambda v: (inside[v], -v)))

    return {value: "" for value in sorted(chosen)}


def name_steps(steps: dict[int, str], family: str) -> dict[int, str]:
    """Имя ступени — её роль. Суффиксов нет и быть не может: в полосе ровно
    одна ступень, а полос три. Если имя понадобилось уточнять номером —
    значит роль выделена неверно, и чинить надо роль, а не имя."""
    return {
        value: next(names[family] for lo, hi, names in BANDS if lo <= value < hi)
        for value in sorted(steps)
    }


def band_of(alpha: int) -> int:
    """Индекс полосы, которой принадлежит значение."""
    for i, (lo, hi, _) in enumerate(BANDS):
        if lo <= alpha < hi:
            return i

    return len(BANDS) - 1


def snap(alpha: int, steps: dict) -> int:
    """Ступень своей полосы.

    Именно своей, а не ближайшей по логиту: полоса — смысловая единица, и
    перенос значения через её границу меняет роль. 80% ближе к 90% по числу,
    но 80% — вуаль, сквозь которую сцена видна, а 90% — плотная поверхность.
    Число здесь не главнее смысла.
    """
    target = band_of(alpha)
    same_band = [s for s in steps if band_of(s) == target]
    if same_band:
        return min(same_band, key=lambda s: abs(logit(s) - logit(alpha)))

    return min(steps, key=lambda s: abs(logit(s) - logit(alpha)))


# Префикс семантического слоя. Имя токена читается слева направо как
# «слой — цвет — плотность»: --accent-cyan-wash, --surface-void-veil.
# Слой отвечает на вопрос «чем это является в интерфейсе», цвет — «какой
# краской», плотность — «насколько». Ни один из трёх вопросов не лишний.
LAYER_PREFIX = {
    "accent": "accent",
    "state": "state",
    "surface": "surface",
    "steel": "border",
    "light": "light",
    "shadow": "shadow",
}


def token_name(primitive: str, step_label: str) -> str:
    hue = primitive.removeprefix("--rgb-")
    family = FAMILY_OF.get(primitive)
    if family is None:
        return f"--{hue}-{step_label}"

    prefix = LAYER_PREFIX[family]
    # У стали и блика цвет один на всё семейство, называть его в имени незачем.
    if family in ("steel", "light", "shadow"):
        return f"--{prefix}-{step_label}"

    return f"--{prefix}-{hue}-{step_label}"


def measure() -> tuple[collections.Counter, collections.Counter]:
    combos: collections.Counter = collections.Counter()
    opaques: collections.Counter = collections.Counter()
    for f in TARGETS:
        text = f.read_text(encoding="utf-8")
        for prim, alpha in COMBO.findall(text):
            combos[(prim, int(alpha))] += 1

        for prim in OPAQUE.findall(text):
            opaques[prim] += 1

    return combos, opaques


def family_steps() -> dict[str, dict[int, str]]:
    """Лестница каждого семейства, выведенная из его собственных данных."""
    combos, _ = measure()
    per_family: dict[str, collections.Counter] = collections.defaultdict(collections.Counter)
    for (primitive, alpha), n in combos.items():
        family = FAMILY_OF.get(primitive)
        if family is not None and alpha < OPAQUE_AT:
            per_family[family][alpha] += n

    return {family: name_steps(steps_for(counts), family)
            for family, counts in per_family.items()}


def build() -> tuple[dict[str, str], dict[str, str], list[str]]:
    """Возвращает (карта замены, объявления токенов, предупреждения)."""
    combos, opaques = measure()
    ladders = family_steps()

    mapping: dict[str, str] = {}
    declarations: dict[str, str] = {}
    warnings: list[str] = []

    for primitive, alpha in combos:
        family = FAMILY_OF.get(primitive)
        if family is None:
            warnings.append(f"{primitive} не отнесён ни к одному семейству")
            continue

        if alpha >= OPAQUE_AT:
            name = token_name(primitive, "solid")
            mapping[f"rgb(var({primitive}) / {alpha}%)"] = f"var({name})"
            declarations[name] = f"rgb(var({primitive}))"
            continue

        steps = ladders[family]
        step = snap(alpha, steps)
        name = token_name(primitive, steps[step])
        mapping[f"rgb(var({primitive}) / {alpha}%)"] = f"var({name})"
        declarations[name] = f"rgb(var({primitive}) / {step}%)"

    # Непрозрачное обращение к примитиву — тоже протечка: у поверхности должно
    # быть имя роли, а не имя краски.
    for primitive in opaques:
        name = token_name(primitive, "solid")
        mapping[f"rgb(var({primitive}))"] = f"var({name})"
        declarations[name] = f"rgb(var({primitive}))"

    return mapping, declarations, warnings


def apply(mapping: dict[str, str]) -> int:
    """Переписывает обращения к примитивам на семантические токены.

    Замена текстовая и потому проверяемая: после неё в целевых файлах не должно
    остаться ни одного `rgb(var(--rgb-…))`. Это же и есть критерий успеха.
    """
    # Длинные образцы первыми: `/ 8%)` не должен съесть часть `/ 80%)`.
    ordered = sorted(mapping.items(), key=lambda kv: -len(kv[0]))
    normalize = re.compile(r"rgb\(\s*var\(\s*(--rgb-[a-z0-9-]+)\s*\)\s*(?:/\s*(\d+)%\s*)?\)", re.I)

    total = 0
    for f in TARGETS:
        text = f.read_text(encoding="utf-8")

        def swap(m: re.Match) -> str:
            key = (f"rgb(var({m.group(1)}) / {m.group(2)}%)" if m.group(2)
                   else f"rgb(var({m.group(1)}))")
            return mapping.get(key, m.group(0))

        new, n = normalize.subn(swap, text)
        if new != text:
            f.write_text(new, encoding="utf-8")

        left = len(normalize.findall(new))
        total += left
        print(f"  {f.name:<20} заменено {n:>3}, осталось примитивов {left}")

    print(f"\nОсталось обращений к примитивам: {total}")
    return 0 if total == 0 else 1


def main() -> int:
    mapping, declarations, warnings = build()
    combos, opaques = measure()
    total = sum(combos.values()) + sum(opaques.values())

    if "--apply" in sys.argv:
        return apply(mapping)

    if "--tokens" in sys.argv:
        by_family: dict[str, list[str]] = collections.defaultdict(list)
        for name, value in declarations.items():
            primitive = re.search(r"var\((--rgb-[a-z0-9-]+)\)", value).group(1)
            by_family[FAMILY_OF.get(primitive, "surface")].append(name)

        for family, spec in FAMILIES.items():
            names = sorted(by_family.get(family, []))
            if not names:
                continue

            print(f"\n  /* {spec['role']} */")
            width = max(len(n) for n in names)
            for name in names:
                print(f"  {name + ':':<{width + 1}} {declarations[name]};")

        return 0

    print(f"Обращений к примитивам вне tokens.css: {total}")
    print(f"  с альфой: {sum(combos.values())} в {len(combos)} сочетаниях")
    print(f"  непрозрачных: {sum(opaques.values())} в {len(opaques)} сочетаниях")
    print(f"\nПосле квантования по ролям — семантических токенов: {len(declarations)}")
    print(f"Сжатие: {len(combos) + len(opaques)} сочетаний → {len(declarations)} имён.\n")

    ladders = family_steps()
    for family, spec in FAMILIES.items():
        steps = ladders.get(family, {})
        print(f"  {family:<8} {spec['role']}")
        print(f"           ступени: {', '.join(f'{k}% {v}' for k, v in sorted(steps.items()))}")

    print("\nМаксимальный сдвиг при квантовании:")
    worst: list[tuple[float, str]] = []
    for (primitive, alpha), _ in combos.items():
        family = FAMILY_OF.get(primitive)
        if family is None:
            continue

        if alpha >= OPAQUE_AT:
            continue

        step = snap(alpha, ladders[family])
        worst.append((abs(logit(alpha) - logit(step)), f"{primitive} {alpha}% → {step}%"))

    for shift, label in sorted(worst, reverse=True)[:8]:
        print(f"  {shift:.2f} лог  {label}")

    for w in warnings:
        print(f"\n  ВНИМАНИЕ: {w}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
