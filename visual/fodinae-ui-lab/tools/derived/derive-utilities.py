#!/usr/bin/env python3
"""
Чем заменить 98 инлайн-стилей: вывод, а не подбор.

Метод. Каждый инлайн-набор — это множество объявлений. Утилита — тоже
множество. Задача: покрыть первое вторыми, жадно и по убыванию размера
(большая утилита выражает роль, мелкая — только свойство; роль всегда
предпочтительнее). Остаток — то, для чего утилиты нет.

Новые утилиты заводятся ТОЛЬКО по правилу, уже записанному в components.css:
паттерн, встречающийся не менее двух раз. Всё, что встретилось однажды, —
не утилита, а особенность экрана, и её место в css/screens/*.css.

Значения сравниваются РАЗРЕШЁННЫМИ: font-size:12px и var(--size-sm) — одно
и то же, и инструмент обязан это видеть, иначе он предложит завести второе
имя для существующей вещи.
"""
import collections
import pathlib
import re

# .parent.parent.parent: файл лежит в tools/derived, корень макета — на два выше.
ROOT = pathlib.Path(__file__).resolve().parent.parent.parent
UTIL_FILES = [ROOT / "css" / "base.css", ROOT / "css" / "components.css"]


def token_map() -> dict[str, str]:
    tok: dict[str, str] = {}
    for m in re.finditer(r"^\s*(--[\w-]+)\s*:\s*([^;]+);",
                         (ROOT / "css" / "tokens.css").read_text(encoding="utf-8"), re.M):
        tok.setdefault(m.group(1), m.group(2).strip())
    return tok


TOK = token_map()


def resolve(value: str, depth: int = 0) -> str:
    if depth > 6:
        return value
    return re.sub(r"var\((--[\w-]+)\)",
                  lambda m: resolve(TOK[m.group(1)], depth + 1) if m.group(1) in TOK else m.group(0),
                  value).strip()


def canon(decl: str) -> str:
    prop, _, val = decl.partition(":")
    return f"{prop.strip()}:{re.sub(r'\s+', ' ', resolve(val.strip()))}"


def utilities() -> dict[str, frozenset[str]]:
    out: dict[str, frozenset[str]] = {}
    for path in UTIL_FILES:
        for m in re.finditer(r"^(\.fdn-[\w-]+)\s*\{([^}]*)\}",
                             path.read_text(encoding="utf-8"), re.M):
            ds = frozenset(canon(d) for d in m.group(2).split(";") if d.strip())
            if ds:
                out[m.group(1)] = ds
    return out


def cover(target: set[str], utils: dict[str, frozenset[str]]) -> tuple[list[str], set[str]]:
    """Жадное покрытие: сначала утилиты, выражающие роль целиком."""
    chosen: list[str] = []
    rest = set(target)
    for name, ds in sorted(utils.items(), key=lambda kv: -len(kv[1])):
        if ds and ds <= rest:
            chosen.append(name)
            rest -= ds
    return chosen, rest


def main() -> None:
    utils = utilities()
    html = (ROOT / "index.html").read_text(encoding="utf-8")
    sets = re.findall(r'\sstyle="([^"]*)"', html)

    full, partial, bare = [], [], []
    residue: collections.Counter = collections.Counter()
    residue_sets: collections.Counter = collections.Counter()

    for s in sets:
        target = {canon(d) for d in s.split(";") if d.strip()}
        chosen, rest = cover(target, utils)
        (full if not rest else (partial if chosen else bare)).append((s, chosen, rest))
        for d in rest:
            residue[d] += 1
        if rest:
            residue_sets[frozenset(rest)] += 1

    print(f"инлайн-наборов: {len(sets)}")
    print(f"  выражаются существующими утилитами целиком : {len(full)}")
    print(f"  выражаются частично                        : {len(partial)}")
    print(f"  не выражаются вовсе                        : {len(bare)}")

    print(f"\nостаток: {sum(residue.values())} объявлений, {len(residue)} различных")
    print("\nКАНДИДАТЫ В УТИЛИТЫ (встретились ≥2 раз):")
    for d, n in residue.most_common():
        if n >= 2:
            print(f"  ×{n:<3} {d}")

    print("\nОДИНОЧКИ (в утилиты не идут — место в css/screens/*.css):")
    ones = [d for d, n in residue.items() if n == 1]
    print(f"  {len(ones)} объявлений")
    for d in sorted(ones)[:12]:
        print(f"    {d[:72]}")

    print("\nЦЕЛЫЕ ОСТАТКИ, повторяющиеся ≥2 раз (кандидаты в РОЛЕВУЮ утилиту):")
    for ds, n in residue_sets.most_common():
        if n >= 2:
            print(f"  ×{n}  {'; '.join(sorted(ds))[:96]}")


if __name__ == "__main__":
    main()
