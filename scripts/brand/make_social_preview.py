"""Builds the 1280 x 640 GitHub social preview image (Repository settings, Social preview) from the brand assets.
Text is set in Segoe UI so the image carries no Garet; output: assets/social/lumina-social-preview.png

Run: python scripts/brand/make_social_preview.py
"""
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
A = lambda *p: os.path.join(ROOT, *p)
W, H = 1280, 640
SS = 2                                    # draw at 2x and scale down for smooth edges
BG, TEXT, MUTED = (12, 13, 16), (236, 238, 242), (155, 161, 174)
AMBER, AMBER_HI, MINT = (246, 162, 58), (255, 190, 92), (92, 225, 198)


def font(name, size):
    return ImageFont.truetype(os.path.join(r"C:\Windows\Fonts", name), size * SS)


def glow(size, center, radius, color, strength):
    w, h = size
    yy, xx = np.mgrid[0:h, 0:w]
    d = np.sqrt((xx - center[0]) ** 2 + (yy - center[1]) ** 2) / radius
    a = np.clip(1 - d, 0, 1) ** 2.2 * strength
    arr = np.stack([a * c for c in color], axis=-1)
    return np.clip(arr, 0, 255).astype(np.int16)


w, h = W * SS, H * SS
base = np.zeros((h, w, 3), dtype=np.int16) + np.array(BG, dtype=np.int16)
base += glow((w, h), (int(w * 0.20), int(h * 0.42)), int(w * 0.46), AMBER, 0.20)
base += glow((w, h), (int(w * 0.98), int(h * 1.05)), int(w * 0.50), MINT, 0.07)
rng = np.random.default_rng(5)
base += rng.normal(0, 3, (h, w, 1)).astype(np.int16)
img = Image.fromarray(np.clip(base, 0, 255).astype(np.uint8), "RGB")

# the app, bleeding off the right and bottom edges
shot = Image.open(A("docs", "images", "settings.png")).convert("RGB")
sw = 860 * SS
shot = shot.resize((sw, round(shot.height * sw / shot.width)), Image.LANCZOS)
pos = (int(w * 0.50), int(h * 0.20))
mask = Image.new("L", shot.size, 0)
ImageDraw.Draw(mask).rounded_rectangle((0, 0, shot.width - 1, shot.height - 1), 22 * SS, fill=255)
shadow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
ImageDraw.Draw(shadow).rounded_rectangle((pos[0], pos[1] + 14 * SS, pos[0] + shot.width, pos[1] + 14 * SS + shot.height), 22 * SS, fill=(0, 0, 0, 230))
shadow = shadow.filter(ImageFilter.GaussianBlur(26 * SS))
img = Image.alpha_composite(img.convert("RGBA"), shadow).convert("RGB")
img.paste(shot, pos, mask)

# the logo, wordmark and line
mk = Image.open(A("assets", "brand", "logo", "aperture-mark-1024.png")).convert("RGBA")
ms = 132 * SS
mk = mk.resize((ms, ms), Image.LANCZOS)
x0, y0 = 64 * SS, 96 * SS
img.paste(mk, (x0, y0), mk)
d = ImageDraw.Draw(img)
d.text((x0, y0 + ms + 26 * SS), "Lumina", font=font("seguibl.ttf", 104), fill=TEXT)
d.text((x0 + 4 * SS, y0 + ms + 150 * SS), "Your media, in the light.", font=font("segoeui.ttf", 36), fill=AMBER_HI)
d.text((x0 + 4 * SS, y0 + ms + 214 * SS), "A free, open-source media library", font=font("segoeui.ttf", 26), fill=MUTED)
d.text((x0 + 4 * SS, y0 + ms + 248 * SS), "and video player for Windows.", font=font("segoeui.ttf", 26), fill=MUTED)

out = img.resize((W, H), Image.LANCZOS)
os.makedirs(A("assets", "social"), exist_ok=True)
out.save(A("assets", "social", "lumina-social-preview.png"), optimize=True)
print("ok", out.size, os.path.getsize(A("assets", "social", "lumina-social-preview.png")) // 1024, "KB")
