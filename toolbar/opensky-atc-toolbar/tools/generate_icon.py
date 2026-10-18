"""Writes the OpenSkyATC toolbar icon (original drawing, GPL-3.0): the app's control-tower logo in one colour.

The shapes follow the bold small-size tower of branding/tools/generate_branding.py (the owner's logo B), with the
cab windows cut out so it reads at toolbar size. MSFS tints toolbar icons through their fill, so everything is a
filled white shape (no strokes). MSFS 2024 reads the icon from html_ui/icons/toolbar, MSFS 2020 from
html_ui/Textures/Menu/toolbar; both copies are written.
Run: python generate_icon.py
"""
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent / "PackageSources" / "html_ui"
NAME = "ICON_TOOLBAR_OPENSKYATC.svg"


def arc_band(cx: float, cy: float, r_in: float, r_out: float, a0: float, a1: float, steps: int = 12) -> str:
    """A filled ring segment between angles a0..a1 (degrees, 0 = right, counterclockwise)."""
    def pt(r: float, a: float) -> str:
        rad = math.radians(a)
        return f"{cx + r * math.cos(rad):.2f} {cy - r * math.sin(rad):.2f}"
    outer = [pt(r_out, a0 + (a1 - a0) * i / steps) for i in range(steps + 1)]
    inner = [pt(r_in, a1 - (a1 - a0) * i / steps) for i in range(steps + 1)]
    return "M" + " L".join(outer + inner) + " Z"


def main() -> None:
    # Bold tower on the 64-unit grid (same coordinates as the logo's small-size version).
    shaft = "M27 33 L37 33 L39.5 55 L24.5 55 Z"
    base = "M17 54 L47 54 L47 58 L17 58 Z"
    # cab with the window band cut out (even-odd fill)
    cab = "M18 21 L46 21 L42 34 L22 34 Z M20.5 24 L43.5 24 L42 29.5 L22 29.5 Z"
    antenna = "M30.2 13 L33.8 13 L33.8 21 L30.2 21 Z"
    waves = [arc_band(32, 13, 5.5, 9.5, 20, 160), arc_band(32, 13, 11.5, 14.5, 30, 150)]
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">\n'
        "  <title>ICON_TOOLBAR_OPENSKYATC</title>\n"
        '  <g transform="translate(0 3)">\n'  # room for the outer wave at the top
        f'    <path fill="#ffffff" d="{shaft} {base} {antenna}" />\n'
        f'    <path fill="#ffffff" fill-rule="evenodd" d="{cab}" />\n'
        + "".join(f'    <path fill="#ffffff" d="{w}" />\n' for w in waves)
        + "  </g>\n</svg>\n"
    )
    for folder in ("icons/toolbar", "Textures/Menu/toolbar"):
        target = ROOT / folder / NAME
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(svg, encoding="utf-8")
        print(f"Wrote {target}")


if __name__ == "__main__":
    main()
