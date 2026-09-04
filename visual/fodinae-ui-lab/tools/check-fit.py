#!/usr/bin/env python3
"""
Перенос контракта data-fit в игру.

Зачем отдельная проверка, а не ветка в compare-components.py: контракт макета
записан селекторами по атрибуту (`[data-fit='clip']`), а в USS селекторов по
атрибуту не существует. Сверка компонентов такие правила исключает намеренно —
значит поведение при нехватке места она не увидит НИКОГДА, сколько её ни
расширяй. Здесь читается сам атрибут на узле, а не правило под него.

Молчание игры по этой оси — не «не сказано», а «текст вылезет»: умолчание USS
никогда не обрежет и не ужмёт строку.

Проверяются только узлы, чей класс есть в карте пар. Узел без пары — не долг,
а ответ: такого компонента в игре нет.
"""
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
REPO = ROOT.parent.parent
GAME_STYLES = REPO / "Assets" / "Resources" / "Styles"
UXML = REPO / "Assets" / "Resources" / "UI"
MAP = ROOT / "component-map.json"
INDEX = ROOT / "index.html"

# Что обязана сказать игра на каждый вариант. Свойства внутри варианта не
# делятся: text-overflow без nowrap и overflow: hidden не срабатывает вовсе.
REQUIRED = {
    "wrap":   {"white-space": "normal"},
    "atomic": {"white-space": "nowrap", "flex-shrink": "0"},
    "clip":   {"white-space": "nowrap", "overflow": "hidden",
               "text-overflow": "ellipsis"},
    "clamp":  {"white-space": "normal", "overflow": "hidden",
               "text-overflow": "ellipsis"},
    "shrink": {"-unity-text-auto-size": None},  # значение задаёт компонент
}

# Ниже этого числа пар «класс игры ↔ вариант» проверка считает себя
# сломанной, а не пройденной. Держится руками: упало честно — впишите новое.
FLOOR = 8

# Утилиты из TokenUtilities.uss: класс на элементе равносилен правилу.
UTILITY = {"wrap": "fit-wrap", "atomic": "fit-atomic", "clip": "fit-clip",
           "clamp": "fit-clamp", "shrink": "fit-shrink"}

NODE = re.compile(r"<(\w+)((?:\s+[-\w]+=(?:\"[^\"]*\"|'[^']*'))*)\s*/?>")
ATTR = re.compile(r"([-\w]+)=[\"']([^\"']*)[\"']")


def declarations(paths):
    """Класс → накопленные свойства всех правил, где он последний компонент."""
    out: dict[str, dict[str, str]] = {}
    for path in paths:
        text = re.sub(r"/\*[\s\S]*?\*/", " ", path.read_text(encoding="utf-8"))
        for selectors, body in re.findall(r"([^{}]+)\{([^{}]*)\}", text):
            props = dict(
                (k.strip(), v.strip())
                for k, v in (d.split(":", 1) for d in body.split(";") if ":" in d))
            for selector in selectors.split(","):
                selector = " ".join(selector.split())
                if ":" in selector:
                    continue  # состояние не отвечает за покой
                last = re.split(r"[\s>+~]", selector)[-1]
                names = re.findall(r"\.([\w-]+)", last)
                if len(names) == 1:
                    out.setdefault(names[0], {}).update(props)
    return out


def classes_in_uxml():
    """Класс игры → набор наборов классов, с которыми он встречается."""
    seen: dict[str, list[set[str]]] = {}
    for path in sorted(UXML.rglob("*.uxml")):
        for m in re.finditer(r'class="([^"]*)"', path.read_text(encoding="utf-8")):
            names = set(m.group(1).split())
            for n in names:
                seen.setdefault(n, []).append(names)
    return seen


def mirror_nodes():
    """Узлы макета с объявленным поведением: (классы, вариант)."""
    html = INDEX.read_text(encoding="utf-8")
    html = re.sub(r"<script[\s\S]*?</script>|<style[\s\S]*?</style>", " ", html)
    out = []
    for tag, attrs in NODE.findall(html):
        a = dict(ATTR.findall(attrs))
        fit = a.get("data-fit")
        if fit:
            out.append((set(a.get("class", "").split()), fit))
    return out


def main() -> int:
    pairs = json.loads(MAP.read_text(encoding="utf-8"))
    to_game = {m: g for section, entries in pairs.items() if section != "_"
               for g, m in entries.items()}
    uss = declarations(sorted(GAME_STYLES.glob("*.uss")))
    uxml = classes_in_uxml()

    # Вариант выбирает элемент, а не класс: в макете одна и та же кнопка
    # где-то wrap, где-то atomic. Поэтому сначала — кто из классов вообще
    # однозначен, и только однозначные вправе объявляться правилом.
    variants: dict[str, set[str]] = {}
    unknown = []
    for names, fit in mirror_nodes():
        if fit not in REQUIRED:
            unknown.append(fit)
            continue
        for name in sorted(names):
            gname = to_game.get(name)
            if gname:
                variants.setdefault(gname, set()).add(fit)

    gaps: dict[tuple[str, str, str], str] = {}
    for fit in sorted(set(unknown)):
        gaps[("?", fit, "?")] = f"неизвестный вариант data-fit: {fit}"

    for gname, fits in sorted(variants.items()):
        uses = uxml.get(gname, [])
        if len(fits) > 1:
            # Разнобой законен, но тогда правило врёт по определению: поведение
            # обязано стоять на элементе утилитой, иначе один из вариантов молча
            # проиграет каскаду.
            bare = sum(1 for s in uses if not (s & set(UTILITY.values())))
            if bare:
                gaps[(gname, "/".join(sorted(fits)), "класс")] = (
                    f"{bare} элементов без утилиты fit-*")
            continue
        fit = next(iter(fits))
        have = uss.get(gname, {})
        if uses and all(UTILITY[fit] in s for s in uses):
            # Утилита равносильна правилу ровно до тех пор, пока правило о том
            # же свойстве молчит. TokenUtilities.uss импортируется в
            # FodinaeTheme.tss ВТОРЫМ, раньше всех тематических листов, а
            # специфичность у одного класса и там и там одинаковая — значит при
            # споре побеждает тематический лист, и утилита не делает ничего.
            clash = sorted(set(REQUIRED[fit]) & set(have))
            if clash:
                gaps[(gname, fit, ", ".join(clash))] = (
                    "утилиту перебивает правило класса: TokenUtilities.uss "
                    "импортируется раньше")
            continue
        for prop, want in REQUIRED[fit].items():
            got = have.get(prop)
            if got is None:
                gaps[(gname, fit, prop)] = "не сказано"
            elif want is not None and got != want:
                gaps[(gname, fit, prop)] = got

    paired = sum(len(v) for v in variants.values())
    for (gname, fit, prop), got in sorted(gaps.items()):
        want = REQUIRED.get(fit, {}).get(prop)
        print(f"  .{gname} (fit={fit}): {prop} = {got}"
              + (f", нужно {want}" if want else ""))
    print(f"контракт data-fit: {paired} пар класс↔вариант, {len(gaps)} пропусков")

    # Проверка, которой нечего проверять, обязана кричать, а не радоваться.
    # Макет будет перерисован заново, и разметка придёт без data-fit: тогда
    # variants окажется пустым, пропусков не найдётся ни одного, и проверка
    # доложит об успехе — ровно то молчание, ради которого всё это писалось.
    # Порог — не «сколько надо», а «ниже этого контракт точно отвалился».
    if paired < FLOOR:
        print(f"  контракт отвалился: пар {paired} при пороге {FLOOR}. "
              "Разметка потеряла data-fit — проверять стало нечего, и молчание "
              "здесь значит «смотрю не туда», а не «всё хорошо». Проставить "
              "заново по css/text.css; если пар честно стало меньше, впишите "
              "новое число в FLOOR.")
        return 1

    return 1 if gaps else 0


if __name__ == "__main__":
    sys.exit(main())
