"""
Builds every Lumina brand asset from one geometry definition: SVG masters, PNG exports, ICO files, previews.

  python scripts/brand/make_brand.py

The aperture mark is six blades. Each blade is a wedge whose apex sits on a vertex of a small hexagon (the opening);
its two edges run along the extensions of that hexagon's sides, so the blades overlap like a pinwheel iris.
Nothing here is drawn by hand: change a number, run it again, and every file follows.
"""
import io
import math
import os
import struct
import sys

from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "assets", "brand")
FONT_HEAVY = os.path.join(ROOT, "src", "Lumina", "Assets", "Fonts", "Garet-Heavy.ttf")

# ---- colours (the permanent identity) ----
AMBER_HI = (0xFF, 0xBE, 0x5C)      # lit edge of the mark
AMBER = (0xF6, 0xA2, 0x3A)         # the flat brand amber
AMBER_LO = (0xEC, 0x86, 0x1C)      # shaded edge
CHARCOAL = (0x0C, 0x0D, 0x10)
CHARCOAL_2 = (0x15, 0x17, 0x1C)
TEXT_DARK_BG = (0xEC, 0xEE, 0xF2)
TEXT_LIGHT_BG = (0x14, 0x16, 0x1A)
MONO_WHITE = (0xF4, 0xF5, 0xF7)
MONO_BLACK = (0x14, 0x16, 0x1A)


def hexc(c):
    return "#%02X%02X%02X" % c


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


# ---- geometry ----
R_OUT = 100.0


def blades(r_open=38.0, gap=8.0, round_w=7.0, arc_steps=3):
    """Six blade polygons around (0,0). The outer radius, the gap and the rounding stroke are all inside R_OUT."""
    inset = round_w / 2.0
    radius = R_OUT - inset
    off = gap / 2.0 + inset
    verts = [(r_open * math.cos(math.radians(60 * k)), r_open * math.sin(math.radians(60 * k))) for k in range(6)]

    def tangent(k):                       # unit direction of hexagon edge k (vertex k to k+1)
        a, b = verts[k % 6], verts[(k + 1) % 6]
        dx, dy = b[0] - a[0], b[1] - a[1]
        n = math.hypot(dx, dy)
        return dx / n, dy / n

    def clip(poly, keep):                 # Sutherland-Hodgman against one half-plane: keep(p) -> signed distance >= 0
        out = []
        for i in range(len(poly)):
            p, q = poly[i], poly[(i + 1) % len(poly)]
            dp, dq = keep(p), keep(q)
            if dp >= 0:
                out.append(p)
            if (dp >= 0) != (dq >= 0):
                t = dp / (dp - dq)
                out.append((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t))
        return out

    disc = [(radius * math.cos(math.radians(a)), radius * math.sin(math.radians(a))) for a in range(0, 360, arc_steps)]
    result = []
    for j in range(6):
        apex = verts[(j + 1) % 6]
        d1, d2 = tangent(j), tangent(j + 1)
        poly = clip(disc, lambda p: (d1[0] * (p[1] - apex[1]) - d1[1] * (p[0] - apex[0])) - off)
        poly = clip(poly, lambda p: -(d2[0] * (p[1] - apex[1]) - d2[1] * (p[0] - apex[0])) - off)
        result.append(poly)
    return result


def blade_colour(j, style):
    if style == "flat":
        return AMBER
    # full: the blades facing up and to the left catch more light
    mid = math.radians(60 * j + 120)      # direction the blade points
    lit = 0.5 + 0.5 * math.cos(mid - math.radians(225))
    return lerp(AMBER_LO, AMBER_HI, lit)


def mark_svg_group(cx, cy, size, style="full", mono=None, **kw):
    """The mark as an SVG <g>, scaled so its outer diameter is `size`."""
    s = size / (2 * R_OUT)
    parts = [f'<g transform="translate({cx:.2f} {cy:.2f}) scale({s:.5f})" stroke-linejoin="round">']
    round_w = kw.get("round_w", 7.0)
    for j, poly in enumerate(blades(**kw)):
        col = hexc(mono) if mono else hexc(blade_colour(j, style))
        pts = " ".join(f"{x:.2f},{-y:.2f}" for x, y in poly)
        if style == "outline":
            parts.append(f'<polygon points="{pts}" fill="none" stroke="{col}" stroke-width="5"/>')
        else:
            parts.append(f'<polygon points="{pts}" fill="{col}" stroke="{col}" stroke-width="{round_w}"/>')
    parts.append("</g>")
    return "\n".join(parts)


def svg_doc(w, h, body, defs="", title=""):
    t = f"<title>{title}</title>" if title else ""
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">{t}'
            f"<defs>{defs}</defs>{body}</svg>\n")


# ---- raster drawing (supersampled) ----
SS = 8


def draw_mark(img, cx, cy, size, style="full", mono=None, **kw):
    """Draws the mark onto img. Corners are rounded by blurring each blade's mask and cutting it again, which rounds
    convex corners and leaves straight edges exactly where they are (the SVG gets the same look from a round-join stroke)."""
    s = size / (2 * R_OUT)
    round_w = kw.get("round_w", 7.0)
    geo = dict(kw)
    geo["round_w"] = 0.0
    sigma = (round_w / 2.0) * s / 1.5
    w_out = max(1, int(round(5 * s)))
    for j, poly in enumerate(blades(**geo)):
        col = mono if mono else blade_colour(j, style)
        pts = [(cx + x * s, cy - y * s) for x, y in poly]
        if style == "outline":
            d = ImageDraw.Draw(img, "RGBA")
            d.line(pts + [pts[0]], fill=col + (255,), width=w_out, joint="curve")
            continue
        mask = Image.new("L", img.size, 0)
        ImageDraw.Draw(mask).polygon(pts, fill=255)
        if sigma >= 0.6:
            mask = mask.filter(ImageFilter.GaussianBlur(sigma)).point(lambda v: 255 if v > 127 else 0)
        layer = Image.new("RGBA", img.size, col + (255,))
        img.paste(layer, (0, 0), mask)


def finish(img, size):
    return img.resize((size, size), Image.LANCZOS) if img.width != size else img


def render_mark_png(size, style="full", mono=None, bg=None, scale=0.92, **kw):
    big = size * SS
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0) if bg is None else bg + (255,))
    draw_mark(img, big / 2, big / 2, big * scale, style, mono, **kw)
    return finish(img, size)


def rounded_tile(img, radius_frac, fill_fn):
    size = img.width
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=int(size * radius_frac), fill=255)
    fill = fill_fn(size)
    img.paste(fill, (0, 0), mask)
    return mask


def vertical_gradient(size, top, bottom):
    g = Image.new("RGBA", (size, size))
    px = g.load()
    for y in range(size):
        c = lerp(top, bottom, y / max(1, size - 1)) + (255,)
        for x in range(size):
            px[x, y] = c
    return g


def radial_glow(size, centre, radius, colour, alpha):
    g = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    px = g.load()
    for y in range(size):
        for x in range(size):
            d = math.hypot(x - centre[0], y - centre[1]) / radius
            if d < 1:
                a = (1 - d) ** 1.6 * alpha
                px[x, y] = colour + (int(a * 255),)
    return g


def app_icon_png(size, variant):
    """variant: A glow (default), B flat, C glass, D outline."""
    big = size * SS if size <= 256 else size       # very large sizes are drawn directly
    ss = big // size
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    tiny = size <= 32
    scale = 0.78 if tiny else 0.62
    gk = {"r_open": 42.0, "gap": 13.0, "round_w": 0.0} if tiny else {}
    if variant == "B":
        rounded_tile(img, 0.225, lambda s: Image.new("RGBA", (s, s), CHARCOAL_2 + (255,)))
        draw_mark(img, big / 2, big / 2, big * scale, "flat", **gk)
    elif variant == "D":
        rounded_tile(img, 0.225, lambda s: Image.new("RGBA", (s, s), CHARCOAL_2 + (255,)))
        draw_mark(img, big / 2, big / 2, big * scale, "outline", mono=AMBER, **gk)
    else:
        rounded_tile(img, 0.225, lambda s: vertical_gradient(s, (0x1D, 0x20, 0x27), (0x0E, 0x0F, 0x13)))
        if variant == "A":
            glow = radial_glow(big, (big / 2, big / 2), big * 0.46, AMBER, 0.20)
            img.alpha_composite(glow)
        draw_mark(img, big / 2, big / 2, big * scale, "full", **gk)
        if variant == "C":
            sheen = Image.new("RGBA", (big, big), (0, 0, 0, 0))
            sd = ImageDraw.Draw(sheen)
            sd.rounded_rectangle([0, 0, big - 1, int(big * 0.52)], radius=int(big * 0.225), fill=(255, 255, 255, 14))
            mask = Image.new("L", (big, big), 0)
            ImageDraw.Draw(mask).rounded_rectangle([0, 0, big - 1, big - 1], radius=int(big * 0.225), fill=255)
            sheen.putalpha(Image.composite(sheen.getchannel("A"), Image.new("L", (big, big), 0), mask))
            img.alpha_composite(sheen)
            rim = Image.new("RGBA", (big, big), (0, 0, 0, 0))
            ImageDraw.Draw(rim).rounded_rectangle([1, 1, big - 2, big - 2], radius=int(big * 0.225), outline=(255, 255, 255, 36), width=max(1, ss))
            img.alpha_composite(rim)
    return finish(img, size)


def tray_png(size, variant, colour):
    """Tray glyphs: flat, one colour. D is the shipping default; at 16 px the gaps are wider and the opening larger."""
    small = size <= 20
    kw = {"r_open": 42.0 if small else 38.0, "gap": 14.0 if small else 9.0, "round_w": 0.0 if small else 4.0}
    if variant == "B":
        kw = {"r_open": 38.0, "gap": 9.0, "round_w": 4.0}
    big = size * SS
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    if variant == "C":
        draw_mark(img, big / 2, big / 2, big * 0.96, "outline", mono=colour, **{"r_open": 42.0, "gap": 14.0, "round_w": 0.0})
    else:
        draw_mark(img, big / 2, big / 2, big * 0.96, "flat", mono=colour, **kw)
    return finish(img, size)


# ---- ICO writer (PNG-compressed entries, one native render per size) ----
def write_ico(path, pngs):
    entries = []
    blobs = []
    offset = 6 + 16 * len(pngs)
    for size, im in pngs:
        buf = io.BytesIO()
        im.save(buf, "PNG")
        data = buf.getvalue()
        entries.append(struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset))
        blobs.append(data)
        offset += len(data)
    with open(path, "wb") as f:
        f.write(struct.pack("<HHH", 0, 1, len(pngs)))
        for e in entries:
            f.write(e)
        for b in blobs:
            f.write(b)


# ---- wordmark as outlines (Garet Heavy) ----
def wordmark_path(text, em):
    """SVG path data for the text at font size `em`, baseline at y=0, plus (width, ascent)."""
    from fontTools.ttLib import TTFont
    from fontTools.pens.svgPathPen import SVGPathPen
    from fontTools.pens.transformPen import TransformPen

    font = TTFont(FONT_HEAVY)
    gs = font.getGlyphSet()
    cmap = font.getBestCmap()
    upm = font["head"].unitsPerEm
    sc = em / upm
    x = 0.0
    d = []
    for ch in text:
        g = cmap[ord(ch)]
        pen = SVGPathPen(gs)
        gs[g].draw(TransformPen(pen, (sc, 0, 0, -sc, x, 0)))
        d.append(pen.getCommands())
        x += gs[g].width * sc
    return " ".join(d), x


def save(path, data, mode="w"):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, mode, **({"encoding": "utf8", "newline": "\n"} if mode == "w" else {})) as f:
        f.write(data)


def lockup_svg(layout, theme):
    """layout: horizontal | stacked ; theme: dark (for dark backgrounds) | light | mono-white | mono-black"""
    if theme == "dark":
        word, style, mono = hexc(TEXT_DARK_BG), "full", None
    elif theme == "light":
        word, style, mono = hexc(TEXT_LIGHT_BG), "full", None
    elif theme == "mono-white":
        word, style, mono = hexc(MONO_WHITE), "flat", MONO_WHITE
    else:
        word, style, mono = hexc(MONO_BLACK), "flat", MONO_BLACK
    em = 96
    d, width = wordmark_path("Lumina", em)
    mark = 168
    if layout == "horizontal":
        gap = 40
        w, h = int(mark + gap + width + 8), mark
        mark_g = mark_svg_group(mark / 2, h / 2, mark, style, mono)
        text = f'<path d="{d}" fill="{word}" transform="translate({mark + gap} {h / 2 + em * 0.36:.1f})"/>'
    else:
        gap = 36
        w, h = int(max(mark, width) + 16), int(mark + gap + em * 0.92)
        mark_g = mark_svg_group(w / 2, mark / 2, mark, style, mono)
        text = f'<path d="{d}" fill="{word}" transform="translate({(w - width) / 2:.1f} {mark + gap + em * 0.72:.1f})"/>'
    return svg_doc(w, h, mark_g + text, title="Lumina")


def build():
    # --- logo masters ---
    for style, name in (("full", "aperture-mark"), ("flat", "aperture-mark-flat")):
        save(os.path.join(OUT, "logo", name + ".svg"), svg_doc(512, 512, mark_svg_group(256, 256, 472, style), title="Lumina aperture mark"))
    save(os.path.join(OUT, "logo", "aperture-mark-mono-white.svg"), svg_doc(512, 512, mark_svg_group(256, 256, 472, "flat", MONO_WHITE)))
    save(os.path.join(OUT, "logo", "aperture-mark-mono-black.svg"), svg_doc(512, 512, mark_svg_group(256, 256, 472, "flat", MONO_BLACK)))
    save(os.path.join(OUT, "logo", "aperture-mark-outline.svg"), svg_doc(512, 512, mark_svg_group(256, 256, 472, "outline", AMBER)))
    for layout in ("horizontal", "stacked"):
        for theme in ("dark", "light", "mono-white", "mono-black"):
            save(os.path.join(OUT, "logo", f"lumina-{layout}-{theme}.svg"), lockup_svg(layout, theme))
    wd, ww = wordmark_path("Lumina", 96)
    save(os.path.join(OUT, "logo", "wordmark-dark.svg"), svg_doc(int(ww + 8), 120, f'<path d="{wd}" fill="{hexc(TEXT_DARK_BG)}" transform="translate(4 88)"/>'))
    save(os.path.join(OUT, "logo", "wordmark-light.svg"), svg_doc(int(ww + 8), 120, f'<path d="{wd}" fill="{hexc(TEXT_LIGHT_BG)}" transform="translate(4 88)"/>'))

    # logo PNGs
    for size in (1024, 256, 128, 64, 48, 32, 16):
        render_mark_png(size, "full").save(os.path.join(OUT, "logo", f"aperture-mark-{size}.png")) if os.path.isdir(os.path.join(OUT, "logo")) else None
    for size in (1024, 256, 64, 32, 16):
        render_mark_png(size, "flat", scale=0.96).save(os.path.join(OUT, "logo", f"aperture-mark-flat-{size}.png"))

    # --- app icons (SVG sources use the same geometry; the rasters are the production art) ---
    tile_defs_a = ('<linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1D2027"/><stop offset="1" stop-color="#0E0F13"/></linearGradient>'
                   '<radialGradient id="glow" cx="0.5" cy="0.5" r="0.46"><stop offset="0" stop-color="#F6A23A" stop-opacity="0.30"/><stop offset="1" stop-color="#F6A23A" stop-opacity="0"/></radialGradient>')
    tile = lambda fill: f'<rect width="1024" height="1024" rx="230" fill="{fill}"/>'
    save(os.path.join(OUT, "app-icon", "app-icon-A-glow.svg"),
         svg_doc(1024, 1024, tile("url(#g)") + '<circle cx="512" cy="512" r="470" fill="url(#glow)"/>' + mark_svg_group(512, 512, 635, "full"), tile_defs_a, "Lumina app icon A"))
    save(os.path.join(OUT, "app-icon", "app-icon-B-flat.svg"), svg_doc(1024, 1024, tile(hexc(CHARCOAL_2)) + mark_svg_group(512, 512, 635, "flat"), "", "Lumina app icon B"))
    save(os.path.join(OUT, "app-icon", "app-icon-C-glass.svg"),
         svg_doc(1024, 1024, tile("url(#g)") + '<rect width="1024" height="530" rx="230" fill="#fff" fill-opacity="0.055"/>' + mark_svg_group(512, 512, 635, "full")
                 + '<rect x="2" y="2" width="1020" height="1020" rx="228" fill="none" stroke="#fff" stroke-opacity="0.14" stroke-width="3"/>', tile_defs_a, "Lumina app icon C"))
    save(os.path.join(OUT, "app-icon", "app-icon-D-outline.svg"), svg_doc(1024, 1024, tile(hexc(CHARCOAL_2)) + mark_svg_group(512, 512, 635, "outline", AMBER), "", "Lumina app icon D"))
    sizes = (1024, 256, 128, 64, 48, 32, 16)
    for v, name in (("A", "A-glow"), ("B", "B-flat"), ("C", "C-glass"), ("D", "D-outline")):
        for size in sizes:
            app_icon_png(size, v).save(os.path.join(OUT, "app-icon", f"app-icon-{name}-{size}.png"))
    # the shipping icon: glow art for large sizes, the flat art below 48 px where the glow only blurs the blades
    ship = [(s, app_icon_png(s, "A" if s >= 48 else "B")) for s in (256, 128, 64, 48, 32, 24, 16)]
    write_ico(os.path.join(OUT, "app-icon", "lumina.ico"), ship)

    # --- tray icons (flat, one colour; the shipping default is D) ---
    for v, name in (("A", "A-filled"), ("B", "B-compact"), ("C", "C-outline"), ("D", "D-mono")):
        svg_kw = {"r_open": 30.0, "gap": 8.0, "round_w": 2.0} if v in ("A", "B") else {"r_open": 42.0, "gap": 14.0, "round_w": 0.0}
        for tone, col in (("light", MONO_BLACK), ("dark", MONO_WHITE)):
            style = "outline" if v == "C" else "flat"
            save(os.path.join(OUT, "tray-icon", f"tray-{name}-{tone}.svg"), svg_doc(64, 64, mark_svg_group(32, 32, 62, style, col, **svg_kw), "", f"Lumina tray {name}"))
            for size in (32, 24, 20, 16):
                tray_png(size, v, col).save(os.path.join(OUT, "tray-icon", f"tray-{name}-{tone}-{size}.png"))
    for tone, col in (("light", MONO_BLACK), ("dark", MONO_WHITE)):
        write_ico(os.path.join(OUT, "tray-icon", f"lumina-tray-{tone}.ico"), [(s, tray_png(s, "D", col)) for s in (32, 24, 20, 16)])

    # --- splash ---
    sp = lockup_svg("stacked", "dark")
    save(os.path.join(OUT, "splash", "splash-mark.svg"), svg_doc(800, 500, '<rect width="800" height="500" fill="#0C0D10"/>' + mark_svg_group(400, 200, 190, "full")
         + f'<path d="{wordmark_path("Lumina", 64)[0]}" fill="{hexc(TEXT_DARK_BG)}" transform="translate({400 - wordmark_path("Lumina", 64)[1] / 2:.1f} 370)"/>'))
    img = Image.new("RGBA", (1600, 1000), CHARCOAL + (255,))
    big = Image.new("RGBA", (1600 * 1, 1000 * 1), CHARCOAL + (255,))
    glow = radial_glow(1600, (800, 400), 520, AMBER, 0.10).crop((0, 0, 1600, 1000))
    big.alpha_composite(glow)
    m = render_mark_png(380, "full")
    big.alpha_composite(m, (800 - 190, 400 - 190 - 0))
    big.convert("RGB").save(os.path.join(OUT, "splash", "splash-1600x1000.png"))

    # --- the start-up splash: static, quiet, charcoal with the aperture and one line ---
    from PIL import ImageFont
    heavy = ImageFont.truetype(FONT_HEAVY, 64 * 2)
    book = ImageFont.truetype(os.path.join(ROOT, "src", "Lumina", "Assets", "Fonts", "Garet-Book.ttf"), 21 * 2)
    sw, sh = 680 * 2, 420 * 2
    sp_img = Image.new("RGBA", (sw, sh), CHARCOAL + (255,))
    sp_img.alpha_composite(radial_glow(sw, (sw // 2, int(sh * 0.40)), sw * 0.42, AMBER, 0.11).crop((0, 0, sw, sh)))
    mk = render_mark_png(210 * 2, "full")
    sp_img.alpha_composite(mk, (sw // 2 - mk.width // 2, int(sh * 0.10)))
    dr = ImageDraw.Draw(sp_img)
    tw = dr.textlength("Lumina", font=heavy)
    dr.text(((sw - tw) / 2, int(sh * 0.60)), "Lumina", font=heavy, fill=TEXT_DARK_BG + (255,))
    line = "Your media, in the light."
    tl = dr.textlength(line, font=book)
    dr.text(((sw - tl) / 2, int(sh * 0.60) + 64 * 2 + 28), line, font=book, fill=(0x9B, 0xA1, 0xAE, 255))
    sp_img.convert("RGB").resize((680, 420), Image.LANCZOS).save(os.path.join(OUT, "splash", "splash-680x420.png"))

    # --- preview sheet ---
    sheet = Image.new("RGB", (1500, 900), (12, 13, 16))
    d = ImageDraw.Draw(sheet)
    x = 40
    for size in (256, 128, 64, 48, 32, 16):
        sheet.paste(app_icon_png(size, "A"), (x, 40), app_icon_png(size, "A"))
        x += size + 28
    x = 40
    for v in "ABCD":
        icon = app_icon_png(180, v)
        sheet.paste(icon, (x, 340), icon)
        x += 210
    x = 40
    for tone, col, bgc in (("dark", MONO_WHITE, (32, 32, 36)), ("light", MONO_BLACK, (236, 238, 242))):
        for v in "ABCD":
            plate = Image.new("RGB", (120, 120), bgc)
            for i, size in enumerate((32, 24, 16)):
                t = tray_png(size, v, col)
                plate.paste(t, (8 + i * 42, 12), t)
            t16 = tray_png(16, v, col).resize((64, 64), Image.NEAREST)
            plate.paste(t16, (28, 52), t16)
            sheet.paste(plate, (x, 600))
            x += 136
    mark = render_mark_png(300, "full")
    sheet.paste(mark, (1180, 560), mark)
    sheet.save(os.path.join(OUT, "previews", "mark-sizes.png")) if os.path.isdir(os.path.join(OUT, "previews")) else (os.makedirs(os.path.join(OUT, "previews")), sheet.save(os.path.join(OUT, "previews", "mark-sizes.png")))
    # --- the same mark as a WPF drawing, so the app draws it as vector at any size ---
    def xaml_mark(style):
        rw = 7.0
        parts = []
        for k, poly in enumerate(blades()):
            col = "#FF%02X%02X%02X" % blade_colour(k, style)
            geo = "M " + " L ".join(f"{x:.2f},{-y:.2f}" for x, y in poly) + " Z"
            parts.append(f'      <GeometryDrawing Brush="{col}" Geometry="{geo}"><GeometryDrawing.Pen><Pen Brush="{col}" Thickness="{rw}" LineJoin="Round"/></GeometryDrawing.Pen></GeometryDrawing>')
        return "\n".join(parts)

    def xaml_image(key, style):
        return ('  <DrawingImage x:Key="' + key + '"><DrawingImage.Drawing>\n'
                '    <DrawingGroup ClipGeometry="M -100,-100 L 100,-100 L 100,100 L -100,100 Z">\n' + xaml_mark(style) + '\n    </DrawingGroup>\n'
                '  </DrawingImage.Drawing></DrawingImage>\n')

    xaml = ('<!-- Generated by scripts/brand/make_brand.py. Do not edit: change the script and run it again. -->\n'
            '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n'
            + xaml_image("Lumina.Brand.Mark", "full") + xaml_image("Lumina.Brand.MarkFlat", "flat") + '</ResourceDictionary>\n')
    save(os.path.join(ROOT, "src", "Lumina", "Theme", "BrandMark.xaml"), xaml)
    print("brand assets written to", OUT)


if __name__ == "__main__":
    for sub in ("logo", "app-icon", "tray-icon", "splash", "previews", "marketing"):
        os.makedirs(os.path.join(OUT, sub), exist_ok=True)
    build()
