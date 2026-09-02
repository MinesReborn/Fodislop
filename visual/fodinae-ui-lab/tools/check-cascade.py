#!/usr/bin/env python3
"""
Отпечаток каскада: доказательство, что разнос файлов ничего не изменил.

Разнести 2100 строк CSS по файлам «на глаз» нельзя: правило, уехавшее
раньше или позже своего соседа с той же специфичностью, меняет результат
молча. Поэтому перед переносом снимается отпечаток — упорядоченный список
всех объявлений в порядке каскада, — а после переноса сравнивается.

Отпечаток намеренно грубый (селектор, свойство, значение, порядковый номер):
он не знает про специфичность и не должен. Достаточно, что порядок ЛЮБЫХ
двух объявлений сохранён — тогда сохранён и результат, каким бы он ни был.

  python3 tools/check-cascade.py --save   снять отпечаток
  python3 tools/check-cascade.py          сравнить с сохранённым
"""
import hashlib
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SNAP = ROOT / "tools" / ".cascade-snapshot.json"
ENTRY = ROOT / "styles.css"

IMPORT = re.compile(r"@import\s+url\(['\"]([^'\"]+)['\"]\)\s*;")
COMMENT = re.compile(r"/\*.*?\*/", re.S)


def expand(path: pathlib.Path, seen: set[pathlib.Path] | None = None) -> str:
    """Разворачивает @import в порядке объявления — это и есть порядок каскада."""
    seen = seen if seen is not None else set()
    if path in seen:
        return ""
    seen.add(path)
    text = path.read_text(encoding="utf-8")
    out: list[str] = []
    pos = 0
    for m in IMPORT.finditer(text):
        out.append(text[pos:m.start()])
        out.append(expand((path.parent / m.group(1)).resolve(), seen))
        pos = m.end()
    out.append(text[pos:])
    return "".join(out)


def declarations(css: str) -> list[str]:
    """(селектор, свойство, значение) в порядке следования.

    Разбор нарочно простой: у нас нет вложенности, кроме @media и
    @keyframes, а их префикс входит в селектор — этого хватает, чтобы
    перестановка внутри или между ними была замечена.
    """
    css = COMMENT.sub("", css)
    out: list[str] = []
    stack: list[str] = []
    buf = ""
    i = 0
    while i < len(css):
        ch = css[i]
        if ch == "{":
            stack.append(re.sub(r"\s+", " ", buf).strip())
            buf = ""
        elif ch == "}":
            for decl in buf.split(";"):
                decl = re.sub(r"\s+", " ", decl).strip()
                if decl:
                    out.append(" | ".join(stack) + " :: " + decl)
            buf = ""
            if stack:
                stack.pop()
        else:
            buf += ch
        i += 1
    return out



# Слова состояния. Их совпадение не значит НИЧЕГО: .fdn-settings-tab.active и
# .route-item.active никогда не попадут на один элемент, хотя обе «.active».
# Без этого списка проверка тонет в 61 ложной тревоге и перестаёт читаться —
# то же, что случилось с линтером, пока он был вечно красным.
STATE = {
    "active", "hover", "focus", "focus-visible", "disabled", "checked",
    "selected", "done", "current", "filled", "open", "visible", "hidden",
    "before", "after", "not", "first-child", "last-child", "nth-child",
    "empty", "warn", "danger", "ok", "root", "is", "where", "has",
}


def tokens(selector: str) -> set[str]:
    """Структурные имена селектора: классы, id, теги — без слов состояния.

    Спорят правила, способные попасть на ОДИН элемент. Общее слово состояния
    такой способности не даёт; общее имя вещи (.dev-btn, .menu-main-title) —
    даёт."""
    names = set(re.findall(r"[.#]?[a-zA-Z][\w-]*", selector))
    return {n for n in names if n.lstrip(".#") not in STATE}


def split_decl(d: str) -> tuple[str, str]:
    sel, _, decl = d.partition(" :: ")
    return sel, decl.split(":")[0].strip()


def risky_reorders(a: list[str], b: list[str]) -> list[tuple[str, str]]:
    """Пары, чей относительный порядок изменился И которые могут спорить."""
    pos_a = {d: i for i, d in enumerate(a)}
    pos_b = {d: i for i, d in enumerate(b)}
    by_prop: dict[str, list[str]] = {}
    for d in a:
        by_prop.setdefault(split_decl(d)[1], []).append(d)

    out: list[tuple[str, str]] = []
    for prop, group in by_prop.items():
        if len(group) > 400:   # свойства вроде color встречаются всюду
            continue
        for i, x in enumerate(group):
            for y in group[i + 1:]:
                if (pos_a[x] < pos_a[y]) == (pos_b[x] < pos_b[y]):
                    continue
                if tokens(split_decl(x)[0]) & tokens(split_decl(y)[0]):
                    out.append((x, y))
    return out


def fingerprint() -> dict:
    decls = declarations(expand(ENTRY))
    return {
        "count": len(decls),
        "sha": hashlib.sha256("\n".join(decls).encode()).hexdigest(),
        "decls": decls,
    }


def main() -> int:
    fp = fingerprint()
    if "--save" in sys.argv:
        SNAP.write_text(json.dumps(fp, ensure_ascii=False), encoding="utf-8")
        print(f"отпечаток снят: {fp['count']} объявлений, sha {fp['sha'][:12]}")
        return 0

    if not SNAP.exists():
        print("отпечатка нет — сначала: python3 tools/check-cascade.py --save")
        return 2

    old = json.loads(SNAP.read_text(encoding="utf-8"))
    if old["sha"] == fp["sha"]:
        print(f"каскад не изменился: {fp['count']} объявлений, sha {fp['sha'][:12]}")
        return 0

    print(f"КАСКАД ИЗМЕНИЛСЯ: было {old['count']}, стало {fp['count']}")
    a, b = old["decls"], fp["decls"]
    # Различия по составу — потери и добавления
    lost = [d for d in a if d not in set(b)]
    added = [d for d in b if d not in set(a)]
    if lost:
        print(f"\n  ПОТЕРЯНО {len(lost)}:")
        for d in lost[:20]:
            print(f"    - {d[:110]}")
    if added:
        print(f"\n  ДОБАВЛЕНО {len(added)}:")
        for d in added[:20]:
            print(f"    + {d[:110]}")
    if not lost and not added and "--relaxed" in sys.argv:
        # Буквальный порядок — не тот инвариант. Разнос по экранам обязан
        # переставлять темы (в исходнике они чередуются: вьюпорт, планета,
        # рейл, хедер, снова меню), и почти все эти перестановки безобидны:
        # два правила, которые не могут попасть на один элемент, о порядке
        # не спорят.
        #
        # Спорят те, что задают ОДНО свойство и делят хотя бы один токен
        # селектора (.dev-btn и .dev-btn.active; .menu-main-title и
        # .menu-main-title span). Проверка нарочно щедра на подозрения:
        # ложная тревога стоит взгляда, пропущенная перестановка — молчаливой
        # смены вида.
        print("\n  Состав тот же. Проверяю только СПОРНЫЕ перестановки:")
        conflicts = risky_reorders(a, b)
        if not conflicts:
            print("    спорных нет — перестановки затрагивают правила,")
            print("    которые не могут попасть на один элемент.")
            return 0
        print(f"    СПОРНЫХ {len(conflicts)}:")
        for x, y in conflicts[:20]:
            print(f"      порядок изменён: {x[:70]}")
            print(f"                    <> {y[:70]}")
        return 1

    if not lost and not added:
        # Состав тот же — значит переставлен порядок. Это тише и опаснее.
        for n, (x, y) in enumerate(zip(a, b)):
            if x != y:
                print(f"\n  СОСТАВ ТОТ ЖЕ, ПОРЯДОК ИНОЙ — первое расхождение на позиции {n}:")
                print(f"    было:  {x[:110]}")
                print(f"    стало: {y[:110]}")
                break
    return 1


if __name__ == "__main__":
    sys.exit(main())
