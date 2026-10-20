"""
Generates the paper-and-ink UI art for Party Taxes (parchment, paper slips, wax, ink flourishes,
stamps, seal, coin, vignette, and the diegetic objects: ledger page, notice board, pins, signpost
planks, ink wash, banner, scroll rods, hang tags, eyelets, ink boxes) into
Assets/PartyTaxes/Resources/PartyTaxes/UI.

Everything is procedural and seeded, so re-running gives the same images. Existing .meta files
are kept (their GUIDs stay stable); missing ones are written as Sprite imports.
The 9-slice borders printed below must match the borders in PTUITheme.cs.

    python3 Tools/UIArt/generate_ui_art.py            (needs numpy and Pillow)
"""
import os
import uuid

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "PartyTaxes", "Resources", "PartyTaxes", "UI")
FONTS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Fonts")          # UnifrakturMaguntia (OFL), only for the embossed Q

PARCHMENT = (231, 213, 174)
SLIP = (242, 231, 205)
INK = (43, 29, 20)
BURN = (104, 64, 28)
WAX = (126, 20, 24)
GOLD = (196, 150, 52)

rng = np.random.default_rng(170)


# ── Helpers ─────────────────────────────────────────────────────────────────
def noise(w, h, scale, octaves=4, persistence=0.5):
    """Fractal value noise in 0..1 (upscaled random grids, finest octave last)."""
    out = np.zeros((h, w), np.float32)
    amp, total = 1.0, 0.0
    for _ in range(octaves):
        gw, gh = max(2, int(w / scale) + 2), max(2, int(h / scale) + 2)
        grid = Image.fromarray(rng.random((gh, gw)).astype(np.float32), "F")
        out += np.asarray(grid.resize((w, h), Image.BICUBIC), np.float32) * amp
        total += amp
        amp *= persistence
        scale = max(1.0, scale / 2)
    return np.clip(out / total, 0, 1)


def edge_distance(w, h):
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    return np.minimum(np.minimum(x, w - 1 - x), np.minimum(y, h - 1 - y))


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def to_image(rgb, alpha):
    rgba = np.dstack([np.clip(rgb, 0, 255), np.clip(alpha * 255, 0, 255)]).astype(np.uint8)
    return Image.fromarray(rgba, "RGBA")


def fibres(w, h, count, length, rgb, alpha):
    """Faint paper fibres: short soft strokes, mostly horizontal."""
    layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    for _ in range(count):
        x, y = rng.random() * w, rng.random() * h
        angle = rng.normal(0, 0.5)
        size = length * (0.4 + rng.random())
        dx, dy = np.cos(angle) * size, np.sin(angle) * size
        shade = rng.choice([-1, 1])
        colour = tuple(int(c + shade * 28) for c in rgb) + (int(255 * alpha * (0.5 + rng.random())),)
        draw.line([(x, y), (x + dx, y + dy)], fill=colour, width=1)
    return layer.filter(ImageFilter.GaussianBlur(0.6))


def paper(w, h, base, margin, deckle, burn_width, burn_strength, mottling=0.09, fibre_count=0):
    """Paper sheet with a torn (deckled) edge and darkened, burnt-looking rim."""
    rough = noise(w, h, 48, 5) - 0.5
    fine = noise(w, h, 6, 2) - 0.5
    d = edge_distance(w, h) + rough * deckle * 2 + fine * deckle * 0.6
    alpha = smoothstep(margin - 1.2, margin + 1.2, d)

    mottle = noise(w, h, 160, 5)
    shade = 1 + (mottle - 0.5) * 2 * mottling
    rgb = np.dstack([np.full((h, w), c, np.float32) for c in base]) * shade[..., None]

    burn = np.exp(-np.maximum(d - margin, 0) / burn_width) * burn_strength
    burn *= 0.75 + 0.5 * noise(w, h, 30, 3)
    rgb = rgb * (1 - burn[..., None]) + np.array(BURN, np.float32) * burn[..., None]

    img = to_image(rgb, alpha)
    if fibre_count:
        img = Image.alpha_composite(img, fibres(w, h, fibre_count, 28, base, 0.10))
        img.putalpha(Image.fromarray((alpha * 255).astype(np.uint8)))
    return img


def supersampled(w, h, scale, draw_fn):
    big = Image.new("RGBA", (w * scale, h * scale), (0, 0, 0, 0))
    draw_fn(ImageDraw.Draw(big), scale)
    return big.resize((w, h), Image.LANCZOS)


def emboss_letter(img, letter, font_path, size, centre, light, dark, fill):
    """Pressed-in letter (for wax and coins): shadow above-left, highlight below-right."""
    font = ImageFont.truetype(font_path, size)
    for offset, colour in (((-2, -2), dark), ((2, 2), light), ((0, 0), fill)):
        layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
        ImageDraw.Draw(layer).text((centre[0] + offset[0], centre[1] + offset[1]), letter, font=font, fill=colour, anchor="mm")
        img = Image.alpha_composite(img, layer)
    return img


# ── The art ─────────────────────────────────────────────────────────────────
def paper_panel():
    return paper(1024, 1024, PARCHMENT, margin=16, deckle=10, burn_width=46, burn_strength=0.55, fibre_count=2600)


def paper_slip():
    w, h = 512, 128
    img = paper(w, h, SLIP, margin=5, deckle=3, burn_width=14, burn_strength=0.35, mottling=0.05, fibre_count=300)
    rules = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(rules)
    draw.rectangle([13, 13, w - 14, h - 14], outline=INK + (150,), width=2)                      # double ink rule
    draw.rectangle([19, 19, w - 20, h - 20], outline=INK + (90,), width=1)
    for cx, cy in ((13, 13), (w - 14, 13), (13, h - 14), (w - 14, h - 14)):                     # corner dots
        draw.ellipse([cx - 3, cy - 3, cx + 3, cy + 3], fill=INK + (170,))
    return Image.alpha_composite(img, rules.filter(ImageFilter.GaussianBlur(0.4)))


def wax_slip():
    w, h = 512, 128
    rough = noise(w, h, 40, 4) - 0.5
    d = edge_distance(w, h) + rough * 9
    alpha = smoothstep(5, 7.5, d)
    mottle = noise(w, h, 60, 4)
    y = np.mgrid[0:h, 0:w][0].astype(np.float32) / h
    light = 1.18 - 0.45 * y + (mottle - 0.5) * 0.35                                              # top-lit sheen
    rim = np.exp(-np.maximum(d - 6, 0) / 7) * 0.45                                               # rolled, darker rim
    rgb = np.dstack([np.full((h, w), c, np.float32) for c in WAX]) * (light - rim)[..., None]
    img = to_image(rgb, alpha)
    ring = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(ring).rounded_rectangle([15, 15, w - 16, h - 16], radius=14, outline=(214, 92, 76, 110), width=2)
    return Image.alpha_composite(img, ring.filter(ImageFilter.GaussianBlur(0.8)))


def ink_divider():
    w, h = 1024, 64

    def draw(d, s):
        cx, cy = w * s / 2, h * s / 2
        for side in (-1, 1):
            for i in range(400):                                                                 # tapering quill stroke
                t = i / 400
                x = cx + side * (34 + t * 430) * s
                r = (4.4 * (1 - t) ** 1.3 + 0.55) * s
                d.ellipse([x - r, cy - r, x + r, cy + r], fill=INK + (235,))
            ex = cx + side * 470 * s                                                             # curl at the end
            d.arc([ex - 14 * s, cy - 22 * s, ex + 14 * s, cy + 6 * s], 0 if side > 0 else 180, 270 if side > 0 else 90,
                  fill=INK + (220,), width=int(2 * s))
            for k in (1, 2):                                                                     # dots beside the lozenge
                dx = cx + side * (20 + 9 * k) * s
                d.ellipse([dx - 2.4 * s, cy - 2.4 * s, dx + 2.4 * s, cy + 2.4 * s], fill=INK + (230,))
        d.polygon([(cx, cy - 13 * s), (cx + 11 * s, cy), (cx, cy + 13 * s), (cx - 11 * s, cy)], fill=INK + (240,))
        d.polygon([(cx, cy - 6 * s), (cx + 5 * s, cy), (cx, cy + 6 * s), (cx - 5 * s, cy)], fill=PARCHMENT + (255,))

    return supersampled(w, h, 4, draw)


def stamp():
    """White rubber-stamp frame, tinted in code; speckled where the ink didn't take."""
    w, h = 512, 256
    frame = supersampled(w, h, 2, lambda d, s: (
        d.rounded_rectangle([10 * s, 10 * s, (w - 11) * s, (h - 11) * s], radius=18 * s, outline=(255, 255, 255, 255), width=10 * s),
        d.rounded_rectangle([28 * s, 28 * s, (w - 29) * s, (h - 29) * s], radius=10 * s, outline=(255, 255, 255, 255), width=4 * s)))
    a = np.asarray(frame, np.float32)[..., 3] / 255
    grit = smoothstep(0.30, 0.55, noise(w, h, 5, 2)) * smoothstep(0.2, 0.5, noise(w, h, 70, 3))
    a *= 0.35 + 0.65 * grit
    return to_image(np.full((h, w, 3), 255, np.float32), a)


def wax_seal(blank=False):
    w = h = 256
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    dx, dy = x - w / 2, y - h / 2
    r = np.sqrt(dx * dx + dy * dy)
    ang = np.arctan2(dy, dx)
    wobble = 7 * np.sin(ang * 7 + 0.6) + 4 * np.sin(ang * 13 + 2.1) + 3 * np.sin(ang * 3)       # squashed-blob outline
    edge = 100 + wobble
    alpha = smoothstep(edge + 1.5, edge - 1.5, r)
    shadow = smoothstep(edge + 10, edge - 4, np.sqrt((dx - 4) ** 2 + (dy - 6) ** 2)) * 0.45
    light = 1.15 - 0.35 * (dx + dy) / 200 + (noise(w, h, 40, 3) - 0.5) * 0.25
    ring = np.exp(-((r - 74) ** 2) / 18)                                                         # pressed ring of the signet
    light = light - ring * 0.25 * np.sign(dx + dy) - np.exp(-np.maximum(edge - r, 0) / 9) * 0.3
    rgb = np.dstack([np.full((h, w), c, np.float32) for c in WAX]) * light[..., None]
    body = to_image(rgb, alpha)
    base = to_image(np.zeros((h, w, 3)), shadow)
    img = Image.alpha_composite(base, body)
    if blank:
        return img
    return emboss_letter(img, "Q", os.path.join(FONTS, "UnifrakturMaguntia-Book.ttf"), 112, (w / 2, h / 2 + 4),
                         light=(196, 70, 62, 200), dark=(60, 6, 8, 220), fill=(112, 16, 20, 255))


def coin():
    w = h = 128
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    dx, dy = x - w / 2, y - h / 2
    r = np.sqrt(dx * dx + dy * dy)
    alpha = smoothstep(58.5, 56.5, r)
    light = 1.1 - 0.3 * (dx + dy) / 110 + (noise(w, h, 12, 2) - 0.5) * 0.12
    rim = np.exp(-((r - 50) ** 2) / 6) * 0.35
    rgb = np.dstack([np.full((h, w), c, np.float32) for c in GOLD]) * (light - rim)[..., None]
    img = to_image(rgb, alpha)
    return emboss_letter(img, "Q", os.path.join(FONTS, "UnifrakturMaguntia-Book.ttf"), 60, (w / 2, h / 2 + 2),
                         light=(250, 222, 140, 220), dark=(110, 74, 16, 220), fill=(178, 132, 40, 255))


def vignette():
    w = h = 512
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    r = np.sqrt(((x - w / 2) / (w / 2)) ** 2 + ((y - h / 2) / (h / 2)) ** 2) / np.sqrt(2)
    return to_image(np.zeros((h, w, 3)), 0.5 + 0.5 * smoothstep(0.25, 1.0, r))



# ── Diegetic objects (ledger book, notice board, signs, banner, scroll, tags) ──
WOOD = (122, 80, 44)
LEDGER = (240, 231, 208)


def grain(w, h, streaks=18, vertical=False):
    """Wood grain: random rows stretched along the board, plus a little fine noise."""
    if vertical:
        return grain(h, w, streaks).T
    rows = Image.fromarray(rng.random((h // 2 + 2, streaks)).astype(np.float32), "F").resize((w, h), Image.BICUBIC)
    return np.clip(np.asarray(rows, np.float32) * 0.8 + noise(w, h, 4, 2) * 0.2, 0, 1)


def ledger_page():
    """A right-hand ledger page: clean cut edges, stitched binding on the left, pages stacked behind."""
    w = h = 1024
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    left, top, right, bottom = 6, 6, w - 24, h - 24
    inside = ((x >= left) & (x <= right) & (y >= top) & (y <= bottom)).astype(np.float32)
    mottle = noise(w, h, 180, 4)
    rgb = np.dstack([np.full((h, w), c, np.float32) for c in LEDGER]) * (1 + (mottle - 0.5) * 0.08)[..., None]
    gutter = np.exp(-np.maximum(x - left, 0) / 44) * 0.42                                        # shadow where the page bends into the spine
    age = np.exp(-np.minimum(np.minimum(x - left, right - x), np.minimum(y - top, bottom - y)).clip(0) / 26) * 0.18
    age *= 0.7 + 0.6 * noise(w, h, 40, 3)
    shade = np.clip(gutter + age, 0, 0.6)
    rgb = rgb * (1 - shade[..., None]) + np.array(BURN, np.float32) * shade[..., None]
    page = Image.alpha_composite(to_image(rgb, inside), fibres(w, h, 1600, 24, LEDGER, 0.07))
    page.putalpha(Image.fromarray((inside * 255).astype(np.uint8)))

    sheet = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    stack = ImageDraw.Draw(sheet)
    for off in (16, 11, 6):                                                                      # the rest of the book peeking out
        stack.rectangle([left + off, top + off, right + off, bottom + off], fill=(224, 212, 184, 255), outline=(150, 118, 84, 255))
    sheet = Image.alpha_composite(sheet, page)
    stitch = ImageDraw.Draw(sheet)
    for yy in range(48, h - 60, 46):                                                             # binding thread
        stitch.line([(24, yy), (24, yy + 24)], fill=(132, 96, 64, 210), width=3)
        for hole in (yy - 2, yy + 26):
            stitch.ellipse([21, hole - 3, 27, hole + 3], fill=(66, 44, 28, 230))
    return sheet


def cork_board():
    """A notice board: speckled cork in a mitred wooden frame."""
    w = h = 512
    f = 40
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    dh = np.minimum(y, h - 1 - y)
    dv = np.minimum(x, w - 1 - x)
    edge = np.minimum(dh, dv)

    speck = noise(w, h, 3, 2)
    cork = np.dstack([np.full((h, w), c, np.float32) for c in (170, 122, 78)]) * (1 + (noise(w, h, 70, 3) - 0.5) * 0.3)[..., None]
    cork *= (1 - 0.38 * smoothstep(0.58, 0.72, speck))[..., None]
    cork += (26 * smoothstep(0.34, 0.22, speck))[..., None]
    cork *= (1 - 0.5 * np.exp(-np.maximum(edge - f, 0) / 9))[..., None]                         # the frame's shadow on the cork

    horizontal = grain(w, h)
    vertical = grain(w, h, vertical=True)
    g = np.where(dh <= dv, horizontal, vertical)                                                 # mitred corners
    wood = np.dstack([np.full((h, w), c, np.float32) for c in WOOD]) * (0.72 + 0.5 * g)[..., None]
    bevel = np.clip(1.25 - edge / f * 0.5, 0.75, 1.25)                                           # rounded outer edge
    wood *= bevel[..., None]
    mitre = np.abs(dh - dv) < 1.2
    wood[mitre & (edge < f)] *= 0.6

    rgb = np.where((edge < f)[..., None], wood, cork)
    return to_image(rgb, np.ones((h, w), np.float32))


def pin():
    """A brass tack, lit from the top left, with a soft shadow."""
    w = h = 64
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    cx, cy = 29, 28
    r = np.sqrt((x - cx) ** 2 + (y - cy) ** 2)
    shadow = smoothstep(18, 8, np.sqrt((x - cx - 4) ** 2 + (y - cy - 5) ** 2)) * 0.45
    head = smoothstep(13.5, 12, r)
    light = 1.25 - 0.6 * ((x - cx) + (y - cy)) / 26 - 0.25 * np.exp(-((r - 10) ** 2) / 4)
    spot = np.exp(-(((x - cx + 4) ** 2 + (y - cy + 4) ** 2) / 10)) * 90
    rgb = np.dstack([np.full((h, w), c, np.float32) for c in GOLD]) * light[..., None] + spot[..., None]
    img = Image.alpha_composite(to_image(np.zeros((h, w, 3)), shadow), to_image(rgb, head))
    return img


def plank():
    """A signpost board: wood with an arrow point on the left and two nails."""
    w, h = 512, 96
    shape = Image.new("L", (w * 4, h * 4), 0)
    ImageDraw.Draw(shape).polygon([(8 * 4, h * 2), (58 * 4, 8 * 4), ((w - 8) * 4, 10 * 4), ((w - 6) * 4, (h - 8) * 4), (58 * 4, (h - 6) * 4)], fill=255)
    alpha = np.asarray(shape.resize((w, h), Image.LANCZOS), np.float32) / 255
    g = grain(w, h, 24)
    y = np.mgrid[0:h, 0:w][0].astype(np.float32)
    light = 0.7 + 0.5 * g + 0.18 * (1 - y / h) - 0.22 * (alpha < 0.98)
    rgb = np.dstack([np.full((h, w), c, np.float32) for c in WOOD]) * light[..., None]
    img = to_image(rgb, alpha)
    nails = ImageDraw.Draw(img)
    for ny in (26, h - 28):
        nails.ellipse([w - 34, ny - 5, w - 24, ny + 5], fill=(58, 52, 48, 255))
        nails.ellipse([w - 32, ny - 4, w - 28, ny], fill=(150, 140, 128, 255))
    return img


def ink_wash():
    """A soft watercolour wash (white, tinted in code) for highlighting ledger lines."""
    w, h = 256, 64
    rough = noise(w, h, 22, 3) - 0.5
    d = edge_distance(w, h) + rough * 12
    alpha = smoothstep(4, 14, d) * (0.65 + 0.35 * noise(w, h, 30, 3))
    alpha += np.exp(-np.abs(d - 9) / 2.5) * 0.25                                                 # pigment pools at the edge
    return to_image(np.full((h, w, 3), 255, np.float32), np.clip(alpha, 0, 1))


def banner():
    """A cloth banner with forked, folded-back tails (white, tinted in code)."""
    w, h = 1024, 192
    s = 2
    big = Image.new("RGBA", (w * s, h * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    p = lambda pts: [(px * s, py * s) for px, py in pts]
    for side in (1, -1):
        ox = 0 if side == 1 else w
        X = lambda v: ox + side * v
        d.polygon(p([(X(8), 68), (X(214), 68), (X(214), 178), (X(8), 178), (X(62), 123)]), fill=(178, 178, 178, 255))   # tail
        d.polygon(p([(X(160), 150), (X(214), 178), (X(214), 150)]), fill=(110, 110, 110, 255))                           # fold
    d.rectangle(p([(160, 24), (w - 160, 150)]), fill=(255, 255, 255, 255))
    img = big.resize((w, h), Image.LANCZOS)
    a = np.asarray(img, np.float32)
    y = np.mgrid[0:h, 0:w][0].astype(np.float32)
    weave = 1 + (noise(w, h, 3, 2) - 0.5) * 0.12 + (noise(w, h, 90, 3) - 0.5) * 0.14
    folds = 1 - 0.1 * np.sin(np.linspace(0, 9 * np.pi, w))[None, :] ** 2
    light = (1.08 - 0.25 * (y / h)) * weave * folds
    rgb = a[..., :3] * light[..., None]
    return to_image(rgb, a[..., 3] / 255)


def scroll_rod():
    """A rolled-up end of a scroll with wooden knobs (vertical; rotate in code for horizontal)."""
    w, h = 64, 256
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    roll = ((x >= 14) & (x <= 50) & (y >= 34) & (y <= h - 34)).astype(np.float32)
    t = (x - 14) / 36
    cyl = 0.55 + 0.6 * np.sin(np.clip(t, 0, 1) * np.pi) - 0.15 * t
    paper_rgb = np.dstack([np.full((h, w), c, np.float32) for c in PARCHMENT]) * cyl[..., None]
    knob = np.zeros((h, w), np.float32)
    for k0, k1 in ((6, 38), (h - 38, h - 6)):
        knob = np.maximum(knob, ((x >= 10) & (x <= 54) & (y >= k0) & (y <= k1)).astype(np.float32))
    kt = (x - 10) / 44
    wood = np.dstack([np.full((h, w), c, np.float32) for c in (92, 58, 32)]) * (0.55 + 0.7 * np.sin(np.clip(kt, 0, 1) * np.pi))[..., None]
    rgb = np.where(knob[..., None] > 0, wood, paper_rgb)
    alpha = np.maximum(roll, knob)
    return to_image(rgb, alpha).filter(ImageFilter.GaussianBlur(0.5))


def tag():
    """A parchment hang tag with clipped top corners (for the name tags over characters)."""
    w, h = 512, 256
    img = paper(w, h, SLIP, margin=5, deckle=2.5, burn_width=16, burn_strength=0.4, mottling=0.06, fibre_count=300)
    shape = Image.new("L", (w, h), 0)
    ImageDraw.Draw(shape).polygon([(44, 4), (w - 44, 4), (w - 4, 44), (w - 4, h - 4), (4, h - 4), (4, 44)], fill=255)
    shape = shape.filter(ImageFilter.GaussianBlur(0.8))
    alpha = np.minimum(np.asarray(img, np.float32)[..., 3], np.asarray(shape, np.float32))
    img.putalpha(Image.fromarray(alpha.astype(np.uint8)))
    return img


def eyelet():
    """A brass eyelet (the tag's string hole)."""
    w = h = 64
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    r = np.sqrt((x - 32) ** 2 + (y - 32) ** 2)
    ring = smoothstep(23, 21.5, r) * smoothstep(10, 11.5, r)
    light = 1.2 - 0.5 * ((x - 32) + (y - 32)) / 40 + 0.25 * np.exp(-((r - 16) ** 2) / 6)
    rgb = np.dstack([np.full((h, w), c, np.float32) for c in GOLD]) * light[..., None]
    return to_image(rgb, ring)


def ink_box():
    """A hand-drawn ink outline for bars (HP and the like)."""
    w, h = 128, 40

    def draw(d, s):
        jitter = lambda: rng.normal(0, 0.6) * s
        pts = [(6 * s, 7 * s), ((w - 6) * s, 6 * s), ((w - 7) * s, (h - 6) * s), (6 * s, (h - 7) * s)]
        for i in range(4):
            a, b = pts[i], pts[(i + 1) % 4]
            d.line([(a[0] + jitter(), a[1] + jitter()), (b[0] + jitter(), b[1] + jitter())], fill=INK + (235,), width=int(2.6 * s))

    return supersampled(w, h, 4, draw)


# ── Output ──────────────────────────────────────────────────────────────────
SPRITE_META = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: -1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 0
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: {left}, y: {bottom}, z: {right}, w: {top}}}
  spriteGenerateFallbackPhysicsShape: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData:
    physicsShape: []
    bones: []
    spriteID: {sprite_id}
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""

# name: (builder, 9-slice border in texture pixels: left, bottom, right, top)
ART = {
    "PaperPanel": (paper_panel, (128, 128, 128, 128)),
    "PaperSlip": (paper_slip, (40, 40, 40, 40)),
    "WaxSlip": (wax_slip, (40, 40, 40, 40)),
    "InkDivider": (ink_divider, (0, 0, 0, 0)),
    "Stamp": (stamp, (56, 56, 56, 56)),
    "WaxSeal": (wax_seal, (0, 0, 0, 0)),
    "Coin": (coin, (0, 0, 0, 0)),
    "Vignette": (vignette, (0, 0, 0, 0)),
    # appended after the originals so the shared random stream leaves the earlier images unchanged
    "LedgerPage": (ledger_page, (128, 128, 128, 128)),
    "CorkBoard": (cork_board, (64, 64, 64, 64)),
    "Pin": (pin, (0, 0, 0, 0)),
    "Plank": (plank, (64, 24, 48, 24)),
    "InkWash": (ink_wash, (28, 20, 28, 20)),
    "Banner": (banner, (220, 40, 220, 40)),
    "ScrollRod": (scroll_rod, (0, 56, 0, 56)),
    "Tag": (tag, (56, 56, 56, 56)),
    "Eyelet": (eyelet, (0, 0, 0, 0)),
    "InkBox": (ink_box, (12, 12, 12, 12)),
    "WaxDisc": (lambda: wax_seal(blank=True), (0, 0, 0, 0)),
}


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, (build, (left, bottom, right, top)) in ART.items():
        path = os.path.join(OUT, name + ".png")
        image = build()
        image.save(path, optimize=True)
        meta = path + ".meta"
        if not os.path.exists(meta):
            with open(meta, "w", newline="\n") as f:
                f.write(SPRITE_META.format(guid=uuid.uuid4().hex, sprite_id=uuid.uuid4().hex,
                                           left=left, bottom=bottom, right=right, top=top))
        print(f"{name}.png  {image.size[0]}x{image.size[1]}  border L{left} B{bottom} R{right} T{top}")


if __name__ == "__main__":
    main()
