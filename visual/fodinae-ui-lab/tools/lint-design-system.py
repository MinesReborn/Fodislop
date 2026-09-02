#!/usr/bin/env python3
"""Линтер дизайн-системы FODINAE.

Проверяет инварианты, которые легко нарушить вручную и невозможно заметить
глазом: неразрешённые токены, сырые значения вне шкал, протечку слоя
примитивов, утёкшие имена, недостаточный контраст.

    python3 tools/inventory.py            — измерения без приговора
    python3 tools/lint-design-system.py   — приговор

Запускается сборкой (scripts/check-architecture.js), руками не обязателен.
Карта всех инструментов макета — tools/README.md.

ХРАПОВИК. Проверка, которая падает в день своего появления, — это не проверка,
а список задач: она красная всегда и потому перестаёт быть сигналом. Поэтому
каждое правило живёт в одном из двух режимов:

  enforced  — нарушений ноль, любое новое валит сборку;
  baselined — текущее число записано в BASELINE, сборку валит только РОСТ.

Так правило начинает защищать нас сразу, не требуя сначала всё починить.
Уменьшив долг, подтяните число в BASELINE — линтер сам об этом напомнит.

Код возврата 1, если появились новые нарушения.
"""

from __future__ import annotations

import collections
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
# Словарь игры — источник истины по тексту; макет к нему присоединяется.
GAME_DICTS = ROOT.parent.parent / "Assets" / "Resources" / "Localization"
TOKENS = ROOT / "css" / "tokens.css"

CSS_FILES = sorted(ROOT.glob("css/**/*.css")) + [ROOT / "styles.css"]

# Витрина попадает в область проверки цветов, токенов и запрещённых имён:
# она — часть системы и разойтись с ней не должна. Но НЕ в область проверки
# инлайн-стилей: styleguide.html — образец, а не вёрстка, и часть его демо
# существует именно чтобы показать значение токена по месту.
SHOWCASE = [ROOT / "styleguide.html", ROOT / "js" / "styleguide.js"]
ALL_FILES = CSS_FILES + [ROOT / "index.html", ROOT / "app.js"] + SHOWCASE

# Файлы, которым разрешено называть слой примитивов: tokens.css объявляет его,
# витрина его показывает. Всем остальным §1 предписывает семантику.
PRIMITIVE_EXEMPT = {TOKENS, ROOT / "css" / "styleguide.css", ROOT / "js" / "styleguide.js"}

# Цвета, которые разрешено писать сырыми вне tokens.css.
COLOR_ALLOWLIST = {"transparent", "currentColor", "inherit", "none"}

# Свойства, у которых множество допустимых литералов равно множеству ступеней
# соответствующей шкалы в tokens.css. Шкала ЧИТАЕТСЯ из tokens.css, а не
# дублируется здесь: расширив шкалу, вы автоматически расширяете допустимое.
SCALED = [
    ("font-size", "--size-", r"font-size:\s*([\d.]+px)\b", set()),
    ("border-radius", "--radius-", r"border-radius:\s*([\d.]+(?:px|%))\b", {"0", "0px"}),
    ("blur()", "--blur-", r"\bblur\(([\d.]+px)\)", set()),
    ("длительность перехода", "--dur-", r"transition[^;:]*:[^;]*?([\d.]+m?s)\b", {"0s"}),
    # Кривая — такое же значение со шкалы, как длительность. Проверялись
    # только длительности, и ФИРМЕННАЯ кривая (планета, рейл, шапка) жила
    # литералом при объявленном --ease-signature: токен, названный главным
    # в витрине, не использовался ни разу.
    ("кривая перехода", "--ease-", r"(cubic-bezier\([^)]*\))", set()),
]

# Свойства, НЕ проверяемые намеренно. --space-* — шкала ритма, выведенная из
# частоты; структурная геометрия законно живёт вне её (хедер 64px, волосяная
# рамка 1px, 100%, --planet-disc). Проверять их — значит утопить сигнал в шуме.
# Вместо приговора инвентаризация печатает их распределение.
UNCHECKED = ("width", "height", "padding", "margin", "gap", "top", "left", "right", "bottom")

# Классы контраста по WCAG 2.1: обычный текст 4.5, крупный (>=18px) 3.0.
CONTRAST_PAIRS = [
    ("--hex-ink-100", "--hex-void", 4.5, "основной текст"),
    ("--hex-ink-70", "--hex-void", 4.5, "вторичный текст"),
    ("--hex-ink-50", "--hex-void", 4.5, "третичный текст"),
    ("--text-on-gold", "--hex-gold", 4.5, "текст на золотой кнопке"),
]

# ═══════════════════════════════════════════════════════════════════════════
# ХРАПОВИК
#
# Число — количество нарушений, измеренное на момент появления проверки.
# Проверка валит сборку, только если счётчик ВЫРОС. Ноль означает enforced:
# правило уже соблюдается полностью и любое новое нарушение — ошибка.
#
# Уменьшив долг, подтяните число: линтер печатает напоминание сам.
# Измерено 2026-08-30.
# ═══════════════════════════════════════════════════════════════════════════

BASELINE = {
    "неразрешённый токен": 0,
    "сырой цвет": 0,
    "запрещённое имя": 0,
    "контраст": 0,
    "инлайн-стили": 0,
    "геометрия по данным": 14,
    "протечка примитивов": 0,
    "значение мимо шкалы": 0,
    "z-index мимо шкал": 0,
    "текст без ключа": 0,
    "сейф-зона мимо лестницы": 0,
    # 247, а не 245: два ключа HDR («HDR» и «Вывод HDR» в четырёх языках)
    # добавлены владельцем текстов вне этого захода. Тексты пишет человек,
    # линтер их только считает.
    "регистр запечён в текст": 247,
    "цветной эмодзи": 0,
    # 167 -> 124: тени и матовое стекло перестали быть потерей. С Unity 6000.6
    # у UI Toolkit есть filter и backdrop-filter, и 30 box-shadow, 4
    # drop-shadow и 9 backdrop-filter переносятся, а не обходятся текстурой.
    "долг переноса в USS": 124,
    "ключа нет в игре": 263,
}

# Проверки, у которых ненулевой предел — это ОБЪЯВЛЕННАЯ НОРМА, а не долг.
# Смешивать их с долгом нельзя: долг зовёт его разобрать, норма — не трогать.
ALLOWED = {"геометрия по данным"}

findings: dict[str, list[str]] = collections.defaultdict(list)


def css_code(path: pathlib.Path) -> str:
    """Файл без комментариев, с сохранением нумерации строк.

    Проверка шкал читала .css построчно и считала стилем ВСЮ строку — включая
    комментарий. Из-за этого объяснение «три кнопки несли padding:4px 10px;
    font-size:12px» само становилось нарушением: инструмент предъявлял
    претензию к собственной документации. Комментарий — не объявление.
    """
    src = read(path)
    out, i, depth = [], 0, 0
    while i < len(src):
        if depth == 0 and src.startswith("/*", i):
            depth, i = 1, i + 2
            out.append("  ")
            continue
        if depth and src.startswith("*/", i):
            depth, i = 0, i + 2
            out.append("  ")
            continue
        ch = src[i]
        out.append(ch if (depth == 0 or ch == "\n") else " ")
        i += 1
    return "".join(out)


def report(check: str, message: str) -> None:
    findings[check].append(message)


def read(path: pathlib.Path) -> str:
    return path.read_text(encoding="utf-8") if path.exists() else ""


STYLE_ATTR = re.compile(r'style="([^"]*)"', re.I)
JS_STYLE = re.compile(r"\.style\.[a-zA-Z]+\s*=\s*['\"]([^'\"]*)['\"]")


def style_chunks(path: pathlib.Path, line: str) -> list[str]:
    """Куски строки, которые являются СТИЛЕМ, а не содержимым.

    В .css стилем является вся строка. В разметке — только значение атрибута
    style=: текст страницы это контент, и пароль `MiningPassword#2026`, сид
    сервера `#849201` или пример кода в витрине стилем не являются. В .js —
    только присваивания element.style.X.
    """
    if path.suffix == ".css":
        return [line]

    if path.suffix == ".html":
        return STYLE_ATTR.findall(line)

    return JS_STYLE.findall(line)


def scale_steps(prefix: str) -> dict[str, str]:
    """Ступени шкалы из tokens.css: значение -> имя токена."""
    steps: dict[str, str] = {}
    for name, value in re.findall(
        rf"({re.escape(prefix)}[a-z0-9-]+)\s*:\s*([^;]+);", read(TOKENS), re.I
    ):
        steps.setdefault(normalize(value), name)

    return steps


def normalize(value: str) -> str:
    """`.2s` и `0.2s` — одно значение, записанное по-разному."""
    value = value.strip()
    return f"0{value}" if value.startswith(".") else value


# --------------------------------------------------------------------------
# 1. Каждый использованный токен должен быть объявлен
# --------------------------------------------------------------------------

def check_tokens_resolve() -> None:
    declared: set[str] = set()
    for f in CSS_FILES:
        declared |= set(re.findall(r"(--[a-z0-9-]+)\s*:", read(f), re.I))

    used: dict[str, set[str]] = {}
    for f in ALL_FILES:
        for line in read(f).splitlines():
            for chunk in style_chunks(f, line):
                for tok in re.findall(r"var\(\s*(--[a-z0-9-]+)", chunk, re.I):
                    used.setdefault(tok, set()).add(f.name)

    for tok in sorted(set(used) - declared):
        report(
            "неразрешённый токен",
            f"токен {tok} используется ({', '.join(sorted(used[tok]))}), но нигде не объявлен",
        )

    # Обратная проверка: объявлен, но ни разу не использован.
    unused = sorted(declared - set(used))
    if unused:
        print(f"  примечание: {len(unused)} объявленных токенов не используются: "
              f"{', '.join(unused[:8])}{' …' if len(unused) > 8 else ''}")


# --------------------------------------------------------------------------
# 2. Сырые цвета разрешены только в tokens.css
# --------------------------------------------------------------------------

def check_no_raw_colors() -> None:
    """Ищет сырые цвета только внутри стилей.

    Сканировать весь HTML нельзя: значение пароля `MiningPassword#2026`,
    сид сервера `#849201` и ID `#8849-0192` — это контент, а не CSS.
    """
    color = re.compile(r"#[0-9a-f]{3,8}\b|rgba?\(\s*\d+\s*,", re.I)

    for f in ALL_FILES:
        if f == TOKENS:
            continue
        for i, line in enumerate(read(f).splitlines(), 1):
            if "lint-ignore" in line:
                continue
            for chunk in style_chunks(f, line):
                for m in color.finditer(chunk):
                    report("сырой цвет", f"{f.name}:{i} сырой цвет {m.group(0)!r} вне tokens.css")


# --------------------------------------------------------------------------
# 3. Имена, которым не место в продакшне
# --------------------------------------------------------------------------

def check_forbidden_names() -> None:
    forbidden = {
        "genshin": "имя источника вдохновения в продакшн-классах",
        "--fa-": "устаревший префикс токенов, заменён семантическим слоем",
    }
    for f in ALL_FILES:
        text = read(f)
        for needle, why in forbidden.items():
            n = len(re.findall(re.escape(needle), text, re.I))
            if n:
                report("запрещённое имя", f"{f.name}: {n}× {needle!r} — {why}")


# --------------------------------------------------------------------------
# 4. Контраст
# --------------------------------------------------------------------------

def _srgb_to_linear(c: float) -> float:
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def luminance(hex_color: str) -> float:
    h = hex_color.lstrip("#")
    if len(h) == 3:
        h = "".join(ch * 2 for ch in h)
    r, g, b = (int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))
    return (0.2126 * _srgb_to_linear(r)
            + 0.7152 * _srgb_to_linear(g)
            + 0.0722 * _srgb_to_linear(b))


def contrast(fg: str, bg: str) -> float:
    a, b = luminance(fg), luminance(bg)
    lo, hi = sorted((a, b))
    return (hi + 0.05) / (lo + 0.05)


def check_contrast() -> None:
    text = read(TOKENS)
    values = dict(re.findall(r"(--[a-z0-9-]+)\s*:\s*(#[0-9a-fA-F]{3,8})\s*;", text))

    def resolve(name: str) -> str | None:
        if name in values:
            return values[name]
        m = re.search(rf"{re.escape(name)}\s*:\s*var\(\s*(--[a-z0-9-]+)\s*\)", text)
        return resolve(m.group(1)) if m else None

    for fg_name, bg_name, minimum, label in CONTRAST_PAIRS:
        fg, bg = resolve(fg_name), resolve(bg_name)
        if not fg or not bg:
            report("контраст", f"не удалось разрешить {fg_name} или {bg_name}")
            continue
        ratio = contrast(fg, bg)
        mark = "ok" if ratio >= minimum else "НИЖЕ НОРМЫ"
        line = f"  {label:28s} {fg} на {bg} = {ratio:5.2f}:1  (нужно {minimum})  {mark}"
        print(line)
        if ratio < minimum:
            report("контраст", f"{label}: {ratio:.2f}:1 при норме {minimum}:1")


# --------------------------------------------------------------------------
# 5. Инлайн-стили в разметке
# --------------------------------------------------------------------------

def check_inline_styles() -> None:
    """Инлайн-оформление: было 98, стало 0. И почему путь оказался не тем.

    План предполагал, что 112 из ~200 объявлений ложатся на существующие
    утилиты (.fdn-text--*, .fdn-font-*): значения ведь совпадают. Значения
    совпадают. Не совпадает МЕСТО В КАСКАДЕ. Инлайн побеждает всегда; утилита
    живёт до экранов, и экранное правило её перебивает. Измерено: подстановка
    84 объявлений дала 1354 расхождения вычисленных стилей; перенос утилит
    последним слоем — 291 расхождение в обратную сторону.

    Правильный путь нашёлся разложением по осям, а не по наборам целиком.
    98 применений давали 92 РАЗНЫХ набора — «паттерн дважды» не срабатывал
    ни разу. Но 290 объявлений внутри них распались на три оси: 130
    типографики, 116 раскладки, 37 поверхности. И тогда стало видно, что
    большинство инлайнов — это НЕОБЪЯВЛЕННЫЕ ВАРИАНТЫ уже существующих
    компонентов: шесть ширин модалки без шкалы ширин, две ступени размера
    кнопки без ступеней, три тона полосы прогресса без тонов.

    Отсюда три исхода вместо одного:
      1. вариант компонента (--sm, --danger, --md ...) — там, где инлайн
         менял существующий компонент;
      2. именованная роль в файле своего экрана — там, где коробка была
         безымянной и уникальной;
      3. ступень зазора у .fdn-row/.fdn-stack — там, где различие было
         только в одном числе.

    ГРАНИЦА, которая осталась. Инлайн допустим ровно там, где значение
    вычисляет программа, а не выбирает автор: ширина полосы прогресса,
    координаты точки на радаре. Это не тема, это состояние, и в классе ему
    места нет. Такие места считаются отдельно и не смешиваются с долгом.

    Доказательство переноса — не глаз и не линтер, а отпечаток вычисленных
    стилей на 18 разрезах (tools/computed-snapshot.js): после переезда
    осталось восемь видов различий, все восемь — намеренные снапы к шкале
    (36->38 заголовок, 22->20 версия, 5px->4px паддинг и так далее).
    Две собственные ошибки поймал он же: класс, приклеенный к соседнему
    <span> на той же строке, и вариант, проигравший базе из-за порядка
    импортов (modals.css идёт раньше reconnect.css, где жила база кнопок).
    """
    theme = data = 0
    for src_file in [ROOT / "index.html", ROOT / "app.js"] + SHOWCASE:
        src = read(src_file)
        for m in re.finditer(r'\sstyle="([^"]*)"', src):
            decls = [d.strip() for d in m.group(1).split(";") if d.strip()]
            props = {d.split(":")[0].strip() for d in decls}
            # Значение выбирает не автор, а программа — двумя способами.
            # Геометрия по данным: ширина полосы, координата точки.
            computed = props <= {"width", "height", "top", "left", "transform"} and any(
                "%" in d for d in decls)
            # Показ значения в витрине: образец окрашен ТЕМ САМЫМ токеном,
            # который он показывает. Класс здесь невозможен — их было бы
            # столько же, сколько токенов, и каждый повторял бы токен.
            if "${" in m.group(1) and src_file in SHOWCASE:
                computed = True
            if computed:
                data += 1
            else:
                theme += 1
                report("инлайн-стили",
                       f"{src_file.name}: инлайн-оформление «{m.group(1)[:60]}» — "
                       f"это тема, ей место в классе своего экрана")
    # То же правило для кода: он тоже пишет стиль, и до сих пор эта половина
    # не проверялась вовсе — «font-weight:700» внутри строки разметки в app.js
    # прожил всю разработку незамеченным.
    for m in re.finditer(r"\.style\.([a-zA-Z]+)\s*=", read(ROOT / "app.js")):
        prop = re.sub(r"([A-Z])", r"-\1", m.group(1)).lower()
        if prop in {"width", "height", "top", "left", "transform"}:
            data += 1
        else:
            theme += 1
            report("инлайн-стили",
                   f"app.js: element.style.{m.group(1)} — вид пишется из кода; "
                   f"состояние объявляют классом, а класс — правилом")

    print(f"  инлайн-оформления: {theme}   геометрии по данным: {data}")
    for _ in range(data):
        report("геометрия по данным",
               "index.html: значение считает программа — в классе ему места нет")


# --------------------------------------------------------------------------
# 6. Слой примитивов не должен протекать в компоненты
# --------------------------------------------------------------------------

def check_primitive_leak() -> None:
    """tokens.css §1: «Компоненты их НЕ используют».

    Правило было записано словами и потому не соблюдалось. Здесь оно
    становится исполняемым.

    Дополнительная причина, не видная из CSS: в USS нет записи
    `rgb(var(--x) / N%)`, поэтому КАЖДОЕ такое место превращается при переносе
    в рукописный литерал в ThemeTokens.uss. Число протечек — это прямая мера
    будущего расхождения макета и игры.
    """
    # --mat-* сюда НЕ входит: tokens.css §1.1b держит породы, страты, планету
    # и космос отдельной группой намеренно — это игровой контент, а не
    # интерфейс. Компонент, называющий базальт базальтом, ничего не нарушает.
    primitive = re.compile(r"var\(\s*(--(?:rgb|hex)-[a-z0-9-]+)", re.I)
    alpha = re.compile(r"rgb\(\s*var\(\s*(--[a-z0-9-]+)\s*\)\s*/\s*(\d+)%", re.I)

    # Сочетания, для которых семантический токен уже существует: подставить
    # его — механическая работа, и линтер обязан назвать замену.
    known: dict[tuple[str, str], str] = {}
    for name, prim, pct in re.findall(
        r"(--[a-z0-9-]+)\s*:\s*rgb\(\s*var\(\s*(--[a-z0-9-]+)\s*\)\s*/\s*(\d+)%",
        read(TOKENS), re.I,
    ):
        known.setdefault((prim, pct), name)

    for f in ALL_FILES:
        if f in PRIMITIVE_EXEMPT:
            continue

        for i, line in enumerate(read(f).splitlines(), 1):
            if "lint-ignore" in line:
                continue

            chunk = " ".join(style_chunks(f, line))
            replacements = {(prim, pct): known[(prim, pct)]
                            for prim, pct in alpha.findall(chunk) if (prim, pct) in known}
            for m in primitive.finditer(chunk):
                hint = ""
                for (prim, pct), token in replacements.items():
                    if prim == m.group(1):
                        hint = f" — есть {token}"
                        break

                report(
                    "протечка примитивов",
                    f"{f.name}:{i} примитив {m.group(1)} вне tokens.css{hint}",
                )


# --------------------------------------------------------------------------
# 7. Значения, для которых уже есть шкала
# --------------------------------------------------------------------------


def responsive_tokens() -> set[str]:
    """Токены, переопределяемые в медиазапросах: их значение зависит от тира.

    Читается из tokens.css, а не перечисляется списком: список разошёлся бы
    с файлом при первой же правке шкалы."""
    src = read(TOKENS)
    out: set[str] = set()
    for blk in re.finditer(r"@media \([^)]*width[^)]*\)\s*\{\s*:root\s*\{(.*?)\}\s*\}", src, re.S):
        out |= set(re.findall(r"(--[\w-]+)\s*:", blk.group(1)))
    return out


RESPONSIVE = responsive_tokens()


def check_scaled_values() -> None:
    """Обобщение «сырого цвета» до «сырого значения».

    Множество допустимых литералов равно множеству ступеней шкалы, и шкала
    читается из tokens.css. Литерал, совпавший со ступенью, — «токен есть, но
    не использован»; литерал вне шкалы — значение вне системы.

    ВАЖНОЕ РАЗЛИЧИЕ, которого здесь сначала не было. Одиннадцать токенов
    переопределяются в медиазапросах (--size-md/lg/xl/2xl/3xl,
    --space-10..14, --planet-disc): их значение зависит от тира экрана.
    Литерал — не зависит. Значит подстановка ТАКОГО токена меняет поведение
    на compact и wide, и советовать её как «токен есть, но не использован»
    — врать: это не переименование, а решение впустить компонент в тир-систему.

    Измерено: из 197 совпадений 177 нейтральны на всех тирах (проверено
    отпечатком вычисленных стилей — 0 расхождений на 13 045 элементах),
    а 20 затрагивают отзывчивые токены и дают 77 изменений значения на
    краевых тирах. Первые заменены, вторые остались долгом с пометкой.
    """
    for label, prefix, pattern in [(a, b, c) for a, b, c, _ in SCALED]:
        steps = scale_steps(prefix)
        allowed = [d for a, _, _, d in SCALED if a == label][0]
        rx = re.compile(pattern, re.I)
        for f in CSS_FILES:
            if f == TOKENS:
                continue

            for i, (line, raw) in enumerate(zip(css_code(f).splitlines(), read(f).splitlines()), 1):
                if "lint-ignore" in raw:
                    continue

                for m in rx.finditer(line):
                    value = normalize(m.group(1))
                    if value in allowed:
                        continue

                    if value in steps:
                        token = steps[value]
                        if token in RESPONSIVE:
                            report("значение мимо шкалы",
                                   f"{f.name}:{i} {label} {value} — есть {token}, "
                                   f"НО он меняется по тиру: подстановка изменит вид "
                                   f"на compact/wide, это решение, а не уборка")
                        else:
                            report("значение мимо шкалы",
                                   f"{f.name}:{i} {label} {value} — есть {token}")
                    else:
                        report("значение мимо шкалы",
                               f"{f.name}:{i} {label} {value} — вне шкалы {prefix}*")


def check_z_index() -> None:
    """z-index в USS не существует вообще: порядок задаётся порядком детей в
    UXML. Голое число переносить некому — оно не сообщает замысла, поэтому
    обязано ссылаться на одну из двух шкал:

      --layer-*  этажи интерфейса (сцена, контент, шапка, модалка);
      --order-*  порядок частей внутри одной сцены.

    Смешивать их нельзя: «планета выше шапки» бессмысленно, планета и шапка
    не соседи. Разделение и есть то, что переносится в иерархию UXML.
    """
    allowed = ("--layer-", "--order-")
    for f in CSS_FILES:
        if f == TOKENS:
            continue

        for i, line in enumerate(read(f).splitlines(), 1):
            if "lint-ignore" in line:
                continue

            for m in re.finditer(r"z-index:\s*([^;}]+)", line):
                value = m.group(1).strip()
                token = re.fullmatch(r"var\(\s*(--[a-z0-9-]+)\s*\)", value)
                if token and token.group(1).startswith(allowed):
                    continue

                report("z-index мимо шкал", f"{f.name}:{i} z-index: {value}")



# --------------------------------------------------------------------------
# 9. Каждый видимый текст — либо ключ, либо явно непереводимое
# --------------------------------------------------------------------------

def check_untranslated() -> None:
    """Строка без ключа не может ни попасть в перевод, ни быть замеченной.

    Проверка держит два разных долга, и их нельзя смешивать:

      «текст без ключа»  — узел не объявил НИЧЕГО: ни data-i18n, ни
                           translate="no". Это дыра в системе, режим enforced:
                           новый текст обязан приезжать с решением.
      «ключа нет в игре» — ключ есть, но живёт в i18n/mirror.ru.json, потому
                           что словарь игры его пока не знает. Это не дефект
                           макета, а очередь на перенос; режим долга, растёт
                           только если макет обгоняет игру.

    Дев-панель исключена целиком: инструмент разработки не переводится.
    """
    import html.parser as _hp
    import json as _json

    src = read(ROOT / "index.html")
    dev = re.search(r'<div class="dev-drawer">.*?\n  </div>', src, re.S)
    body = src.replace(dev.group(0), "") if dev else src

    class W(_hp.HTMLParser):
        SKIP = {"script", "style", "svg", "defs", "g", "path", "use", "title", "option"}

        def __init__(self) -> None:
            super().__init__(convert_charrefs=True)
            self.stack: list[dict] = []
            self.bare: list[tuple[int, str]] = []
            self.keys: list[str] = []

        def handle_starttag(self, tag, attrs):
            a = dict(attrs)
            self.stack.append({"tag": tag, "notr": a.get("translate") == "no",
                               "key": a.get("data-i18n")})
            if tag in ("br", "img", "input", "use", "hr", "meta", "link", "path"):
                self.stack.pop()

        def handle_endtag(self, tag):
            for i in range(len(self.stack) - 1, -1, -1):
                if self.stack[i]["tag"] == tag:
                    del self.stack[i:]
                    return

        def handle_data(self, data):
            text = re.sub(r"\s+", " ", data).strip()
            if not text or not re.search(r"[A-Za-zА-Яа-яЁё]", text):
                return
            if any(f["tag"] in self.SKIP for f in self.stack):
                return
            if any(f["notr"] for f in self.stack):
                return
            # Ключ владеет текстом на любой глубине, а не только у прямого
            # родителя: <h1 data-i18n="X">Начало<span>хвост</span></h1> — это
            # ОДНА строка X с переносом, и обе её части принадлежат X.
            key = next((f["key"] for f in reversed(self.stack) if f["key"]), None)
            if key:
                self.keys.append(key)
            else:
                self.bare.append((self.getpos()[0], text))

    w = W()
    w.feed(body)

    game = set(_json.loads(read(GAME_DICTS / "ru.json")))
    only_mirror = sorted({k for k in w.keys if k not in game})

    print(f"  текстовых узлов с ключом: {len(w.keys)}  без ключа: {len(w.bare)}")
    print(f"  ключей, которых нет в словаре игры: {len(only_mirror)}")

    # Текст, который показывает JS, — такой же видимый текст. Проверка,
    # смотрящая только в разметку, оставляет дыру ровно там, где её труднее
    # всего заметить: строка в коде не выглядит как интерфейс. Поймано на
    # собственных тостах — заменяя alert(), я вписал 8 русских строк в app.js
    # через час после того, как закрыл ту же дыру в index.html.
    js = read(ROOT / "app.js")
    for n, line in enumerate(js.split("\n"), 1):
        stripped = line.lstrip()
        if stripped.startswith(("*", "//", "/*")):
            continue
        for m in re.finditer(r"""showToast\(\s*['"`]([^'"`]*[А-Яа-яЁё][^'"`]*)""", line):
            w.bare.append((f"app.js:{n}", m.group(1)))

    for line, text in w.bare:
        where = line if isinstance(line, str) else f"index.html:{line}"
        report("текст без ключа", f"{where} «{text[:48]}» — нужен ключ локализации")
    for key in only_mirror:
        report("ключа нет в игре", f"{key} — живёт только в i18n/mirror.ru.json")



# --------------------------------------------------------------------------
# 10. Площадь расхождения с USS: что при порте придётся решать руками
# --------------------------------------------------------------------------

# Свойство -> чем оно является в UI Toolkit 6000.5. Проверено по
# UnityEngine.UIElementsModule.dll, а не по памяти.
# Проверено по таблице свойств в UnityEngine.UIElementsModule.dll 6000.5
# (строки UTF-16 из метаданных сборки), а не по памяти и не по документации.
# Две прежние записи оказались НЕВЕРНЫ и стоили системе лишнего страха:
# «filter отсутствует целиком» — на деле filter ЕСТЬ (blur, grayscale, sepia,
# invert, contrast, hue-rotate, шейдер Hidden/UIR/GaussianBlur), и он
# анимируется; text-shadow тоже есть и тоже анимируется.
#
# Потолок системы — не этот список. У USS три двери наружу:
#   -unity-material          свой шейдер на элемент (анимируемый)
#   Painter2D + generateVisualContent   векторные Fill/Stroke/CLIP
#   DrawVectorImage          готовая векторная графика
# Через них достижимо всё нижеперечисленное; вопрос не «можно ли», а «сколько
# это стоит». Поэтому строка описи называет ЦЕНУ, а не запрет.
UNPORTABLE = {
    "clip-path":         "нет как свойства. Цена: Painter2D.Clip в generateVisualContent",
    "-webkit-line-clamp": "нет. Цена: max-height + text-overflow: ellipsis",
    "line-clamp":        "нет. Цена: max-height + text-overflow: ellipsis",
    "overflow-wrap":     "нет (нет и word-break). Цена: -unity-text-auto-size "
                         "либо разрыв длинных значений в самих данных",
    "mix-blend-mode":    "нет. Цена: свой шейдер через -unity-material",
    "radial-gradient":   "нет (linear-gradient ЕСТЬ). Цена: текстура 9-slice, "
                         "Painter2D или шейдер",
    "conic-gradient":    "нет. Цена: текстура или шейдер",

    "gap":               "нет (ни gap, ни column-gap/row-gap). Цена: margin на детях",
    # box-shadow, drop-shadow и backdrop-filter больше не потери. С Unity
    # 6000.6 у UI Toolkit есть filter и backdrop-filter, а в наборе функций —
    # drop-shadow и blur (FilterFunctionType: Blur, Contrast, Custom,
    # DropShadow, Grayscale, HueRotate, Invert, Opacity, Sepia, Tint).
    # box-shadow как свойства по-прежнему нет, но тень пишется фильтром:
    # box-shadow: X Y R C  ->  filter: drop-shadow(X Y R/2 C), потому что
    # третий параметр Unity — сигма гауссианы, а CSS задаёт радиус, вдвое
    # больший. У blur() параметр и в CSS сигма, поэтому там число то же.
    # Ни spread, ни inset макет не использует ни разу — переносится всё.
    "cubic-bezier":      "нет. Цена: одна из 22 именованных плавностей "
                         "(ease-out-circ подобран, отклонение 0.129)",
}


def check_uss_debt() -> None:
    """Не нарушение, а СЧЁТ. Каждое такое место при порте станет ручным
    решением в .uss — то есть точкой, где макет и игра разойдутся первыми.

    План требовал вынести всё это в отдельный css/effects.css. Сделано
    наполовину и осознанно: @keyframes вынесены (самостоятельные блоки, в USS
    отсутствуют вовсе), а объявления вроде backdrop-filter оставлены в своих
    правилах — они живут ВНУТРИ правил, несущих и переносимые свойства, и
    переезд разорвал бы правило между файлами и договор «порядок @import =
    порядок каскада». Цель плана была «видно целиком»: она достигается этим
    отчётом точнее, чем переездом, — и держится потолком в BASELINE.

    Считать надо КОД, а не комментарий: опись потерь, записанная словами в
    шапке css/effects.css, сама попала в счёт и дала +7. Тот же промах, что
    у проверки шкал и у проверки эмодзи; здесь он исправлен тем же способом.
    """
    hits: list[tuple[str, int, str, str]] = []
    for path in CSS_FILES:
        if "styleguide" in path.name:
            continue
        for n, line in enumerate(css_code(path).split("\n"), 1):
            for prop, why in UNPORTABLE.items():
                if prop in line:
                    hits.append((path.name, n, prop, why))
                    break

    by_prop: dict[str, int] = {}
    for _, _, prop, _ in hits:
        by_prop[prop] = by_prop.get(prop, 0) + 1
    print(f"  площадь расхождения с USS: {len(hits)} объявлений")
    for prop in sorted(by_prop, key=lambda k: -by_prop[k]):
        print(f"    {prop:<20} {by_prop[prop]:>3}  — {UNPORTABLE[prop]}")

    for name, n, prop, _ in hits:
        report("долг переноса в USS", f"{name}:{n} {prop}")



# --------------------------------------------------------------------------
# 11. Сейф-зона поверхности берётся из лестницы, а не набирается на глаз
# --------------------------------------------------------------------------

# Поверхности, у которых отступ от края — это сейф-зона, а не внутренний
# ритм. Определяются по имени: это те, у кого есть СВОЙ край (рамка, заливка,
# край экрана), от которого содержимое обязано держать дистанцию.
SURFACES = re.compile(
    r"^\.(?:modal-card|modal-card-header|modal-card-body|modal-card-footer"
    r"|auth-card|fa-header|fa-footer|fdn-box)\b")

SAFE = ("--safe-screen", "--safe-panel", "--safe-box", "--safe-tight")


def check_safe_zone() -> None:
    """Отступ от края поверхности до содержимого назван, а не набран.

    Был не объявлен — и каждая поверхность придумала свой. Замер по живому
    интерфейсу дал шесть значений: 48 у хрома, 30 у карточки входа (вне шкалы
    --space-* вовсе), 24 И 28 у одного класса .modal-card-body, 12 у
    инспектора, 6/10/12 у .fdn-box. Проверить или объяснить нельзя было ни
    одно.

    Лестница выводится делением пополам по вложенности (48/24/12/6) — то же
    правило, по которому устроена рампа высот: чем глубже поверхность, тем
    меньше её вес и тем меньше воздуха. Отношение 2:1 читается глазом как
    смена уровня, 4:3 — нет.
    """
    for f in CSS_FILES:
        if f == TOKENS or "styleguide" in f.name:
            continue
        selector = ""
        for i, line in enumerate(read(f).splitlines(), 1):
            if "lint-ignore" in line:
                continue
            head = line.strip()
            if head.endswith("{"):
                selector = head[:-1].strip()
                continue
            if not SURFACES.match(selector):
                continue
            m = re.match(r"padding(?:-left|-right)?\s*:\s*([^;]+);", head)
            if not m:
                continue
            horizontal = m.group(1).split()
            edge = horizontal[1] if len(horizontal) > 1 else horizontal[0]
            if any(tok in edge for tok in SAFE) or edge in ("0", "0px"):
                continue
            report("сейф-зона мимо лестницы",
                   f"{f.name}:{i} {selector} — край {edge}, нужен один из "
                   f"--safe-screen/panel/box/tight")



# --------------------------------------------------------------------------
# 12. Регистр — оформление, а не содержимое
# --------------------------------------------------------------------------

def check_baked_case() -> None:
    """Значение словаря, набранное ПРОПИСНЫМИ, — это типографика в содержимом.

    Переводчик не обязан знать, что подпись рисуется капсом, и знать не будет:
    hud.chat в словаре игры уже пришёл как «Чат» и встал строчными среди
    прописных. Кроме того капс ломается по языкам (турецкое i -> İ, греческие
    ударения), а text-transform язык знает.

    Долг, а не ошибка: исправление требует переписать значения в естественный
    регистр, то есть работы с текстом, а её делает человек.
    """
    import json as _json
    seen: set[str] = set()
    sources = [GAME_DICTS / "ru.json", GAME_DICTS / "en.json", ROOT / "i18n" / "mirror.ru.json"]
    for src in sources:
        if not src.exists():
            continue
        for key, value in _json.loads(read(src)).items():
            if key in seen or len(value) < 3:
                continue
            if value == value.upper() and re.search(r"[А-ЯA-Z]{3}", value):
                seen.add(key)
                report("регистр запечён в текст", f"{src.name}:{key} = {value[:44]!r}")
    print(f"  значений ПРОПИСНЫМИ в словарях: {len(seen)}")



# --------------------------------------------------------------------------
# 13. Цветные эмодзи вне набора
# --------------------------------------------------------------------------

# Геометрические глифы, оставленные НАМЕРЕННО: они моноширинные, наследуют
# currentColor и работают на терминальную эстетику. Список взят из
# комментария к спрайту в index.html, а не придуман заново.
# ⚠ и ⛃ — монохромные текстовые символы, наследуют currentColor; в набор
# входят на тех же основаниях, что ромб и звезда. Эмодзи-представление у ⚠
# включает вариационный селектор U+FE0F — он и запрещён, а не сам знак.
GEOMETRIC = set("◆★⛏⚑⌬●○◇▲▼■□·•→←↑↓↗↘⚙♨⬇➡✓×—–⚠⛃")

# Диапазоны цветных эмодзи: пиктограммы, транспорт, символы, флаги.
EMOJI = re.compile(
    "[\U0001F300-\U0001FAFF\U00002600-\U000027BF\U0001F1E6-\U0001F1FF\uFE0F]")


def check_emoji() -> None:
    """Правило было записано словами в index.html и потому не соблюдалось.

    «Заменяет цветные эмодзи, которые рендерились по-разному на разных
    платформах» — и при этом 🔋 спокойно жил в данных инвентаря в app.js.
    Здесь правило становится исполняемым: цветной эмодзи запрещён, а
    геометрический глиф из объявленного набора разрешён.
    """
    for path in (ROOT / "index.html", ROOT / "app.js", ROOT / "js" / "i18n.js"):
        if not path.exists():
            continue
        # Комментарий, объясняющий запрет, обязан называть запрещённое — иначе
        # правило запрещает собственную документацию. Проверка первого символа
        # строки для этого не годится: продолжение блочного /* … */ маркера не
        # несёт, и мой же комментарий про 🔋 остался «нарушением».
        in_block = False
        for n, line in enumerate(read(path).split("\n"), 1):
            head = line.lstrip()
            was_block = in_block
            if not in_block and ("/*" in line or "<!--" in line):
                in_block = not ("*/" in line.split("/*", 1)[-1]
                                or "-->" in line.split("<!--", 1)[-1])
                was_block = True
            elif in_block and ("*/" in line or "-->" in line):
                in_block = False
            if was_block or in_block or head.startswith("//"):
                continue
            for m in EMOJI.finditer(line):
                ch = m.group(0)
                if ch in GEOMETRIC:
                    continue
                report("цветной эмодзи",
                       f"{path.name}:{n} {ch!r} — используй спрайт (#i-*) "
                       f"или геометрический глиф")


def main() -> int:
    print("Контраст:")
    check_contrast()
    print("\nТокены:")
    check_tokens_resolve()
    print("\nРазметка:")
    check_inline_styles()

    check_no_raw_colors()
    check_forbidden_names()
    check_primitive_leak()
    check_scaled_values()
    check_z_index()
    check_untranslated()
    check_uss_debt()
    check_safe_zone()
    check_baked_case()
    check_emoji()

    show = [a for a in sys.argv[1:] if not a.startswith("-")]
    if "--show" in sys.argv:
        # Долг виден только когда растёт — а разбирать его надо, пока он молчит.
        for check in sorted(findings):
            if show and not any(s in check for s in show):
                continue
            print(f"\n  {check} — {len(findings[check])}:")
            for item in findings[check]:
                print(f"    · {item}")
        return 0

    print("\nПроверки:")
    regressions: list[str] = []
    slack: list[str] = []
    width = max(len(k) for k in BASELINE)
    for check in sorted(BASELINE):
        found = len(findings.get(check, []))
        limit = BASELINE[check]
        if found > limit:
            mark = f"ВЫРОСЛО (+{found - limit})"
            regressions.append(check)
        elif found < limit:
            mark = f"долг убыл, подтяните BASELINE до {found}"
            slack.append(check)
        elif limit == 0:
            mark = "enforced"
        elif check in ALLOWED:
            mark = "норма"
        else:
            mark = "долг"

        print(f"  {check:<{width}}  {found:>4} / {limit:<4}  {mark}")

    # Проверки, для которых потолок не заведён, — всегда ошибка: молча
    # проглатывать находку опаснее, чем потребовать её осознанно записать.
    for check in sorted(set(findings) - set(BASELINE)):
        print(f"  {check:<{width}}  {len(findings[check]):>4} /  —    НЕТ В BASELINE")
        regressions.append(check)

    if slack:
        print(f"\n  Долг уменьшился: {', '.join(slack)}.")
        print("  Подтяните числа в BASELINE, иначе потолок останется на старом месте и даст откатиться назад.")

    if not regressions:
        print("\nНовых нарушений нет.")
        return 0

    print(f"\nВыросло проверок: {len(regressions)}")
    for check in regressions:
        items = findings[check]
        print(f"\n  {check} — {len(items)}:")
        for item in items[:12]:
            print(f"    ✗ {item}")
        if len(items) > 12:
            print(f"    … и ещё {len(items) - 12}")

    return 1


if __name__ == "__main__":
    sys.exit(main())
