#!/usr/bin/env python3
"""Вывод шкалы прозрачности из фактического распределения.

Тем же способом, каким в проекте выведены шкала отступов («6px ×14, 10px ×12,
8px ×9…») и шкала кеглей: берётся то, что макет уже использует, и сводится к
минимальному числу ступеней, каждая из которых представляет свой кластер.

Метод: жадная кластеризация с ограничением на максимальное отклонение.
Представитель кластера — самое частое значение в нём, а не середина: частое
значение уже написано в макете десятки раз, и выбор его ступенью делает
замену механической там, где она и так механическая.

Расстояние меряется в ЛОГИТАХ, а не в процентных пунктах. Причина: абсолютная
разница — неверная мера заметности. Сдвиг 2 п.п. при альфе 8% меняет плотность
на четверть, а при 90% — на два процента. Но и просто относительной разницы
мало: 90% и 96% пропускают 10% и 4% света, различаясь в 2,5 раза, то есть у
высоких альф важна пропускаемая доля. Логит log(a/(1-a)) выравнивает обе
крайности и считает 6%→8% и 92%→94% одинаково заметными.

    python3 tools/derive-alpha-scale.py [допуск]

Допуск по умолчанию 0.25 логита (≈ 28% относительного изменения плотности).
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
FILES = sorted(ROOT.glob("css/**/*.css")) + [ROOT / "styles.css", ROOT / "index.html"]

ALPHA = re.compile(r"rgb\(\s*var\(\s*(--[a-z0-9-]+)\s*\)\s*/\s*(\d+)%", re.I)


def fixed_points() -> dict[int, list[str]]:
    """Альфы, уже закреплённые семантическими токенами: значение -> токены."""
    found: dict[int, list[str]] = collections.defaultdict(list)
    for name, _, pct in re.findall(
        r"(--[a-z0-9-]+)\s*:\s*rgb\(\s*var\(\s*(--[a-z0-9-]+)\s*\)\s*/\s*(\d+)%",
        TOKENS.read_text(encoding="utf-8"), re.I,
    ):
        found[int(pct)].append(name)

    return found


def measure() -> collections.Counter:
    found: collections.Counter = collections.Counter()
    for f in FILES:
        if f == TOKENS:
            continue

        for _, alpha in ALPHA.findall(f.read_text(encoding="utf-8")):
            found[int(alpha)] += 1

    return found


def logit(alpha: int) -> float:
    """Мера заметности плотности. Альфы 0 и 100 не встречаются: это уже не
    полупрозрачность, а её отсутствие."""
    a = min(max(alpha, 1), 99) / 100
    return math.log(a / (1 - a))


def partition(counts: collections.Counter, k: int) -> tuple[float, list[list[int]]]:
    """Оптимальное разбиение на k кластеров по логит-оси.

    Жадная нарезка режет непрерывный ряд произвольно и даёт заведомо худший
    результат, поэтому здесь динамическое программирование: минимизируется
    сумма взвешенных квадратов отклонения от представителя кластера. Вес —
    частота значения в макете, чтобы ступень тянулась к тому, что реально
    написано, а не к середине диапазона.
    """
    values = sorted(counts)
    n = len(values)

    def cost(i: int, j: int) -> tuple[float, int]:
        """Стоимость кластера values[i:j] и его лучший представитель."""
        best = (float("inf"), values[i])
        for rep in values[i:j]:
            total = sum(counts[v] * (logit(v) - logit(rep)) ** 2 for v in values[i:j])
            if total < best[0]:
                best = (total, rep)

        return best

    # dp[c][j] — минимальная стоимость покрытия первых j значений c кластерами.
    inf = float("inf")
    dp = [[inf] * (n + 1) for _ in range(k + 1)]
    cut = [[0] * (n + 1) for _ in range(k + 1)]
    dp[0][0] = 0.0
    for c in range(1, k + 1):
        for j in range(c, n + 1):
            for i in range(c - 1, j):
                if dp[c - 1][i] == inf:
                    continue

                total = dp[c - 1][i] + cost(i, j)[0]
                if total < dp[c][j]:
                    dp[c][j] = total
                    cut[c][j] = i

    groups: list[list[int]] = []
    j = n
    for c in range(k, 0, -1):
        i = cut[c][j]
        groups.append(values[i:j])
        j = i

    return dp[k][n], list(reversed(groups))


def main() -> int:
    counts = measure()
    total = sum(counts.values())
    values = sorted(counts)

    print(f"Измерено: {total} использований, {len(values)} различных значений альфы.")
    print("Расстояние — логит; вес — частота в макете.\n")

    # Число ступеней не назначается, а выбирается по излому кривой ошибки:
    # там, где очередная ступень перестаёт заметно улучшать приближение.
    print("Кривая ошибки (сколько теряем, ограничившись k ступенями):\n")
    print(f"  {'k':>2}  {'ошибка':>8}  {'выигрыш':>8}  макс. сдвиг")
    curve: dict[int, tuple[float, list[list[int]]]] = {}
    gains: dict[int, float] = {}
    previous = None
    for k in range(3, min(15, len(values)) + 1):
        err, groups = partition(counts, k)
        curve[k] = (err, groups)
        reps = [max(g, key=lambda v: (counts[v], -v)) for g in groups]
        worst = max(abs(logit(v) - logit(r)) for g, r in zip(groups, reps) for v in g)
        if previous is not None:
            gains[k] = previous - err

        gain = "—" if previous is None else f"{gains[k]:8.3f}"
        print(f"  {k:>2}  {err:8.3f}  {gain:>8}  {worst:.2f} лог")
        previous = err

    # Излом — там, где выигрыш от следующей ступени обрушивается сильнее всего.
    # Берём максимум отношения соседних выигрышей: это точка, после которой
    # ступени перестают покупать точность и начинают покупать дробность.
    ratios = {k: gains[k] / gains[k + 1] for k in gains if k + 1 in gains}
    k = max(ratios, key=lambda key: ratios[key])
    err, groups = curve[k]
    reps = [max(g, key=lambda v: (counts[v], -v)) for g in groups]

    # Уже опубликованные семантические токены — неподвижные точки. Они
    # скопированы в Assets/Resources/Styles/ThemeTokens.uss, и сдвинуть их
    # значит изменить внешний вид игры ради стройности шкалы. Шкала обязана
    # обслуживать систему, а не наоборот, поэтому она достраивается вокруг них.
    fixed = fixed_points()
    for value in sorted(fixed):
        if value not in reps:
            reps.append(value)

    reps.sort()
    assign: dict[int, list[int]] = {r: [] for r in reps}
    for v in sorted(counts):
        assign[min(reps, key=lambda r: abs(logit(r) - logit(v)))].append(v)

    # Закреплённая ступень может не получить ни одного использования: токен
    # объявлен, но компоненты пишут соседнее значение руками. Это не ошибка
    # вывода, а находка — такую ступень показываем отдельно.
    orphans = [r for r in reps if not assign[r]]
    reps = [r for r in reps if assign[r]]
    groups = [assign[r] for r in reps]

    print(f"\nИзлом на k={k}: выигрыш падает в {ratios[k]:.1f} раза "
          f"({gains[k]:.2f} → {gains[k + 1]:.2f}).")
    print(f"Сжатие: {len(values)} значений → {len(reps)} ступеней "
          f"(из них {len(fixed)} закреплены существующими токенами).\n")

    print(f"  {'ступень':>8}  {'вес':>4}  {'покрывает':<32}  макс. сдвиг")
    for rep, members in zip(reps, groups):
        weight = sum(counts[v] for v in members)
        shift = max(abs(logit(v) - logit(rep)) for v in members)
        covered = ", ".join(f"{v}×{counts[v]}" for v in members)
        print(f"  {rep:>7}%  {weight:>4}  {covered:<32}  {shift:.2f} лог")

    if orphans:
        print("\n  Ступени без использований — токен есть, но компоненты пишут")
        print("  соседнее значение руками:")
        for r in orphans:
            print(f"    {r:>3}%  {', '.join(fixed.get(r, []))}")

    print("\nОбъявление для tokens.css:\n")
    for rep in reps:
        holders = fixed.get(rep, [])
        note = f"   /* закреплено: {', '.join(holders)} */" if holders else ""
        print(f"  --alpha-{rep:02d}: {rep / 100:g};{note}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
