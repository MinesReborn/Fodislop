#!/usr/bin/env python3
"""Генератор: css/tokens.css -> ThemeTokens.uss + TokenUtilities.uss + палитра.

ЗАЧЕМ

Связь макета и игры держалась на человеческом договоре: шапка ThemeTokens.uss
просила «меняя значение здесь, поменяй его и там». Договор нарушился молча.
Замер перед написанием генератора: шестнадцать токенов разошлись по значению,
худший — --border-subtle: 0.08 в макете против 0.22 в игре, втрое ярче, почти
на каждой поверхности. Увидеть это можно было только сравнив два файла руками.

Генератор убирает не расхождение, а саму возможность расхождения.

ПОЧЕМУ ФАЙЛ ЦЕЛИКОМ МАШИННЫЙ

Первая версия умела «сшивку»: писала свою секцию между маркерами и бережно
обходила чужое. Это была подпорка, а не система, и она уже дала сбой — старый
:root объявлял те же токены ПОСЛЕ машинных и перебивал их, то есть генератор
работал вхолостую. Вместо подпорки файл разобрали:
  • четыре слоя псевдонимов (--color-*, --mm-*, --scifi-*, --btn-*) схлопнуты
    в семантический слой — 291 подстановка, 69 объявлений удалено;
  • 27 компонентных правил .sci-fi-* уехали в SciFi.uss.
После этого в ThemeTokens.uss не осталось ничего, кроме токенов, и сшивка
стала не нужна.

ПЯТЬ ПРЕОБРАЗОВАНИЙ CSS -> USS

  1. var() раскрывается до конца: в USS нет слоя примитивов.
  2. rgb(var(--rgb-x) / N%) -> rgba(r, g, b, 0.N). Именно на этом переводе,
     который делался руками, и разъехались 16 значений.
  3. Отбрасывается непереносимое: --hex-*/--rgb-*/--mat-* (их роль выполняет
     раскрытие), --blur-* (нет backdrop-filter), --layer-* (нет z-index),
     --fit-lines (нет line-clamp), шрифтовая скоропись (нет shorthand font).
  4. cubic-bezier -> именованная кривая: в USS 23 имени и ни одной свободной.
     Подбор сделан tools/fit-easing.py, отклонение записано рядом со строкой.
  5. Гарнитура -> путь к SDF-ассету. Это не расхождение, а правильная
     подстановка, поэтому таблица задана явно и путь проверяется.
"""

import argparse
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
REPO = ROOT.parent.parent
TOKENS = ROOT / "css" / "tokens.css"
STYLES = REPO / "Assets" / "Resources" / "Styles"
THEME = STYLES / "ThemeTokens.uss"
UTILS = STYLES / "TokenUtilities.uss"
PALETTE = STYLES / "token-palette.json"

WARNING = ("/* ФАЙЛ МАШИННЫЙ. Правки будут затёрты.\n"
           "   Источник истины: visual/fodinae-ui-lab/css/tokens.css\n"
           "   Генератор:       visual/fodinae-ui-lab/tools/emit-uss-tokens.py\n"
           "   Расхождение ловит CI (scripts/check-architecture.js). */\n")

DROP_PREFIX = ("--hex-", "--rgb-", "--mat-", "--blur-", "--layer-", "--z-")
DROP_EXACT = {"--fit-lines"}

FONT_ASSETS = {
    "--face-body": "Assets/Resources/Fonts/Exo2_SDF.asset",
    "--face-data": "Assets/Resources/Fonts/JetBrainsMono_SDF.asset",
    "--face-display": "Assets/Resources/Fonts/Unbounded_SDF.asset",
}

EASING = {
    "cubic-bezier(0.2,0.75,0.2,1)": ("ease-out-circ", "подбор fit-easing.py, max-отклонение 0.129"),
    "cubic-bezier(0.4,0,0.2,1)": ("ease-in-out", None),
    "cubic-bezier(0,0.2,0.8,1)": ("ease-out-cubic", None),
}

# Имена тиров НЕ придуманы здесь: их объявляет и ставит
# Assets/Scripts/UI/Common/Interaction/UILayoutTier.cs.
TIERS = {
    "max-width: 899px": "tier--compact",
    "min-width: 1600px": "tier--wide",
}


def read_blocks(src: str):
    """(:root по умолчанию, {класс тира: {токен: значение}})."""
    base: dict[str, str] = {}
    tiers: dict[str, dict[str, str]] = {}
    current: dict[str, str] | None = None
    in_media = False

    for line in src.splitlines():
        media = re.match(r"\s*@media\s*\(([^)]*)\)", line)
        if media:
            in_media = True
            cls = TIERS.get(media.group(1).strip())
            current = tiers.setdefault(cls, {}) if cls else None
            continue
        if re.match(r"\s*:root\s*\{", line):
            if not in_media:
                current = base
            continue
        if line.startswith("}"):
            in_media, current = False, None
            continue
        decl = re.match(r"\s*(--[\w-]+)\s*:\s*([^;]+);", line)
        if decl and current is not None:
            current.setdefault(decl.group(1), decl.group(2).strip())
    return base, tiers


def resolve(value: str, table: dict[str, str], depth: int = 0) -> str:
    if depth > 12:
        return value

    def sub(m: re.Match) -> str:
        name = m.group(1).strip()
        return resolve(table[name], table, depth + 1) if name in table else m.group(0)

    value = re.sub(r"var\(\s*(--[\w-]+)\s*\)", sub, value).strip()
    m = re.fullmatch(r"rgb\(\s*([\d\s,]+?)\s*/\s*([\d.]+)%\s*\)", value)
    if m:
        parts = [p for p in re.split(r"[,\s]+", m.group(1).strip()) if p]
        return f"rgba({', '.join(parts)}, {round(float(m.group(2)) / 100, 4):g})"
    return value


def convert(name: str, value: str):
    """Значение для USS и пояснение, либо None — если токен не переносится."""
    if name in DROP_EXACT or name.startswith(DROP_PREFIX):
        return None

    if name in FONT_ASSETS:
        path = FONT_ASSETS[name]
        if not (REPO / path).exists():
            sys.exit(f"нет SDF-ассета для {name}: {path}")
        return f'url("project://database/{path}")', None
    if name.startswith("--face-"):
        sys.exit(f"{name}: гарнитура без SDF-ассета — добавьте её в FONT_ASSETS")

    flat = value.replace(" ", "")
    if flat in EASING:
        return EASING[flat]
    if "cubic-bezier" in value:
        sys.exit(f"{name}: кривая {value} не подобрана, запустите tools/fit-easing.py")

    # font: weight size/leading family — в USS есть только longhand.
    if re.match(r"^\d+\s+\S+\s*/\s*\S+\s", value):
        return None

    # Относительные единицы USS не понимает вовсе: letter-spacing принимает
    # только пиксели. Пересчитать em в px статически нельзя — величина зависит
    # от кегля, а он у каждого правила свой. Поймано импортом Unity:
    # «Unsupported unit: '0.04em'».
    if re.search(r"[\d.]+(em|rem|ch|ex|vw|vh|vmin|vmax)\b", value):
        return None

    return value, None


def emit_tokens(base, tiers) -> str:
    out = [WARNING, ":root {"]
    dropped = []
    for name, raw in base.items():
        got = convert(name, resolve(raw, base))
        if got is None:
            dropped.append(name)
            continue
        value, note = got
        out.append(f"    {name}: {value};" + (f"  /* {note} */" if note else ""))
    out.append("}")

    for cls, table in tiers.items():
        merged = {**base, **table}
        out += ["", "/* Тир задаётся классом на корневом элементе: @media в USS нет.",
                "   Класс ставит UILayoutTier.cs. */", f".{cls} {{"]
        for name in table:
            got = convert(name, resolve(merged[name], merged))
            if got:
                out.append(f"    {name}: {got[0]};")
        out.append("}")

    # Имена в комментарии пишутся БЕЗ ведущих дефисов: парсер USS видит «--имя»
    # даже внутри комментария, принимает за объявление и падает с ColonMissing.
    names = ", ".join(n.lstrip("-") for n in sorted(dropped))
    out += ["", f"/* Не переносится в USS ({len(dropped)}): {names} */"]
    return "\n".join(out) + "\n"


# Утилиты без токенов: состояние и раскладка.
#
# Они не выводятся из tokens.css, потому что значения у них не палитровые —
# это структура. Но печатаются тем же генератором и в тот же файл, чтобы у
# утилитарного слоя был один автор: два автора у одного файла — это способ
# снова разъехаться, чего весь этот генератор и должен не допустить.
#
# .is-hidden закрывает 41 запись element.style.display в коде. Имя взято из
# макета (css/components.css §8), а не придумано здесь.
#
# Пропусков (gap) тут нет намеренно: USS не понимает gap/column-gap/row-gap.
# Расстояние между детьми задаётся отступами — см. долг переноса в USS.
STRUCTURAL = """
/* Состояние: показать/скрыть без инлайна. */
.is-hidden { display: none; }

/* Раскладка: направление и выравнивание для серверных контейнеров. */
.row { flex-direction: row; }
.row-reverse { flex-direction: row-reverse; }
.col { flex-direction: column; }
.col-reverse { flex-direction: column-reverse; }

.ai-start { align-items: flex-start; }
.ai-center { align-items: center; }
.ai-end { align-items: flex-end; }
.ai-stretch { align-items: stretch; }

.jc-start { justify-content: flex-start; }
.jc-center { justify-content: center; }
.jc-end { justify-content: flex-end; }
.jc-between { justify-content: space-between; }
.jc-around { justify-content: space-around; }

.as-start { align-self: flex-start; }
.as-center { align-self: center; }
.as-end { align-self: flex-end; }
.as-stretch { align-self: stretch; }

/* Положение: абсолютное окно/маркер, координаты остаются вычисляемыми. */
.abs { position: absolute; }
.rel { position: relative; }

/* Центр экрана: константа, а не вычисление. Серверные окна ставились так
   инлайном, из-за чего окно нельзя было сдвинуть ни темой, ни тиром. */
.centered {
    position: absolute;
    left: 50%;
    top: 50%;
    translate: -50% -50%;
}

/* === ось fit: что делает текст, когда места не хватает ====================
   Перенос контракта data-fit из макета (css/text.css) в USS. Там поведение
   выбирается атрибутом, здесь — классом: селекторов по атрибуту в USS нет.

   Каждый вариант собран НАТИВНЫМИ средствами UI Toolkit, без замеров из C#.
   Свойства внутри варианта не разделяются: text-overflow без nowrap и
   overflow: hidden не срабатывает вовсе, а min-width: 0 обязателен, иначе
   флекс-ребёнок не сжимается ниже содержимого и многоточие не наступает
   никогда — правило есть, эффекта нет.

   overflow-wrap из макета не переносится: в USS его нет, перенос внутри
   слова недоступен. Слово длиннее коробки остаётся за max-width родителя. */

/* Растёт вниз. Умолчание для прозы. */
.fit-wrap {
    white-space: normal;
    min-width: 0;
}

/* Неделимо по смыслу: измерение, код, подпись действия. Не переносится и не
   обрезается — уступать должна раскладка, а не значение. */
.fit-atomic {
    white-space: nowrap;
    flex-shrink: 0;
    flex-grow: 0;
}

/* Одна строка, хвост в многоточие. */
.fit-clip {
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    -unity-text-overflow-position: end;
    min-width: 0;
}

/* Растёт вниз, но не бесконечно. Числа строк в USS нет (line-clamp там
   отсутствует), потолок задаётся высотой: max-height на самом компоненте,
   как размер шрифта × межстрочный × число строк. Утилита даёт обрезку и
   многоточие, потолок остаётся за компонентом — иначе класс диктовал бы
   кегль, которого не знает. */
.fit-clamp {
    white-space: normal;
    overflow: hidden;
    text-overflow: ellipsis;
    min-width: 0;
}

/* Кегль подгоняется под коробку. Единственный случай, когда текст обязан
   читаться целиком, а коробка не может ни вырасти, ни спрятать хвост.
   Нижняя граница обязательна: ужатое ниже size-micro нечитаемо, и дальше
   уменьшать — менять один тихий дефект на другой. Не влезло на дне — это
   случай для детектора, а не для меньшего числа. */
.fit-shrink {
    white-space: nowrap;
    overflow: hidden;
    min-width: 0;
    -unity-text-auto-size: best-fit var(--size-micro) var(--size-lg);
}

.grow { flex-grow: 1; }
.no-grow { flex-grow: 0; }
.no-shrink { flex-shrink: 0; }
"""

def emit_utilities(base) -> str:
    """Структурные утилиты: класс вместо инлайна там, где вид выбирает клиент.

    Здесь были ещё 278 классов на каждую роль палитры — bg-surface-panel,
    pad-t-space-6 и так далее. Они существовали ради одного потребителя:
    StyleApplicator примагничивал серверный ARGB к ближайшему токену и вешал
    такой класс. Примагничивание отменено (протокол надо соблюдать, а не
    интерпретировать), потребителей у токенных классов не осталось ни одного —
    ни в разметке, ни в коде, — и они удалены вместе с ним.

    Оставшееся describe не палитру, а структуру: видимость, направление
    флекса, выравнивание, положение. Значения у них не токенные, и оттого они
    не выводятся из tokens.css — но печатаются тем же генератором, чтобы у
    файла был один автор.
    """
    return "\n".join([WARNING, STRUCTURAL.strip()]) + "\n"


def emit_palette(base) -> str:
    """Таблица примагничивания для StyleApplicator: сервер шлёт ARGB и пиксели,
    клиент ищет ближайший токен и выдаёт класс. Таблица машинная, потому что
    палитра меняется вместе с макетом."""
    colors, space = {}, {}
    for name, raw in base.items():
        if name.startswith(DROP_PREFIX):
            continue
        value = resolve(raw, base)
        m = re.fullmatch(r"rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)(?:[,\s]+([\d.]+))?\s*\)", value)
        if m:
            colors[name] = [int(float(m.group(i))) for i in (1, 2, 3)] + \
                [round(float(m.group(4)) if m.group(4) else 1.0, 4)]
            continue
        m = re.fullmatch(r"#([0-9a-fA-F]{6})", value)
        if m:
            h = m.group(1)
            colors[name] = [int(h[i:i + 2], 16) for i in (0, 2, 4)] + [1.0]
            continue
        if name.startswith("--space-"):
            px = re.fullmatch(r"(\d+)px", value)
            if px:
                space[name] = int(px.group(1))
    return json.dumps({
        "_": "Машинный файл. Источник visual/fodinae-ui-lab/css/tokens.css, "
             "генератор tools/emit-uss-tokens.py. Правки будут затёрты.",
        "colors": dict(sorted(colors.items())),
        "space": dict(sorted(space.items(), key=lambda kv: kv[1])),
    }, ensure_ascii=False, indent=2) + "\n"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true",
                    help="ничего не писать; выйти с 1, если игра разошлась с макетом")
    args = ap.parse_args()

    base, tiers = read_blocks(TOKENS.read_text(encoding="utf-8"))
    targets = {
        THEME: emit_tokens(base, tiers),
        UTILS: emit_utilities(base),
        PALETTE: emit_palette(base),
    }
    stale = [p for p, text in targets.items()
             if not p.exists() or p.read_text(encoding="utf-8") != text]

    if args.check:
        if stale:
            print("ТОКЕНЫ ИГРЫ РАЗОШЛИСЬ С МАКЕТОМ:")
            for p in stale:
                print(f"  {p.relative_to(REPO)}")
            print("\n  Источник истины — visual/fodinae-ui-lab/css/tokens.css")
            print("  Выполните: python3 visual/fodinae-ui-lab/tools/emit-uss-tokens.py")
            return 1
        print(f"игра совпадает с макетом ({len(base)} токенов, {len(tiers)} тира)")
        return 0

    for p, text in targets.items():
        p.write_text(text, encoding="utf-8")
    print(f"прочитано токенов: {len(base)}, тиров: {len(tiers)}")
    for p in targets:
        print(f"  {p.relative_to(REPO)}" + ("  (изменился)" if p in stale else ""))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
