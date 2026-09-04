#!/usr/bin/env python3
"""
Вытаскивает тексты макета в словарь и проставляет data-i18n.

ПОЧЕМУ КЛЮЧИ НЕ ВЫДУМЫВАЮТСЯ. Имя ключа — ровно тот случай, где «можно
миллион вариантов и все подойдут». Поэтому имя не выбирается, а выводится
из места, где строка живёт: экран (машина состояний #appViewport[data-state]
уже назвала экраны) -> компонент (id или класс) -> слот. Где в словаре игры
такой текст уже есть, побеждает существующий ключ: словарь игры — источник
истины, макет к нему присоединяется, а не заводит второй.

Режимы:
  (без флагов)  отчёт: что нашлось, какие ключи получатся
  --apply       записать data-i18n в index.html и словари в i18n/
"""
import collections
import html.parser
import json
import pathlib
import re
import sys
import unicodedata

ROOT = pathlib.Path(__file__).resolve().parent.parent
GAME = ROOT.parent.parent
DICTS = GAME / "Assets" / "Resources" / "Localization"
OUT = ROOT / "i18n"

# Экран -> пространство имён словаря игры. Соответствия не придуманы: взяты
# из уже существующих ключей (gateway.auth.*, mainmenu.*, hud.*, pause.*).
SCREEN_NS = {
    "authView": "gateway",
    "onboardingView": "onboarding",
    "menuArea": "mainmenu",
    "descentView": "descent",
    "ingameView": "hud",
    "pauseView": "pause",
    "reconnectView": "network",
}
MODAL_NS = {
    "inventoryModal": "inventory",
    "programmatorModal": "programmator",
    "settingsModal": "settings",
    "chatModal": "chat",
    "serverBrowserModal": "server",
    "profileModal": "mainmenu",
    "chronicleModal": "mainmenu",
    "clanModal": "mainmenu",
    "traderModal": "mainmenu",
    "repairModal": "mainmenu",
}

SKIP_TAGS = {"script", "style", "svg", "defs", "g", "path", "use", "circle", "rect", "title"}

# Не текст, а обозначение: клавиша, код тира, множитель, единица, число.
# Проверяется формой, а не списком — список расходится, форма нет.
NOT_TEXT = re.compile(r"""^(
      [A-Z]                      # одиночная клавиша: E, I, K, L, P
    | [xX]\d+                    # множитель: x3, x12
    | T\d+ | CR | OK | ОР        # коды тира/статуса
    | v?\d+[\d.,:/%°\s+-]*       # числа, версии, координаты, проценты
    | [^\w\s]+                   # чистая пунктуация и глифы
)$""", re.X)


def slugify(s: str) -> str:
    """Латинская транслитерация в snake_case: ключи в словаре игры латиницей."""
    table = {
        "а": "a", "б": "b", "в": "v", "г": "g", "д": "d", "е": "e", "ё": "e",
        "ж": "zh", "з": "z", "и": "i", "й": "y", "к": "k", "л": "l", "м": "m",
        "н": "n", "о": "o", "п": "p", "р": "r", "с": "s", "т": "t", "у": "u",
        "ф": "f", "х": "h", "ц": "c", "ч": "ch", "ш": "sh", "щ": "sch",
        "ъ": "", "ы": "y", "ь": "", "э": "e", "ю": "yu", "я": "ya",
    }
    s = "".join(table.get(ch, ch) for ch in s.lower())
    s = unicodedata.normalize("NFKD", s).encode("ascii", "ignore").decode()
    s = re.sub(r"[^a-z0-9]+", "_", s).strip("_")
    return re.sub(r"_+", "_", s)


class Walker(html.parser.HTMLParser):
    """Собирает текстовые узлы вместе с их путём по дереву."""

    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.stack: list[dict] = []
        self.found: list[dict] = []
        self.pos_of_tag: list[int] = []

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        self.stack.append({
            "tag": tag,
            "id": a.get("id", ""),
            "cls": (a.get("class") or "").split(),
            "notr": a.get("translate") == "no",
            "keyed": "data-i18n" in a,
            "line": self.getpos()[0],
            "pos": self.getpos(),
        })
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
        if any(f["tag"] in SKIP_TAGS for f in self.stack):
            return
        if any(f["notr"] for f in self.stack):
            return
        if any("dev-drawer" in f["cls"] for f in self.stack):
            return
        self.found.append({
            "text": text,
            "path": list(self.stack),
            "line": self.getpos()[0],
            "keyed": bool(self.stack and self.stack[-1]["keyed"]),
        })


def namespace(path: list[dict]) -> str:
    for f in path:
        if f["id"] in MODAL_NS:
            return MODAL_NS[f["id"]]
    for f in path:
        if f["id"] in SCREEN_NS:
            return SCREEN_NS[f["id"]]
    for f in path:
        if "modal-overlay" in f["cls"] and f["id"]:
            return MODAL_NS.get(f["id"], "modal")
    return "common"


def slot(path: list[dict], text: str) -> str:
    """Слот — ближайшее осмысленное имя из пути, а не из текста.

    Имя из текста ломается при первой же правке текста (а править текст —
    штатное дело); имя из структуры переживает её."""
    for f in reversed(path):
        if f["id"]:
            return slugify(re.sub(r"([a-z])([A-Z])", r"\1_\2", f["id"]))
        for c in f["cls"]:
            if c.startswith(("fdn-", "sg-")) or c in ("active", "done", "current"):
                continue
            return slugify(c)
    return slugify(text[:24])


def build() -> dict:
    ru = json.loads((DICTS / "ru.json").read_text(encoding="utf-8"))
    norm = lambda s: re.sub(r"[^а-яёa-z0-9]", "", s.lower())
    by_norm: dict[str, str] = {}
    for k, v in ru.items():
        by_norm.setdefault(norm(v), k)

    w = Walker()
    w.feed((ROOT / "index.html").read_text(encoding="utf-8"))

    reused, minted, skipped = [], [], []
    used: collections.Counter = collections.Counter()
    for node in w.found:
        t = node["text"]
        if NOT_TEXT.match(t):
            skipped.append(node)
            continue
        existing = by_norm.get(norm(t))
        if existing:
            node["key"] = existing
            node["origin"] = "игра"
            reused.append(node)
            continue
        base = f"{namespace(node['path'])}.{slot(node['path'], t)}"
        used[base] += 1
        node["key"] = base if used[base] == 1 else f"{base}_{used[base]}"
        node["origin"] = "новый"
        minted.append(node)
    return {"reused": reused, "minted": minted, "skipped": skipped, "ru": ru}


def main() -> None:
    r = build()
    print(f"текстовых узлов игры : {len(r['reused']) + len(r['minted'])}")
    print(f"  ключ есть в игре   : {len(r['reused'])} "
          f"(уник. {len({n['key'] for n in r['reused']})})")
    print(f"  ключ выведен       : {len(r['minted'])} "
          f"(уник. {len({n['key'] for n in r['minted']})})")
    print(f"  не текст (пропуск) : {len(r['skipped'])}")

    print("\nвыведенные ключи по пространствам:")
    ns = collections.Counter(n["key"].split(".")[0] for n in r["minted"])
    for k, v in ns.most_common():
        print(f"  {k:<14} {v}")

    print("\nобразцы выведенных ключей:")
    for n in r["minted"][:24]:
        print(f"  {n['key']:<44} {n['text'][:44]!r}")

    if "--apply" in sys.argv:
        apply_keys(r)


def line_offsets(text: str) -> list[int]:
    off, acc = [0], 0
    for ln in text.split("\n"):
        acc += len(ln) + 1
        off.append(acc)
    return off


def apply_keys(r: dict) -> None:
    """Вписывает data-i18n в разметку и пишет словарь макета.

    Атрибут ставится на элемент, которому текстовый узел принадлежит напрямую.
    Рантайм заменяет ПЕРВЫЙ прямой текстовый узел, а не textContent: элемент
    вроде <h1>Планета ждёт <span>...</span></h1> владеет и текстом, и детьми,
    и запись textContent снесла бы детей вместе с оформлением.
    """
    OUT.mkdir(exist_ok=True)
    src = (ROOT / "index.html").read_text(encoding="utf-8")
    off = line_offsets(src)

    # Один элемент — один ключ. Если элемент владеет двумя текстовыми узлами,
    # это разрезанное предложение: атрибут получает только первый, второй
    # остаётся в отчёте как долг (руками решать, склеивать или нет).
    seen: set[tuple[int, int]] = set()
    edits: list[tuple[int, str]] = []
    split_debt = []

    # Непереводимое помечается В РАЗМЕТКЕ, а не остаётся решением регулярки
    # внутри этого скрипта. Иначе «почему эта строка не переводится» имеет
    # ответ только в коде инструмента — то есть нигде, где его станут искать.
    n_notr = 0
    for node in r["skipped"]:
        frame = node["path"][-1]
        if frame["pos"] in seen:
            continue
        seen.add(frame["pos"])
        at = off[frame["pos"][0] - 1] + frame["pos"][1]
        edits.append((at + 1 + len(frame["tag"]), ' translate="no"'))
        n_notr += 1

    for node in r["reused"] + r["minted"]:
        if node.get("keyed"):
            continue
        frame = node["path"][-1]
        pos = frame["pos"]
        if pos in seen:
            split_debt.append(node)
            continue
        seen.add(pos)
        at = off[pos[0] - 1] + pos[1]
        assert src[at] == "<", src[at:at + 20]
        insert_at = at + 1 + len(frame["tag"])
        edits.append((insert_at, f' data-i18n="{node["key"]}"'))

    for at, text in sorted(edits, reverse=True):
        src = src[:at] + text + src[at:]
    (ROOT / "index.html").write_text(src, encoding="utf-8")

    mirror = {n["key"]: n["text"] for n in r["minted"]}
    (OUT / "mirror.ru.json").write_text(
        json.dumps(mirror, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    print(f"\n-> index.html: data-i18n {len(edits) - n_notr}, translate=\"no\" {n_notr}")
    print(f"-> i18n/mirror.ru.json: {len(mirror)} ключей, которых нет в словаре игры")
    print(f"   переиспользовано ключей игры: {len({n['key'] for n in r['reused']})}")
    if split_debt:
        print(f"\n   разрезанных предложений (второй узел без ключа): {len(split_debt)}")
        for n in split_debt:
            print(f"     index.html:{n['line']:<5} {n['text'][:52]!r}")


if __name__ == "__main__":
    main()
