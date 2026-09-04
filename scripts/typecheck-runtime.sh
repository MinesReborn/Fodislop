#!/usr/bin/env bash
# Компиляция рантайм-сборки настоящим csc.
#
# ЗАЧЕМ. Локальные хуки — статические проверки: анализаторы Roslyn читают
# файлы поштучно и не знают, существует ли вызываемый метод в соседнем типе.
# Вырезанный при рефакторинге вызов и пропущенный using они пропускают, и
# ошибка всплывает только когда Unity наконец пересоберёт проект.
#
# Скрипт берёт список файлов не из .csproj (он устаревает: Unity его
# перегенерирует, и на диске регулярно лежат файлы, которых там нет), а с
# диска, и собирает всё под Assets/Scripts, кроме редакторных и тестов.
#
# Это ПРОВЕРКА ТИПОВ. Она не говорит ничего о том, работает ли игра.
set -euo pipefail
cd "$(dirname "$0")/.."

UNITY_ROOT="${UNITY_ROOT:-$(ls -d /Applications/Unity/Hub/Editor/* 2>/dev/null | tail -1)}"
if [ ! -d "$UNITY_ROOT" ]; then
    echo "Не найден каталог Unity. Задайте UNITY_ROOT." >&2
    exit 1
fi

CSC="$(find "$UNITY_ROOT" -name csc.dll -path '*Roslyn*' 2>/dev/null | head -1)"
if [ -z "$CSC" ]; then
    echo "Не найден csc.dll в $UNITY_ROOT" >&2
    exit 1
fi

export UNITY_ROOT

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

python3 - "$WORK" <<'PY'
import glob, os, re, sys

work = sys.argv[1]

# Ссылки берём из сгенерированного проекта, но версию редактора
# подменяем на установленную: путь в .csproj прибит к той версии,
# в которой его последний раз генерировали.
refs = re.findall(r'<HintPath>([^<]+)</HintPath>', open("Fodinae.Runtime.csproj").read())
root = os.environ["UNITY_ROOT"].rstrip("/")
fixed = []
for ref in refs:
    ref = re.sub(r'/Applications/Unity/Hub/Editor/[^/]+', root, ref)
    if os.path.exists(ref):
        fixed.append(ref)

# Вендорный VContainer исключён. Unity собирает его отдельной сборкой со
# своими define-ами, а здесь всё сваливается в одну: его PlayerLoopHelper
# обращается к UnityEngine.Experimental.LowLevel, которого в Unity 6 уже
# нет, и давал бы две вечные "ошибки", на фоне которых не видно настоящих.
sources = sorted(
    path for path in glob.glob("Assets/Scripts/**/*.cs", recursive=True)
    if "/Editor/" not in path
    and "/Tests/" not in path
    and "/VContainer/" not in path
)

with open(os.path.join(work, "args.rsp"), "w") as out:
    for flag in ("-target:library", "-nostdlib+", "-noconfig", "-unsafe+",
                 "-langversion:latest", "-nullable:enable"):
        out.write(flag + "\n")
    out.write(f"-out:{os.path.join(work, 'typecheck.dll')}\n")
    for ref in fixed:
        out.write(f"-r:{ref}\n")
    for dll in sorted(glob.glob("Library/ScriptAssemblies/*.dll")):
        # Собственные сборки исключены: их берём из исходников, иначе
        # старый .dll подменит свежий код и спрячет расхождение.
        if os.path.basename(dll) in ("Fodinae.Runtime.dll", "Assembly-CSharp.dll"):
            continue
        out.write(f"-r:{os.path.abspath(dll)}\n")
    out.write("\n".join(sources) + "\n")

print(f"Файлов: {len(sources)}, ссылок: {len(fixed)}")
PY

# Статус конвейера здесь бесполезен: под pipefail его определяет сам csc,
# который при найденных ошибках выходит с ненулевым кодом — то есть
# «ошибки нашлись» и «ошибок нет» давали один и тот же результат, и
# проверка молча сообщала об успехе именно тогда, когда падала. Решает
# наличие строк в файле, а не код возврата.
dotnet "$CSC" "@$WORK/args.rsp" 2>&1 | sort -u > "$WORK/output.txt" || true
grep "error CS" "$WORK/output.txt" > "$WORK/errors.txt" || true

# Из предупреждений показываются только нулевые ссылки (CS86xx): это
# будущие NullReferenceException, и без этого их видит лишь тот, кто
# смотрит в консоль Unity. Остальное здесь — шум стенда: две с половиной
# тысячи CS0436 порождает он сам, складывая в одну сборку исходники и
# уже собранные DLL с теми же типами. Считать их значило бы приучить не
# читать эту строку вовсе.
grep -E "warning CS86[0-9]+" "$WORK/output.txt" > "$WORK/warnings.txt" || true
if [ -s "$WORK/warnings.txt" ]; then
    echo "Предупреждений о нулевых ссылках: $(wc -l < "$WORK/warnings.txt" | tr -d ' ') (см. --warnings)"
    if [ "${1:-}" = "--warnings" ]; then
        cat "$WORK/warnings.txt"
    fi
fi

if [ -s "$WORK/errors.txt" ]; then
    echo "ОШИБКИ КОМПИЛЯЦИИ: $(wc -l < "$WORK/errors.txt" | tr -d ' ')"
    cat "$WORK/errors.txt"
    exit 1
fi

echo "Проверка типов пройдена: ошибок компиляции нет."
