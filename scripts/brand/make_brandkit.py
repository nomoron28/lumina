"""Builds the Lumina brand-kit overview board (3 x 3) from the real brand assets: the aperture mark, the app icon, the palette,
the typeface, and screenshots of the app. Output: assets/brand/brandkit/lumina-brand-board.png

Needs Pillow and numpy. Uses the Garet typeface from src/Lumina/Assets/Fonts when it is present (it is not in the public
repository), otherwise Segoe UI.

Run: python scripts/brand/make_brandkit.py
"""
import os
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
A = lambda *p: os.path.join(ROOT, *p)
OUT = A("assets", "brand", "brandkit")
os.makedirs(OUT, exist_ok=True)

BG, SIDEBAR, SURFACE, RAISED, HOVER = (12, 13, 16), (16, 18, 22), (21, 23, 28), (29, 32, 39), (36, 40, 50)
TEXT, MUTED, FAINT = (236, 238, 242), (155, 161, 174), (124, 131, 146)
AMBER, AMBER_HI = (246, 162, 58), (255, 190, 92)
MINT = (92, 225, 198)
ACCENTS = [("Mint", "5CE1C6"), ("Sky", "6CB8FF"), ("Amber", "F5B84B"), ("Orange", "F28C1E"), ("Coral", "FF8A7A"),
           ("Pink", "FF7BAC"), ("Lime", "B8E65A"), ("Lilac", "A99BFF"), ("Neutral", "D9DEE8")]


def font(kind, size):
    heavy, book = A("src", "Lumina", "Assets", "Fonts", "Garet-Heavy.ttf"), A("src", "Lumina", "Assets", "Fonts", "Garet-Book.ttf")
    path = heavy if kind == "heavy" else book
    if not os.path.exists(path):
        path = r"C:\Windows\Fonts\seguibl.ttf" if kind == "heavy" else r"C:\Windows\Fonts\segoeui.ttf"
    return ImageFont.truetype(path, size)


def hexrgb(h):
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


W, H = 3200, 2400
M, G = 56, 32
PW = (W - 2 * M - 2 * G) // 3
PH = (H - 2 * M - 2 * G) // 3
canvas = Image.new("RGB", (W, H), (8, 9, 11))


def panel_origin(col, row):
    return M + col * (PW + G), M + row * (PH + G)


def new_panel(fill):
    return Image.new("RGB", (PW, PH), fill)


def rounded_mask(size, r):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, size[0] - 1, size[1] - 1), r, fill=255)
    return m


def place(col, row, img):
    x, y = panel_origin(col, row)
    canvas.paste(img, (x, y), rounded_mask(img.size, 30))


def label(d, text, xy, fill=FAINT, size=22, anchor="la"):
    d.text(xy, text, font=font("book", size), fill=fill, anchor=anchor)


def footer(d, name, page, fill=FAINT):
    label(d, name.upper(), (44, PH - 50), fill)
    label(d, f"{page:02d}", (PW - 44, PH - 50), fill, anchor="ra")


def glow(size, center, radius, color, strength):
    """A soft radial glow as an RGB layer to add onto a panel."""
    w, h = size
    yy, xx = np.mgrid[0:h, 0:w]
    d = np.sqrt((xx - center[0]) ** 2 + (yy - center[1]) ** 2) / radius
    a = np.clip(1 - d, 0, 1) ** 2.2 * strength
    arr = np.zeros((h, w, 3), dtype=np.float32)
    for i in range(3):
        arr[..., i] = a * color[i]
    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGB")


def add(base, layer):
    return Image.fromarray(np.clip(np.asarray(base, dtype=np.int16) + np.asarray(layer, dtype=np.int16), 0, 255).astype(np.uint8), "RGB")


def grain(img, amount=7):
    n = np.random.default_rng(3).normal(0, amount, (img.height, img.width, 1)).astype(np.int16)
    return Image.fromarray(np.clip(np.asarray(img, dtype=np.int16) + n, 0, 255).astype(np.uint8), "RGB")


mark_src = Image.open(A("assets", "brand", "logo", "aperture-mark-1024.png")).convert("RGBA")


def mark(size):
    return mark_src.resize((size, size), Image.LANCZOS)


# ---------- 1. logo cover ----------
p = new_panel(BG)
p = add(p, glow((PW, PH), (PW // 2, PH // 2 - 70), 520, AMBER, 0.20))
m1 = mark(360)
p.paste(m1, (PW // 2 - 180, PH // 2 - 290), m1)
d = ImageDraw.Draw(p)
d.text((PW // 2, PH // 2 + 150), "Lumina", font=font("heavy", 150), fill=TEXT, anchor="mm")
label(d, "BRAND GUIDELINES", (44, 44))
label(d, "2026", (PW - 44, 44), anchor="ra")
footer(d, "Lumina", 1)
place(0, 0, grain(p, 4))

# ---------- 2. construction ----------
p = new_panel(SIDEBAR)
d = ImageDraw.Draw(p)
cx, cy, s = PW // 2, PH // 2 - 30, 400
for k in range(-6, 7):                                   # faint grid
    d.line((cx + k * 70, 90, cx + k * 70, PH - 90), fill=(26, 29, 35), width=1)
    d.line((70, cy + k * 70, PW - 70, cy + k * 70), fill=(26, 29, 35), width=1)
mk = mark(s)
p.paste(mk, (cx - s // 2, cy - s // 2), mk)
r = s // 2
guide = (255, 255, 255)
ov = Image.new("RGBA", (PW, PH), (0, 0, 0, 0))
od = ImageDraw.Draw(ov)
for rad, al in ((r, 150), (int(r * 0.62), 90), (int(r * 0.30), 90), (int(r * 1.22), 55)):
    od.ellipse((cx - rad, cy - rad, cx + rad, cy + rad), outline=guide + (al,), width=2)
import math
for k in range(6):
    a = math.radians(k * 60 + 30)
    od.line((cx, cy, cx + math.cos(a) * r * 1.22, cy + math.sin(a) * r * 1.22), fill=guide + (70,), width=2)
od.line((cx - r * 1.35, cy, cx + r * 1.35, cy), fill=guide + (110,), width=2)
od.line((cx, cy - r * 1.35, cx, cy + r * 1.35), fill=guide + (110,), width=2)
od.ellipse((cx - 7, cy - 7, cx + 7, cy + 7), fill=AMBER + (255,))
p = Image.alpha_composite(p.convert("RGBA"), ov).convert("RGB")
d = ImageDraw.Draw(p)
label(d, "CONSTRUCTION", (44, 44))
label(d, "Six blades, one point of light", (PW // 2, PH - 96), MUTED, 30, "mm")
footer(d, "Aperture mark", 2)
place(1, 0, p)

# ---------- 3. digital application ----------
p = new_panel((14, 15, 19))
shot = Image.open(A("docs", "images", "settings.png")).convert("RGB")
sw = 860
shot = shot.resize((sw, round(shot.height * sw / shot.width)), Image.LANCZOS)
sh = shot.height
shadow = Image.new("RGBA", (PW, PH), (0, 0, 0, 0))
ImageDraw.Draw(shadow).rounded_rectangle((70, 120, 70 + sw, 120 + sh), 26, fill=(0, 0, 0, 200))
shadow = shadow.filter(ImageFilter.GaussianBlur(34))
p = Image.alpha_composite(p.convert("RGBA"), shadow).convert("RGB")
p.paste(shot, (74, 116), rounded_mask(shot.size, 24))
d = ImageDraw.Draw(p)
label(d, "THE APP", (44, 44))
label(d, "Dark, quiet, accent where it matters", (44, PH - 108), MUTED, 28)
footer(d, "Digital application", 3)
place(2, 0, p)

# ---------- 4. brand essence ----------
p = new_panel(BG)
p = add(p, glow((PW, PH), (PW - 60, PH + 40), 780, AMBER, 0.16))
d = ImageDraw.Draw(p)
f = font("heavy", 128)
d.text((72, 250), "Your media,", font=f, fill=TEXT)
x_light = 72
d.text((72, 390), "in the ", font=f, fill=TEXT)
x_light = 72 + d.textlength("in the ", font=f)
d.text((x_light, 390), "light.", font=f, fill=AMBER_HI)
label(d, "BRAND ESSENCE", (44, 44))
label(d, "A library that gets out of the way of the picture.", (72, 590), MUTED, 30)
footer(d, "Tagline", 4)
place(0, 1, grain(p, 4))

# ---------- 5. colour system ----------
p = new_panel(SIDEBAR)
d = ImageDraw.Draw(p)
label(d, "COLOUR", (44, 44))
base = [("Charcoal", BG, "0C0D10"), ("Surface", SURFACE, "15171C"), ("Raised", RAISED, "1D2027"), ("Text", TEXT, "ECEEF2"), ("Aperture amber", AMBER, "F6A23A")]
bw = (PW - 88 - 4 * 14) // 5
for i, (name, col, hx) in enumerate(base):
    x = 44 + i * (bw + 14)
    d.rounded_rectangle((x, 100, x + bw, 280), 18, fill=col, outline=(46, 50, 60) if sum(col) < 200 else None, width=1)
    label(d, name, (x + 4, 296), MUTED, 22)
    label(d, "#" + hx, (x + 4, 326), FAINT, 20)
label(d, "NINE ACCENTS · THE PLAYER AND SELECTION", (44, 396))
dw = 78
for i, (name, hx) in enumerate(ACCENTS):
    col, row = i % 9, 0
    x = 44 + i * ((PW - 88 - dw) // 8)
    y = 450
    if name == "Mint":
        d.ellipse((x - 9, y - 9, x + dw + 9, y + dw + 9), outline=hexrgb(hx), width=3)
    d.ellipse((x, y, x + dw, y + dw), fill=hexrgb(hx))
    label(d, name, (x + dw // 2, y + dw + 30), MUTED if name != "Mint" else TEXT, 19, "ma")
label(d, "Mint is the default. The logo stays amber whichever you choose.", (44, PH - 122), MUTED, 26)
footer(d, "Colour system", 5)
place(1, 1, p)

# ---------- 6. typography ----------
p = new_panel(BG)
d = ImageDraw.Draw(p)
label(d, "TYPE", (44, 44))
d.text((44, 150), "Aa", font=font("heavy", 300), fill=TEXT)
fb = font("book", 40)
for i, line in enumerate(["ABCDEFGHIJKLM", "NOPQRSTUVWXYZ", "abcdefghijklm", "nopqrstuvwxyz", "0123456789"]):
    d.text((540, 110 + i * 70), line, font=fb, fill=TEXT if i < 2 else MUTED)
d.text((60, 520), "Garet Heavy: titles, and the controller's bold", font=font("book", 28), fill=MUTED)
d.text((60, 564), "Garet Book: everything you read", font=font("book", 28), fill=MUTED)
d.text((60, 608), "Fallback: Segoe UI", font=font("book", 28), fill=FAINT)
footer(d, "Typography", 6)
place(2, 1, p)

# ---------- 7. icons and applications ----------
p = new_panel(SURFACE)
d = ImageDraw.Draw(p)
label(d, "ICON", (44, 44))
big = Image.open(A("assets", "brand", "app-icon", "app-icon-A-glow-1024.png")).convert("RGBA").resize((360, 360), Image.LANCZOS)
p.paste(big, (70, 130), big)
x = 480
for sz in (96, 64, 48, 32, 24, 16):
    src = A("assets", "brand", "app-icon", f"app-icon-A-glow-{sz}.png")
    ic = Image.open(src).convert("RGBA") if os.path.exists(src) else Image.open(A("assets", "brand", "app-icon", "app-icon-A-glow-128.png")).convert("RGBA").resize((sz, sz), Image.LANCZOS)
    ic = ic.resize((sz, sz), Image.LANCZOS) if ic.size != (sz, sz) else ic
    p.paste(ic, (x, 370 - sz // 2), ic)
    label(d, str(sz), (x + sz // 2, 440), FAINT, 18, "ma")
    x += sz + 26
# tray glyphs on a light and a dark taskbar plate
d.rounded_rectangle((480, 170, 700, 250), 14, fill=(32, 32, 32))
d.rounded_rectangle((720, 170, 940, 250), 14, fill=(243, 243, 243))
for px_, tone in ((560, "dark"), (800, "light")):
    t = Image.open(A("assets", "brand", "tray-icon", f"tray-A-filled-{tone}-32.png")).convert("RGBA")
    p.paste(t, (px_, 186), t)
label(d, "Taskbar, dark and light", (480, 262), FAINT, 20)
label(d, "Every size is drawn natively", (480, 470), FAINT, 20)
d.rounded_rectangle((70, PH - 220, PW - 70, PH - 110), 22, fill=RAISED)
ic = Image.open(A("assets", "brand", "app-icon", "app-icon-A-glow-64.png")).convert("RGBA")
p.paste(ic, (96, PH - 197), ic)
d.text((190, PH - 190), "Lumina-0.1.0-setup.exe", font=font("heavy", 34), fill=TEXT)
d.text((190, PH - 148), "Per-user installer · 84 MB · no administrator rights", font=font("book", 24), fill=MUTED)
footer(d, "Icon and installer", 7)
place(0, 2, p)

# ---------- 8. image direction ----------
p = new_panel((6, 6, 8))
cone = Image.new("L", (PW, PH), 0)
cd = ImageDraw.Draw(cone)
cd.polygon([(-40, 120), (PW + 40, PH * 0.20), (PW + 40, PH * 0.85), (-40, 330)], fill=255)
cone = cone.filter(ImageFilter.GaussianBlur(90))
layer = Image.merge("RGB", [cone.point(lambda v, c=c: int(v * c / 255 * 0.85)) for c in AMBER])
p = add(p, layer)
p = add(p, glow((PW, PH), (80, 220), 360, AMBER_HI, 0.55))
d = ImageDraw.Draw(p)
shade = Image.new("L", (PW, PH), 255)
sd = ImageDraw.Draw(shade)
for k in range(0, 4):                                     # a few blade shadows crossing the beam
    sd.polygon([(PW * (0.10 + k * 0.27), PH), (PW * (0.10 + k * 0.27) + 70, PH), (PW * (0.46 + k * 0.27) + 70, 0), (PW * (0.46 + k * 0.27), 0)], fill=120)
shade = shade.filter(ImageFilter.GaussianBlur(26))
p = Image.composite(p, Image.new("RGB", (PW, PH), (0, 0, 0)), shade)
p = p.filter(ImageFilter.GaussianBlur(1))
p = grain(p, 11)
d = ImageDraw.Draw(p)
label(d, "IMAGE DIRECTION", (44, 44), (200, 170, 120))
d.text((60, PH - 214), "Light on dark.", font=font("heavy", 78), fill=TEXT)
label(d, "Warm light, deep shadow, room to breathe.", (62, PH - 120), (210, 200, 185), 27)
footer(d, "Atmosphere", 8, (160, 140, 110))
place(1, 2, p)

# ---------- 9. system detail ----------
p = new_panel(BG)
d = ImageDraw.Draw(p)
label(d, "COMPONENTS", (44, 44))
bar = Image.open(A("docs", "images", "player.png")).convert("RGB").crop((226, 836, 1374, 914))
bar = bar.resize((PW - 88, round(bar.height * (PW - 88) / bar.width)), Image.LANCZOS)
p.paste(bar, (44, 120), rounded_mask(bar.size, 18))
y = 120 + bar.height + 56
d.rounded_rectangle((44, y, 330, y + 76), 16, fill=MINT)
d.text((187, y + 38), "Resume 15:54", font=font("book", 30), fill=BG, anchor="mm")
d.rounded_rectangle((350, y, 590, y + 76), 16, fill=RAISED, outline=(46, 50, 60))
d.text((470, y + 38), "Open file", font=font("book", 30), fill=TEXT, anchor="mm")
d.rounded_rectangle((610, y, 690, y + 76), 16, fill=RAISED, outline=(46, 50, 60))
for k in range(3):
    d.ellipse((636 + k * 14, y + 34, 644 + k * 14, y + 42), fill=TEXT)
# toggle on, toggle off
d.rounded_rectangle((730, y + 14, 826, y + 62), 24, fill=MINT)
d.ellipse((780, y + 18, 822, y + 60), fill=BG)
d.rounded_rectangle((846, y + 14, 942, y + 62), 24, fill=(70, 74, 86))
d.ellipse((850, y + 18, 892, y + 60), fill=(160, 165, 178))
# progress + selection ring
y2 = y + 120
d.rounded_rectangle((44, y2, PW - 44, y2 + 10), 5, fill=(40, 44, 54))
d.rounded_rectangle((44, y2, 44 + int((PW - 88) * 0.37), y2 + 10), 5, fill=MINT)
d.rounded_rectangle((44, y2 + 50, 334, y2 + 224), 20, fill=RAISED, outline=MINT, width=5)
d.polygon([(334 - 74, y2 + 50), (334 - 18, y2 + 50), (334, y2 + 68), (334, y2 + 124)], fill=MINT)
label(d, "Selection ring and unwatched corner", (362, y2 + 120), MUTED, 24)
footer(d, "System detail", 9)
place(2, 2, p)

canvas.save(os.path.join(OUT, "lumina-brand-board.png"), optimize=True)
print("ok", canvas.size)
