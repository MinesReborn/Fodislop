#!/usr/bin/env python3
"""Validate repository moves without changing files or controlling Unity."""

import argparse
import collections
import hashlib
import json
from pathlib import Path
import re
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[3])
    args = parser.parse_args()
    root = args.root.resolve()
    inventory = root / "tools/Kern.RepositoryTaxonomy/Inventory"
    errors = []
    counts = collections.Counter()

    def require(condition, message):
        if not condition:
            errors.append(message)

    def digest(path):
        return hashlib.sha256(path.read_bytes()).hexdigest()

    for old, current in json.loads((inventory / "moves.json").read_text()).items():
        require((root / current).is_file(), f"Missing move target: {old} -> {current}")
        counts["moves"] += 1

    unchanged_formats = {".png", ".gif", ".exr", ".jpg", ".jpeg", ".bytes", ".asset", ".mat", ".prefab", ".inputactions"}
    for name in ("scripts", "assets"):
        for entry in json.loads((inventory / f"{name}.json").read_text()):
            path = root / entry["path"]
            meta = Path(str(path) + ".meta")
            require(path.is_file() and meta.is_file(), f"Missing asset/metadata: {entry['path']}")
            if not path.is_file() or not meta.is_file():
                continue
            match = re.search(r"^guid: (\w+)", meta.read_text(), re.MULTILINE)
            require(match and match[1] == entry["guid"], f"Changed GUID: {entry['path']}")
            if name == "scripts":
                require(digest(meta) == entry["meta_sha256"], f"Changed script metadata: {entry['path']}")
            elif path.suffix.lower() in unchanged_formats:
                require(digest(path) == entry["sha256"], f"Changed authored asset bytes: {entry['path']}")
            counts[name] += 1

    guids = {}
    for meta in (root / "Assets").rglob("*.meta"):
        match = re.search(r"^guid: (\w+)", meta.read_text(), re.MULTILINE)
        if match:
            guid = match[1]
            require(guid not in guids, f"Duplicate GUID: {meta.relative_to(root)} and {guids.get(guid)}")
            guids[guid] = str(meta.relative_to(root))
    counts["guids"] = len(guids)

    catalog_path = root / "Assets/Settings/Build/Textures/TextureDeliveryCatalog.json"
    sources, published = set(), set()
    for entry in json.loads(catalog_path.read_text())["Entries"]:
        require(entry.get("Delivery") in {"Embedded", "DummyWorld"},
                f"Invalid texture delivery category: {entry.get('Source')} -> {entry.get('Delivery')}")
        for key, identities in (("Source", sources), ("Published", published)):
            value = entry[key]
            require(bool(value) and not value.startswith("/") and "\\" not in value and
                    all(part not in ("", ".", "..") for part in value.split("/")), f"Invalid {key}: {value}")
            require(value.casefold() not in identities, f"Duplicate {key}: {value}")
            identities.add(value.casefold())
        require((root / "Assets/Textures" / entry["Source"]).is_file(), f"Missing texture: {entry['Source']}")
    actual_sources = {str(p.relative_to(root / "Assets/Textures")).casefold()
                      for p in (root / "Assets/Textures").rglob("*")
                      if p.is_file() and p.suffix != ".meta" and not p.name.startswith(".")}
    require(sources == actual_sources, f"Texture catalog differs from authored files: {sources ^ actual_sources}")
    counts["published_textures"] = len(sources)

    for directory in ("Assets/Resources/UI", "Assets/UI"):
        for path in (root / directory).rglob("*"):
            if path.suffix not in (".uss", ".uxml", ".tss"):
                continue
            text = path.read_text()
            urls = re.findall(r"url\(\s*[\"']([^\"']+)[\"']\s*\)", text) + re.findall(r'\bsrc="([^"]+)"', text)
            for value in urls:
                value = value.split("?", 1)[0]
                if value.startswith("project://database/"):
                    target = root / value.removeprefix("project://database/")
                elif "://" in value:
                    continue
                else:
                    target = path.parent / value
                require(target.is_file(), f"Missing UI reference: {path.relative_to(root)} -> {value}")
                counts["ui_references"] += 1

    for directory in ("Assets/Shaders", "Assets/Resources/Shaders"):
        for path in (root / directory).rglob("*"):
            if path.suffix not in (".hlsl", ".shader", ".compute"):
                continue
            for value in re.findall(r'^\s*#include(?:_with_pragmas)?\s+"([^"]+)"', path.read_text(), re.MULTILINE):
                if value.startswith(("Packages/", "Internal/")):
                    continue
                target = root / value if value.startswith("Assets/") else path.parent / value
                require(target.is_file(), f"Missing shader include: {path.relative_to(root)} -> {value}")
                counts["shader_includes"] += 1

    print(json.dumps(dict(counts), ensure_ascii=False, indent=2))
    for error in errors:
        print(error, file=sys.stderr)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
