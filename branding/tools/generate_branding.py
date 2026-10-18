"""Generates the OpenSkyATC brand images (original artwork, GPL-3.0): a control tower with radio waves.

Writes into branding/:
  logo.png                         512 px app logo (rounded tile)
  OpenSkyATC.ico                   app and installer icon (256, 64, 48, 40, 32, 24, 20, 16 px; a bolder tower below 40 px)
  installer/wizard-large-*.png     the installer's side image (welcome and finish pages), 100-250 % DPI
  installer/wizard-small-*.png     the installer's corner image (other pages), 100-250 % DPI

Run: python branding/tools/generate_branding.py   (needs Pillow; it is only a developer tool)
The text on the side image is drawn with Segoe UI from Windows' font folder (rendered into the image, not shipped).
"""
import math
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
SS = 4  # supersampling

NAVY_TOP = (16, 24, 40)
NAVY_BOTTOM = (10, 58, 92)
WHITE = (255, 255, 255, 255)
AMBER = (255, 196, 61, 255)
GLASS = (125, 211, 252, 255)


def gradient(w, h, top, bottom):
    img = Image.new("RGBA", (w, h))
    d = ImageDraw.Draw(img)
    for y in range(h):
        t = y / max(1, h - 1)
        d.line([(0, y), (w, y)], fill=tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,))
    return img


def arc_band(d, cx, cy, r_in, r_out, a0, a1, fill, steps=48):
    pts = []
    for i in range(steps + 1):
        a = math.radians(a0 + (a1 - a0) * i / steps)
        pts.append((cx + r_out * math.cos(a), cy - r_out * math.sin(a)))
    for i in range(steps + 1):
        a = math.radians(a1 - (a1 - a0) * i / steps)
        pts.append((cx + r_in * math.cos(a), cy - r_in * math.sin(a)))
    d.polygon(pts, fill=fill)


def draw_tower(d, ox, oy, k, bold=False):
    """The tower on a 64-unit grid at (ox, oy), k pixels per unit. Bold: thicker parts, one wave, for small icons."""
    def p(x, y):
        return (ox + x * k, oy + y * k)
    if bold:
        d.polygon([p(27, 33), p(37, 33), p(39.5, 55), p(24.5, 55)], fill=WHITE)
        d.rectangle([*p(17, 54), *p(47, 58)], fill=WHITE)
        d.polygon([p(18, 21), p(46, 21), p(42, 34), p(22, 34)], fill=WHITE)
        d.polygon([p(20.5, 24), p(43.5, 24), p(42, 29.5), p(22, 29.5)], fill=GLASS)
        d.rectangle([*p(30.2, 13), *p(33.8, 21)], fill=WHITE)
        arc_band(d, *p(32, 13), 5.5 * k, 9.5 * k, 20, 160, AMBER)
        return
    d.polygon([p(28.5, 33), p(35.5, 33), p(37.5, 54), p(26.5, 54)], fill=WHITE)
    d.rectangle([*p(20, 54), *p(44, 57)], fill=WHITE)
    d.polygon([p(22, 23), p(42, 23), p(39, 33), p(25, 33)], fill=WHITE)
    d.polygon([p(23.4, 25), p(40.6, 25), p(39.4, 29.5), p(24.6, 29.5)], fill=GLASS)
    d.rectangle([*p(24, 20.5), *p(40, 23)], fill=WHITE)
    d.rectangle([*p(31.2, 14), *p(32.8, 20.5)], fill=WHITE)
    for r in (4.5, 9.0):
        arc_band(d, *p(32, 14), r * k, (r + 2.2) * k, 25, 155, AMBER)


def logo(size, bold=False):
    n = size * SS
    k = n / 64
    tile = gradient(n, n, NAVY_TOP, NAVY_BOTTOM)
    draw_tower(ImageDraw.Draw(tile), 0, 0, k, bold)
    mask = Image.new("L", (n, n), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, n - 1, n - 1], radius=int(14 * k), fill=255)
    out = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    out.paste(tile, (0, 0), mask)
    return out.resize((size, size), Image.LANCZOS)


def font(size, bold=False):
    for name in (("segoeuib.ttf" if bold else "segoeui.ttf"), "arial.ttf"):
        try:
            return ImageFont.truetype(str(Path("C:/Windows/Fonts") / name), size)
        except OSError:
            continue
    return ImageFont.load_default()


def wizard_large(scale):
    """Side image of the welcome and finish pages (164 x 314 at 100 %): night-sky gradient, tower, name."""
    w, h = round(164 * scale), round(314 * scale)
    W, H = w * SS, h * SS
    img = gradient(W, H, NAVY_TOP, NAVY_BOTTOM)
    d = ImageDraw.Draw(img)
    # faint stars and a runway glow at the bottom, for depth
    # (kept clear of the name, 0.13-0.28 of the height)
    for x, y, r in ((0.15, 0.06, 1.0), (0.78, 0.04, 1.2), (0.52, 0.10, 0.8), (0.9, 0.33, 1.0), (0.1, 0.38, 0.7), (0.84, 0.47, 0.9)):
        rr = r * scale * SS * 1.2
        d.ellipse([x * W - rr, y * H - rr, x * W + rr, y * H + rr], fill=(255, 255, 255, 150))
    # approach lights in perspective: widest nearest the viewer (bottom)
    for i in range(6):
        y = H - (i + 1) * 9 * scale * SS
        half = (42 - i * 7) * scale * SS
        d.line([(W * 0.5 - half, y), (W * 0.5 + half, y)], fill=(255, 196, 61, 110 - i * 14), width=max(1, int(1.2 * scale * SS)))
    k = W / 64 * 0.82
    draw_tower(d, (W - 64 * k) / 2, H * 0.30, k)
    f_big, f_small = font(int(19 * scale * SS), bold=True), font(int(10 * scale * SS))
    for text, f, y, col in (("OpenSkyATC", f_big, H * 0.155, WHITE), ("ATC for MSFS 2024", f_small, H * 0.235, (184, 214, 236, 255))):
        tw = d.textlength(text, font=f)
        d.text(((W - tw) / 2, y), text, font=f, fill=col)
    return img.resize((w, h), Image.LANCZOS)


def main():
    ROOT.mkdir(exist_ok=True)
    (ROOT / "installer").mkdir(exist_ok=True)
    logo(512).save(ROOT / "logo.png")

    sizes = [256, 64, 48, 40, 32, 24, 20, 16]
    frames = [logo(s, bold=s < 40) for s in sizes]
    frames[0].save(ROOT / "OpenSkyATC.ico", format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[1:])

    for pct in (100, 125, 150, 175, 200, 250):
        s = pct / 100
        wizard_large(s).convert("RGB").save(ROOT / "installer" / f"wizard-large-{pct}.png")
        logo(round(55 * s), bold=55 * s < 40).save(ROOT / "installer" / f"wizard-small-{pct}.png")
    print(f"Wrote {ROOT / 'logo.png'}, {ROOT / 'OpenSkyATC.ico'} and the installer images in {ROOT / 'installer'}")


if __name__ == "__main__":
    main()
