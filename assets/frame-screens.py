"""Turns raw captures into README screenshots in docs/images: rounded corners, thin border, soft shadow.

    Annex.exe -capture-screens <dir>          (launcher.png, pairing.png, apps.png)
    python assets/frame-screens.py <dir> [installer-welcome.png installer-tasks.png]
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'docs' / 'images'
RADIUS, SHADOW, BORDER = 12, 24, (46, 52, 68)


def trim_black(img):
    """Drops the black invisible-resize-border strips around PrintWindow captures."""
    rgb = img.convert('RGB'); px = rgb.load(); w, h = rgb.size
    dark = lambda p: sum(p) < 12
    left = next(x for x in range(w) if not dark(px[x, h // 2]))
    right = next(x for x in range(w - 1, -1, -1) if not dark(px[x, h // 2]))
    bottom = next(y for y in range(h - 1, -1, -1) if not dark(px[w // 2, y]))
    return img.crop((left, 0, right + 1, bottom + 1))


def frame(img):
    img = img.convert('RGBA')
    w, h = img.size
    mask = Image.new('L', (w, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, w - 1, h - 1], RADIUS, fill=255)
    img.putalpha(mask)
    ImageDraw.Draw(img).rounded_rectangle([0, 0, w - 1, h - 1], RADIUS, outline=BORDER + (255,), width=1)
    canvas = Image.new('RGBA', (w + SHADOW * 2, h + SHADOW * 2), (0, 0, 0, 0))
    shadow = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle([SHADOW, SHADOW + 6, SHADOW + w, SHADOW + h + 6], RADIUS, fill=(0, 0, 0, 110))
    canvas.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(12)))
    canvas.alpha_composite(img, (SHADOW, SHADOW))
    return canvas


if __name__ == '__main__':
    OUT.mkdir(parents=True, exist_ok=True)
    src = Path(sys.argv[1])
    for name in ('launcher', 'pairing', 'apps'):
        frame(Image.open(src / f'{name}.png')).save(OUT / f'{name}.png', optimize=True)
    for raw, name in zip(sys.argv[2:], ('installer', 'installer-options')):
        frame(trim_black(Image.open(raw))).save(OUT / f'{name}.png', optimize=True)
    print('wrote', ', '.join(sorted(p.name for p in OUT.glob('*.png'))))
