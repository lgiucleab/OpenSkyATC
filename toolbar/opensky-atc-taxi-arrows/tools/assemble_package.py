"""Assembles the opensky-atc-taxi-arrows Community package without the SDK package tool.

The arrow SimObject has no textures, so nothing needs compiling: the package is the source files plus
manifest.json and layout.json (every file's path, size and Windows file time). Use this when fspackagetool
cannot start the sim (it waits forever with the Microsoft Store version).

Run: python assemble_package.py [--install "<Community folder>"]
Writes Packages/opensky-atc-taxi-arrows (git-ignored); --install also copies it into the Community folder.
"""
import argparse
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
NAME = "opensky-atc-taxi-arrows"
SOURCES = ROOT / "PackageSources"
OUT = ROOT / "Packages" / NAME

MANIFEST = {
    "dependencies": [],
    "content_type": "MISC",
    "title": "OpenSkyATC Taxi Arrows",
    "manufacturer": "",
    "creator": "OpenSkyATC",
    "package_version": "0.1.0",
    "minimum_game_version": "1.0.0",
    "builder": "Microsoft Flight Simulator 2024",
    "package_order_hint": "CUSTOM_SIMOBJECTS",
    "release_notes": {"neutral": {"LastUpdate": "", "OlderHistory": ""}},
}

EPOCH_AS_FILETIME = 116444736000000000  # 1970-01-01 in 100 ns ticks since 1601


def filetime(path: Path) -> int:
    return EPOCH_AS_FILETIME + int(path.stat().st_mtime * 10_000_000)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--install", help="Community folder to copy the package into")
    args = parser.parse_args()

    if OUT.exists():
        shutil.rmtree(OUT)
    shutil.copytree(SOURCES, OUT)
    (OUT / "manifest.json").write_text(json.dumps(MANIFEST, indent=2), encoding="utf-8")

    files = sorted(p for p in OUT.rglob("*") if p.is_file() and p.name not in ("layout.json", "manifest.json"))
    layout = {"content": [{"path": p.relative_to(OUT).as_posix(), "size": p.stat().st_size, "date": filetime(p)} for p in files]}
    (OUT / "layout.json").write_text(json.dumps(layout, indent=2), encoding="utf-8")
    print(f"Assembled {OUT} ({len(files)} files)")

    if args.install:
        target = Path(args.install) / NAME
        if target.exists():
            shutil.rmtree(target)
        shutil.copytree(OUT, target)
        print(f"Installed to {target}")


if __name__ == "__main__":
    main()
