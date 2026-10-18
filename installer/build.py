"""Builds the OpenSkyATC installer (step 17).

1. Publishes the app self-contained for win-x64 into artifacts/publish/app.
2. Assembles the two MSFS Community packages (toolbar panel, taxi arrows).
3. Compiles installer/OpenSkyATC.iss with Inno Setup 6 into artifacts/installer/OpenSkyATC-Setup-<version>.exe.

Run from anywhere: python installer/build.py [--version 0.1.0]
Needs the .NET 10 SDK, the MSFS 2024 SDK (to build the SimConnect project) and Inno Setup 6
(winget install JRSoftware.InnoSetup).
"""
import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def iscc() -> Path:
    for candidate in (
        Path(os.environ.get("LOCALAPPDATA", "")) / "Programs" / "Inno Setup 6" / "ISCC.exe",
        Path(r"C:\Program Files (x86)\Inno Setup 6\ISCC.exe"),
        Path(r"C:\Program Files\Inno Setup 6\ISCC.exe"),
    ):
        if candidate.exists():
            return candidate
    sys.exit("Inno Setup 6 not found: winget install JRSoftware.InnoSetup")


def run(*args: str) -> None:
    print("> " + " ".join(args), flush=True)
    subprocess.run(args, cwd=ROOT, check=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", default="0.1.0")
    version = parser.parse_args().version

    publish = ROOT / "artifacts" / "publish" / "app"
    if publish.exists():
        shutil.rmtree(publish)
    run("dotnet", "publish", "src/OpenSkyAtc.App", "-c", "Release", "-r", "win-x64", "--self-contained", "true",
        f"-p:Version={version}", "-o", str(publish))
    run(sys.executable, "toolbar/opensky-atc-toolbar/tools/assemble_package.py")
    run(sys.executable, "toolbar/opensky-atc-taxi-arrows/tools/assemble_package.py")
    run(str(iscc()), f"/DAppVersion={version}", "installer/OpenSkyATC.iss")
    print(f"Installer: {ROOT / 'artifacts' / 'installer' / f'OpenSkyATC-Setup-{version}.exe'}")


if __name__ == "__main__":
    main()
