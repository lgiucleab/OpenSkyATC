"""Builds the OpenSkyATC installer (step 17).

1. Publishes the app self-contained for win-x64 into artifacts/publish/app.
   and copies the SimConnect DLLs from the MSFS 2024 SDK next to it (shipped in the installer only, never committed).
2. Assembles the MSFS Community package (taxi arrows).
3. Gathers the bundled ATC voices (Kokoro and Piper VCTK, owner's choice) into artifacts/voices: from the developer's
   own voice folder when they are there, else downloaded once into artifacts/cache. Only the files the app uses go
   in, with one shared espeak-ng-data.
4. Compiles installer/OpenSkyATC.iss with Inno Setup 6 into artifacts/installer/OpenSkyATC-Setup-<version>.exe.

Run from anywhere: python installer/build.py [--version 1.0.0-alpha]
Needs the .NET 10 SDK, the MSFS 2024 SDK (to build the SimConnect project) and Inno Setup 6
(winget install JRSoftware.InnoSetup).
"""
import argparse
import os
import shutil
import subprocess
import sys
import tarfile
import urllib.request
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


def simconnect_dlls() -> list[Path]:
    """Both SimConnect DLLs from the MSFS 2024 SDK (MSFS2024_SDK, else the installer's default folder)."""
    for root in (os.environ.get("MSFS2024_SDK"), r"C:\MSFS 2024 SDK"):
        if not root:
            continue
        lib = Path(root) / "SimConnect SDK" / "lib"
        dlls = [lib / "managed" / "Microsoft.FlightSimulator.SimConnect.dll", lib / "SimConnect.dll"]
        if all(d.exists() for d in dlls):
            return dlls
    sys.exit("The MSFS 2024 SDK's SimConnect DLLs were not found (set MSFS2024_SDK).")


# The voices the installer carries (refactor phase 4): Kokoro, the default, and VCTK, the fast option. Other Piper
# voices download on first use if the user ticks them.
BUNDLED_VOICES = ["kokoro-multi-lang-v1_0", "vits-piper-en_GB-vctk-medium"]
VOICE_RELEASE = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/"
# What the app reads (VoiceSynthesizer) plus the licence and model card files.
VOICE_FILES = {"tokens.txt", "voices.bin", "lexicon-us-en.txt", "LICENSE", "README.md", "MODEL_CARD"}


def voice_source(archive: str) -> Path:
    """The unpacked voice: the developer's %LOCALAPPDATA%\\OpenSkyATC\\voices copy, else a cached download."""
    own = Path(os.environ.get("LOCALAPPDATA", "")) / "OpenSkyATC" / "voices" / archive
    if (own / "tokens.txt").exists():
        return own
    cache = ROOT / "artifacts" / "cache" / "voices"
    unpacked = cache / archive
    if not (unpacked / "tokens.txt").exists():
        cache.mkdir(parents=True, exist_ok=True)
        tarball = cache / f"{archive}.tar.bz2"
        if not tarball.exists():
            print(f"Downloading {archive}...", flush=True)
            urllib.request.urlretrieve(f"{VOICE_RELEASE}{archive}.tar.bz2", tarball)
        with tarfile.open(tarball, "r:bz2") as tar:
            tar.extractall(cache, filter="data")
    return unpacked


def bundle_voices() -> None:
    target = ROOT / "artifacts" / "voices"
    if target.exists():
        shutil.rmtree(target)
    target.mkdir(parents=True)
    for archive in BUNDLED_VOICES:
        source = voice_source(archive)
        dest = target / archive
        dest.mkdir()
        for f in source.iterdir():
            if f.is_file() and (f.suffix == ".onnx" or f.name in VOICE_FILES):
                shutil.copy2(f, dest / f.name)
        if not (target / "espeak-ng-data").exists():
            shutil.copytree(source / "espeak-ng-data", target / "espeak-ng-data")
        print(f"Bundled {archive}")
    size = sum(f.stat().st_size for f in target.rglob("*") if f.is_file())
    print(f"Voices: {size / 2**20:.0f} MB in {target}")


def run(*args: str) -> None:
    print("> " + " ".join(args), flush=True)
    subprocess.run(args, cwd=ROOT, check=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", default="1.0.0-alpha")
    version = parser.parse_args().version

    publish = ROOT / "artifacts" / "publish" / "app"
    if publish.exists():
        shutil.rmtree(publish)
    run("dotnet", "publish", "src/OpenSkyAtc.App", "-c", "Release", "-r", "win-x64", "--self-contained", "true",
        f"-p:Version={version}", "-o", str(publish))
    for dll in simconnect_dlls():
        shutil.copy2(dll, publish / dll.name)
        print(f"Copied {dll}")
    run(sys.executable, "toolbar/opensky-atc-taxi-arrows/tools/assemble_package.py")
    bundle_voices()
    run(str(iscc()), f"/DAppVersion={version}", "installer/OpenSkyATC.iss")
    print(f"Installer: {ROOT / 'artifacts' / 'installer' / f'OpenSkyATC-Setup-{version}.exe'}")


if __name__ == "__main__":
    main()
