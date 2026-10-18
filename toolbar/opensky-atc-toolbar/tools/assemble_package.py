"""Assembles the opensky-atc-toolbar Community package (the in-sim OpenSkyATC panel, step 15).

The panel is HTML, JavaScript and CSS (copied as they are) plus one compiled file: the panel definition
PackageSources/InGamePanels/opensky-atc-toolbar.xml becomes InGamePanels/opensky-atc-toolbar.spb. Only the SDK's
package tool can compile it, and it does so through a running MSFS 2024, so the compiled .spb is committed and
rebuilt only when that XML changes (--build-spb).

Run: python assemble_package.py [--build-spb] [--install "<Community folder>"]
Writes Packages/opensky-atc-toolbar (git-ignored); --install also copies it into the Community folder.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
NAME = "opensky-atc-toolbar"
SOURCES = ROOT / "PackageSources"
SPB = ROOT / "InGamePanels" / f"{NAME}.spb"          # committed compiled definition
BUILT_SPB = ROOT / "Packages" / NAME / "InGamePanels" / f"{NAME}.spb"  # package tool output
OUT = ROOT / "Packages" / NAME

MANIFEST = {
    "dependencies": [],
    "content_type": "MISC",
    "title": "OpenSkyATC Toolbar Panel",
    "manufacturer": "",
    "creator": "OpenSkyATC",
    "package_version": "0.1.0",
    "minimum_game_version": "1.0.0",
    "builder": "Microsoft Flight Simulator 2024",
    "release_notes": {"neutral": {"LastUpdate": "", "OlderHistory": ""}},
}

EPOCH_AS_FILETIME = 116444736000000000  # 1970-01-01 in 100 ns ticks since 1601


def filetime(path: Path) -> int:
    return EPOCH_AS_FILETIME + int(path.stat().st_mtime * 10_000_000)


def sdk_tool() -> Path:
    for root in (os.environ.get("MSFS2024_SDK"), r"C:\MSFS 2024 SDK"):
        if root and (Path(root) / "Tools" / "bin" / "fspackagetool.exe").exists():
            return Path(root) / "Tools" / "bin" / "fspackagetool.exe"
    sys.exit("fspackagetool.exe not found: install the MSFS 2024 SDK.")


def build_spb() -> None:
    """Compiles the panel definition with the SDK package tool (it starts MSFS 2024 to do it)."""
    tool = sdk_tool()
    print(f"Running {tool.name}; it starts MSFS 2024 to compile the panel definition...")
    subprocess.run([str(tool), str(ROOT / f"{NAME}.xml"), "-nomirroring"], cwd=ROOT, check=True, timeout=1800)
    if not BUILT_SPB.exists():
        sys.exit(f"The package tool did not write {BUILT_SPB}.")
    SPB.parent.mkdir(exist_ok=True)
    shutil.copy2(BUILT_SPB, SPB)
    print(f"Compiled {SPB.relative_to(ROOT)} ({SPB.stat().st_size} bytes): commit it.")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--build-spb", action="store_true", help="compile the panel definition first (starts MSFS 2024)")
    parser.add_argument("--install", help="Community folder to copy the package into")
    args = parser.parse_args()

    if args.build_spb:
        build_spb()
    if not SPB.exists():
        sys.exit(f"{SPB.relative_to(ROOT)} is missing: run with --build-spb once (MSFS 2024 must be able to start).")

    if OUT.exists():
        shutil.rmtree(OUT)
    shutil.copytree(SOURCES / "html_ui", OUT / "html_ui")
    (OUT / "InGamePanels").mkdir(parents=True)
    shutil.copy2(SPB, OUT / "InGamePanels" / SPB.name)
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
