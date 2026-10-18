"""Writes the OpenSkyATC toolbar icon (original drawing, GPL-3.0): a radio mast sending two pairs of waves.

MSFS tints toolbar icons through their fill, so everything is a filled white shape (no strokes). MSFS 2024 reads
the icon from html_ui/icons/toolbar, MSFS 2020 from html_ui/Textures/Menu/toolbar; both copies are written.
Run: python generate_icon.py
"""
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent / "PackageSources" / "html_ui"
NAME = "ICON_TOOLBAR_OPENSKYATC.svg"
CX, CY = 32.0, 24.0


def arc_band(r_in: float, r_out: float, a0: float, a1: float, steps: int = 12) -> str:
    """A filled ring segment between angles a0..a1 (degrees, 0 = right, counterclockwise)."""
    def pt(r: float, a: float) -> str:
        rad = math.radians(a)
        return f"{CX + r * math.cos(rad):.2f} {CY - r * math.sin(rad):.2f}"
    outer = [pt(r_out, a0 + (a1 - a0) * i / steps) for i in range(steps + 1)]
    inner = [pt(r_in, a1 - (a1 - a0) * i / steps) for i in range(steps + 1)]
    return "M" + " L".join(outer + inner) + " Z"


def main() -> None:
    waves = []
    for r in (11.0, 19.0):
        waves.append(arc_band(r, r + 3.6, 140, 220))   # left
        waves.append(arc_band(r, r + 3.6, -40, 40))    # right
    mast = "M32 26 L42.5 60 L37.8 60 L32 39 L26.2 60 L21.5 60 Z"   # legs meeting under the antenna
    brace = "M26.6 48.5 L37.4 48.5 L38.3 51.5 L25.7 51.5 Z"
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">\n'
        "  <title>ICON_TOOLBAR_OPENSKYATC</title>\n"
        f'  <path fill="#ffffff" d="{mast} {brace}" />\n'
        f'  <circle fill="#ffffff" cx="{CX:g}" cy="{CY:g}" r="5" />\n'
        + "".join(f'  <path fill="#ffffff" d="{w}" />\n' for w in waves)
        + "</svg>\n"
    )
    for folder in ("icons/toolbar", "Textures/Menu/toolbar"):
        target = ROOT / folder / NAME
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(svg, encoding="utf-8")
        print(f"Wrote {target}")


if __name__ == "__main__":
    main()
