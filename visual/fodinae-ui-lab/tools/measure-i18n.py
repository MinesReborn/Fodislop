#!/usr/bin/env python3
"""
Линейка текста. Печатает только числа — ни оценок, ни exit-кода.

Отвечает на два вопроса, из которых выводится всё остальное:
  1. Насколько текст растёт при переводе (кривая роста по длине оригинала).
  2. Сколько текстовых узлов макета объявили, что делать при нехватке места.

Источник роста — реальная пара словарей игры (en/ru), а не таблица из статьи.
"""
import json
import pathlib
import re
import statistics
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
GAME = ROOT.parent.parent
DICTS = GAME / "Assets" / "Resources" / "Localization"

# Границы корзин по длине оригинала. Выбраны не на глаз: рост — функция от
# длины (короткая строка не может «размазать» служебные морфемы по тексту),
# и корзины должны различать именно короткое от длинного.
BUCKETS = [(1, 5), (6, 10), (11, 20), (21, 40), (41, 10**6)]


def bucket_of(n: int) -> tuple[int, int]:
    for lo, hi in BUCKETS:
        if lo <= n <= hi:
            return (lo, hi)
    return BUCKETS[-1]


def label(b: tuple[int, int]) -> str:
    return f"{b[0]}-{b[1]}" if b[1] < 10**6 else f"{b[0]}+"


def growth_curve(base: str = "en", target: str = "ru") -> dict:
    """Кривая роста target/base по корзинам длины оригинала."""
    a = json.loads((DICTS / f"{base}.json").read_text(encoding="utf-8"))
    b = json.loads((DICTS / f"{target}.json").read_text(encoding="utf-8"))
    per: dict[tuple[int, int], list[float]] = {bk: [] for bk in BUCKETS}
    worst: list[tuple[float, str, str, str]] = []
    for k, src in a.items():
        dst = b.get(k)
        if not dst or not src:
            continue
        f = len(dst) / len(src)
        per[bucket_of(len(src))].append(f)
        worst.append((f, k, src, dst))
    worst.sort(reverse=True)
    return {"per": per, "worst": worst, "n_base": len(a), "n_target": len(b)}


def pct(v: list[float], p: float) -> float:
    return sorted(v)[min(len(v) - 1, int(p * len(v)))]


def report_dicts() -> None:
    print("СЛОВАРИ")
    langs = sorted(p.stem for p in DICTS.glob("*.json"))
    keys = {}
    for lang in langs:
        keys[lang] = set(json.loads((DICTS / f"{lang}.json").read_text(encoding="utf-8")))
    base = "en" if "en" in langs else langs[0]
    print(f"  языков: {len(langs)} ({', '.join(langs)})")
    for lang in langs:
        missing = keys[base] - keys[lang]
        extra = keys[lang] - keys[base]
        print(f"  {lang:<4} ключей {len(keys[lang]):<5} нет от {base}: {len(missing):<4} лишних: {len(extra)}")

    c = growth_curve()
    print("\nРОСТ ru/en ПО ДЛИНЕ ОРИГИНАЛА (символы)")
    print(f"  {'корзина':<8} {'n':>5} {'med':>6} {'p90':>6} {'p99':>6} {'max':>6}")
    for bk in BUCKETS:
        v = c["per"][bk]
        if not v:
            continue
        print(f"  {label(bk):<8} {len(v):>5} {statistics.median(v):>6.2f} "
              f"{pct(v, .90):>6.2f} {pct(v, .99):>6.2f} {max(v):>6.2f}")
    allv = [x for bk in BUCKETS for x in c["per"][bk]]
    print(f"  {'ВСЕ':<8} {len(allv):>5} {statistics.median(allv):>6.2f} "
          f"{pct(allv, .90):>6.2f} {pct(allv, .99):>6.2f} {max(allv):>6.2f}")
    print("\n  худшие 8:")
    for f, k, src, dst in c["worst"][:8]:
        print(f"    x{f:<5.2f} {k:<32} {src[:28]!r} -> {dst[:32]!r}")


TEXT_NODE = re.compile(r">([^<>]+)<")
HAS_LETTER = re.compile(r"[A-Za-zА-Яа-яЁё]")


def report_markup() -> None:
    html = (ROOT / "index.html").read_text(encoding="utf-8")
    body = re.sub(r"<script.*?</script>|<style.*?</style>", "", html, flags=re.S)

    nodes = [t.strip() for t in TEXT_NODE.findall(body)]
    nodes = [t for t in nodes if t and HAS_LETTER.search(t)]
    lens = sorted(len(t) for t in nodes)
    print("\nТЕКСТ В МАКЕТЕ")
    print(f"  текстовых узлов: {len(nodes)}  уникальных: {len(set(nodes))}  символов: {sum(lens)}")
    print(f"  длина: med={lens[len(lens)//2]} p90={pct([float(x) for x in lens], .9):.0f} max={lens[-1]}")
    per: dict[str, int] = {}
    for t in nodes:
        per[label(bucket_of(len(t)))] = per.get(label(bucket_of(len(t))), 0) + 1
    print("  по корзинам: " + "  ".join(f"{k}={per.get(k,0)}" for k in (label(b) for b in BUCKETS)))

    fit = re.findall(r'data-fit=["\']([a-z-]+)["\']', html)
    print(f"\n  объявили поведение (data-fit): {len(fit)}")
    if fit:
        seen: dict[str, int] = {}
        for f in fit:
            seen[f] = seen.get(f, 0) + 1
        for k in sorted(seen):
            print(f"    {k:<10} {seen[k]}")

    css = "\n".join(p.read_text(encoding="utf-8")
                    for p in [ROOT / "styles.css", *sorted((ROOT / "css").rglob("*.css"))]
                    if "styleguide" not in p.name)
    nowrap = len(re.findall(r"white-space:\s*nowrap", css))
    ell = len(re.findall(r"text-overflow:\s*ellipsis", css))
    print(f"\n  white-space: nowrap  {nowrap}")
    print(f"  text-overflow: ellipsis  {ell}")
    widths = re.findall(r"(?<![-a-z])(max-width|min-width|width):\s*([0-9.]+)(px|rem)", css)
    kinds = {k: sum(1 for w in widths if w[0] == k) for k in ("width", "max-width", "min-width")}
    print(f"  ширин в px/rem: {len(widths)}  " + " ".join(f"{k}={v}" for k, v in kinds.items()))


if __name__ == "__main__":
    if not DICTS.exists():
        print(f"нет словарей: {DICTS}", file=sys.stderr)
        sys.exit(2)
    report_dicts()
    report_markup()
