#!/usr/bin/env python3
"""Обновление mapColor по исходным текстурам клеток.

Запуск из любой папки:
    uv run --with Pillow python tools/Kern.CellPalette/Generation/update_map_colors.py
    uv run --with Pillow python tools/Kern.CellPalette/Generation/update_map_colors.py --dry-run

Каталог публикации связывает textureName с исходником в Assets/Textures.
Исходник может содержать варианты клетки и вертикальные ленты кадров:
усредняется вся его площадь, без необходимости угадывать размеры тайлов.
В GIF, APNG и WebP учитываются все кадры с одинаковым весом каждого пикселя.
APNG-постер, не входящий в анимацию, исключается. Полностью прозрачные пиксели
исключаются; RGB остальных учитывается без альфы. Результат — непрозрачный
#RRGGBB. Усредняются байты исходного RGB без преобразования цветового пространства.
Общий атлас разных клеток не читается: используется исходник конкретной клетки.
Декаль и процедурные эффекты в средний цвет основной текстуры не входят.
"""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import sys
import tempfile


def unique_object(pairs: list[tuple[str, object]]) -> dict:
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Повторный ключ JSON: {key}")
        result[key] = value
    return result


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_object)


def asset_path(root: Path, relative: str) -> Path:
    parts = relative.split("/")
    if not relative or "\\" in relative or any(p in ("", ".", "..") for p in parts):
        raise ValueError(f"Недопустимый путь текстуры: {relative!r}")
    path = (root / relative).resolve()
    if not path.is_relative_to(root.resolve()):
        raise ValueError(f"Текстура за пределами каталога: {relative!r}")
    return path


def texture_index(path: Path) -> dict[str, str]:
    index = {}
    sources = set()
    for entry in read_json(path)["Entries"]:
        source, published = entry["Source"], entry["Published"]
        if source in sources or published in index:
            raise ValueError(f"Повторная запись каталога: {entry}")
        sources.add(source)
        index[published] = source
    # Те же псевдонимы без расширения, что в PublishedTextureSourceIndex.
    priority = {".png": 0, ".jpg": 1, ".jpeg": 1, ".exr": 2}
    for published, source in sorted(
        list(index.items()), key=lambda item: (priority.get(Path(item[1]).suffix, 99), item[1])
    ):
        if Path(source).suffix in priority:
            index.setdefault(str(Path(published).with_suffix("")), source)
    return index


def average_color(path: Path) -> str | None:
    from PIL import Image

    totals = [0, 0, 0]
    count = 0
    with Image.open(path) as image:
        first_frame = 1 if image.info.get("default_image", False) else 0
        for frame in range(first_frame, getattr(image, "n_frames", 1)):
            image.seek(frame)
            # Pillow восстанавливает полный кадр с учётом disposal/blend.
            rgba = image.convert("RGBA")
            mask = rgba.getchannel("A").point(lambda alpha: 255 if alpha else 0)
            visible = mask.histogram()[255]
            if visible == 0:
                continue
            count += visible
            for channel in range(3):
                histogram = rgba.getchannel(channel).histogram(mask=mask)
                totals[channel] += sum(value * frequency for value, frequency in enumerate(histogram))
    if count == 0:
        return None
    # Округление к ближайшему целому, половина вверх.
    return "#" + "".join(f"{(total * 2 + count) // (2 * count):02X}" for total in totals)


def replace_colors(text: str, colors: dict[str, str]) -> str:
    # Меняем только строковые значения mapColor, сохраняя отступы и порядок ключей.
    pattern = re.compile(
        r'(?P<cell>"(?:[^"\\]|\\.)*")\s*:\s*\{(?P<body>[^{}]*)\}', re.DOTALL
    )
    replaced = set()

    def replace_cell(match: re.Match) -> str:
        name = json.loads(match["cell"])
        if name not in colors:
            return match.group()
        body = match["body"]
        new_body, count = re.subn(
            r'("mapColor"\s*:\s*)"(?:[^"\\]|\\.)*"',
            lambda field: field[1] + json.dumps(colors[name]),
            body,
        )
        if count != 1:
            raise ValueError(f"{name}: ожидалось ровно одно строковое поле mapColor")
        replaced.add(name)
        start = match.start("body") - match.start()
        return match.group()[:start] + new_body + match.group()[start + len(body):]

    result = pattern.sub(replace_cell, text)
    if replaced != colors.keys():
        raise ValueError("Не удалось однозначно найти клетки для обновления mapColor")
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[3], help="Корень проекта")
    parser.add_argument("--dry-run", action="store_true", help="Показать изменения без записи")
    parser.add_argument("--cell", action="append", help="Обновить только указанную клетку; можно повторять")
    args = parser.parse_args()
    root = args.root.resolve()
    cells_path = root / "Assets/Resources/World/Cells/Configuration/cells.json"
    # newline='' сохраняет исходный формат переноса строк.
    with cells_path.open(encoding="utf-8", newline="") as stream:
        original = stream.read()
    cells = json.loads(original, object_pairs_hook=unique_object)
    selected = set(args.cell) if args.cell else set(cells)
    if unknown := selected - cells.keys():
        raise ValueError(f"Неизвестные клетки: {', '.join(sorted(unknown))}")
    index = texture_index(root / "Assets/Settings/Build/Textures/TextureStagingCatalog.json")
    cache = {}
    changes = {}
    for name, cell in cells.items():
        if name not in selected:
            continue
        texture = cell["textureName"]
        if not texture:
            print(f"{name}: без текстуры, цвет сохранён")
            continue
        if texture not in index:
            raise ValueError(f"{name}: текстура {texture!r} отсутствует в каталоге публикации")
        source = asset_path(root / "Assets/Textures", index[texture])
        if source not in cache:
            cache[source] = average_color(source)
        color = cache[source]
        if color is None:
            print(f"{name}: полностью прозрачная текстура, цвет сохранён")
        elif color != cell["mapColor"]:
            changes[name] = color
            print(f"{name}: {cell['mapColor']} → {color}")
    updated = replace_colors(original, changes)
    expected = {name: dict(cell, mapColor=changes.get(name, cell["mapColor"])) for name, cell in cells.items()}
    if json.loads(updated) != expected:
        raise ValueError("Проверка JSON: изменились данные помимо mapColor")
    if changes and not args.dry_run:
        # Все картинки прочитаны до записи; ошибка не оставляет частичный результат.
        temporary = None
        try:
            with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", newline="", dir=cells_path.parent, delete=False) as stream:
                temporary = Path(stream.name)
                stream.write(updated)
            temporary.chmod(cells_path.stat().st_mode & 0o777)
            os.replace(temporary, cells_path)
        finally:
            if temporary is not None:
                temporary.unlink(missing_ok=True)
    print(f"{'Будет обновлено' if args.dry_run else 'Обновлено'} клеток: {len(changes)}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, KeyError, ImportError) as error:
        print(f"Ошибка: {error}", file=sys.stderr)
        raise SystemExit(1)
