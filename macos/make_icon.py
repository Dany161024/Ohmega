#!/usr/bin/env python3
"""make_icon.py — macos/AppIcon.png from ../ohmega.png, on Apple's macOS icon grid.

macOS icons are not full-bleed: on a 1024 px canvas the shape is 824 px with a soft shadow below it,
so it sits at the same size as every other app in the Dock and Finder. ohmega.png fills its whole
canvas, which made Ohmega look bigger than the rest. Needs Pillow; build.sh uses the committed PNG.
"""
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
CANVAS, SHAPE, OVERSAMPLE = 1024, 824, 4
# ohmega.png has tighter corners than the squircle (its edge crosses the diagonal at 84 % vs 87 %),
# so the artwork is scaled this far past the shape on every side
BLEED = 32


def squircle_mask(size: int) -> Image.Image:
    """Apple's continuous-corner shape, approximated by a superellipse (|x|^5 + |y|^5 = 1)."""
    big = size * OVERSAMPLE
    r = big / 2
    points = []
    for i in range(720):
        t = i / 720 * math.tau
        c, s = math.cos(t), math.sin(t)
        points.append((r + r * math.copysign(abs(c) ** 0.4, c), r + r * math.copysign(abs(s) ** 0.4, s)))
    mask = Image.new("L", (big, big), 0)
    ImageDraw.Draw(mask).polygon(points, fill=255)
    return mask.resize((size, size), Image.LANCZOS)


def main() -> None:
    source = Image.open(HERE.parent / "ohmega.png").convert("RGBA")
    # the artwork spans 8…504 of 512 px; its own corners and soft edge must fall outside the new mask
    art = source.crop(source.getchannel("A").getbbox()).resize((SHAPE + 2 * BLEED,) * 2, Image.LANCZOS)
    art = art.crop((BLEED, BLEED, BLEED + SHAPE, BLEED + SHAPE))
    # the white waveform in ohmega.png is see-through (cut out with the background), so the Dock showed
    # through it — lay the artwork on white, which is what it was drawn on
    base = Image.new("RGBA", art.size, (255, 255, 255, 255))
    base.alpha_composite(art)
    art = base
    mask = squircle_mask(SHAPE)
    art.putalpha(mask)

    origin = (CANVAS - SHAPE) // 2
    shadow = Image.new("L", (CANVAS, CANVAS), 0)
    shadow.paste(mask, (origin, origin + 10))
    shadow = shadow.filter(ImageFilter.GaussianBlur(12)).point(lambda v: v * 30 // 100)
    icon = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    icon.putalpha(shadow)
    icon.alpha_composite(art, (origin, origin))
    icon.save(HERE / "AppIcon.png", optimize=True)
    print(f"wrote {HERE / 'AppIcon.png'}")


if __name__ == "__main__":
    main()
