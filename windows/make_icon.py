#!/usr/bin/env python3
"""make_icon.py — windows/Ohmega.ico from ../ohmega.png.

Windows icons have no backdrop like on the Mac: just the artwork with a small transparent margin.
Every size is scaled separately (LANCZOS from 512 px), so small icons (16, 24 px) stay crisp.
Run with a Python that has Pillow (~/.venvs/navod).
"""
from pathlib import Path

from PIL import Image, ImageFilter

HERE = Path(__file__).resolve().parent
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

art = Image.open(HERE.parent / "ohmega.png").convert("RGBA")
images = []
for size in SIZES:
    inner = round(size * (0.94 if size >= 32 else 1.0))     # tiny sizes need every pixel
    img = art.resize((inner, inner), Image.LANCZOS)
    if size <= 24:
        img = img.filter(ImageFilter.UnsharpMask(radius=0.6, percent=60, threshold=1))
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.alpha_composite(img, ((size - inner) // 2, (size - inner) // 2))
    images.append(canvas)
images[-1].save(HERE / "Ohmega.ico", format="ICO", sizes=[(s, s) for s in SIZES], append_images=images[:-1])
print("Ohmega.ico:", ", ".join(map(str, SIZES)))
