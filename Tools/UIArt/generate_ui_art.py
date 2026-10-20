"""
Generates the paper-and-ink UI art for Party Taxes (parchment, paper slips, wax, ink flourishes,
stamps, seal, coin, vignette) into Assets/PartyTaxes/Resources/PartyTaxes/UI.

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


def wax_seal():
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
