"""Generates the application icons (original artwork) into assets/.

Usage: python scripts/generate-icons.py
Requires Pillow.
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "assets")
S = 1024


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def base_tile():
    tile = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    grad = Image.new("RGBA", (S, S))
    top, bottom = (124, 92, 255, 255), (46, 140, 255, 255)
    px = grad.load()
    for y in range(S):
        for x in range(S):
            t = (x * 0.35 + y * 0.65) / S
            px[x, y] = lerp(top, bottom, t)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([40, 40, S - 40, S - 40], radius=220, fill=255)
    tile.paste(grad, (0, 0), mask)
    return tile


def draw_eye(img):
    d = ImageDraw.Draw(img)
    cx, cy = S // 2, S // 2
    w, h = 700, 420
    # Almond-shaped eye outline built from two arcs.
    pts_top, pts_bottom = [], []
    for i in range(101):
        t = i / 100
        x = cx - w / 2 + w * t
        yoff = math.sin(math.pi * t) * h / 2
        pts_top.append((x, cy - yoff))
        pts_bottom.append((x, cy + yoff))
    outline = pts_top + pts_bottom[::-1]
    d.polygon(outline, fill=(255, 255, 255, 255))
    # Iris: split blue / amber halves (blue-yellow axis stays distinguishable for red-green CVD).
    r = 165
    d.pieslice([cx - r, cy - r, cx + r, cy + r], 90, 270, fill=(37, 99, 235, 255))
    d.pieslice([cx - r, cy - r, cx + r, cy + r], 270, 90, fill=(245, 176, 30, 255))
    pr = 70
    d.ellipse([cx - pr, cy - pr, cx + pr, cy + pr], fill=(20, 20, 40, 255))
    hr = 26
    d.ellipse([cx + 30 - hr, cy - 60 - hr, cx + 30 + hr, cy - 60 + hr], fill=(255, 255, 255, 230))


def draw_reset(img):
    d = ImageDraw.Draw(img)
    cx, cy, r, wdt = S // 2, S // 2 + 20, 270, 90
    d.arc([cx - r, cy - r, cx + r, cy + r], start=-60, end=250, fill=(255, 255, 255, 255), width=wdt)
    # Arrow head at the end of the arc (angle -60 degrees).
    a = math.radians(-60)
    ex, ey = cx + r * math.cos(a), cy + r * math.sin(a)
    size = 150
    d.polygon([(ex + size * 0.9, ey - size * 0.15), (ex - size * 0.35, ey - size * 0.75), (ex - size * 0.2, ey + size * 0.55)], fill=(255, 255, 255, 255))


def save_ico(img, name):
    sizes = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)]
    img.save(os.path.join(ASSETS, name), sizes=sizes)


def main():
    os.makedirs(ASSETS, exist_ok=True)
    app = base_tile()
    draw_eye(app)
    app = app.filter(ImageFilter.SMOOTH)
    save_ico(app, "akeno.ico")
    app.resize((256, 256), Image.LANCZOS).save(os.path.join(ASSETS, "akeno-256.png"))
    app.resize((512, 512), Image.LANCZOS).save(os.path.join(ASSETS, "akeno-512.png"))

    reset = base_tile()
    draw_reset(reset)
    save_ico(reset, "akeno-reset.ico")
    print("Icons written to", ASSETS)


if __name__ == "__main__":
    main()
