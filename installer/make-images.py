"""Renders the installer's wizard images from the Annex logo (run from the repo root):
    python installer/make-images.py
wizard-*.bmp: welcome/finish banner (164x314 at 100%, 2x at 200%), small-*.bmp: page-corner logo.
"""
import importlib.util
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location('logo', ROOT / 'assets' / 'make-logo.py')
logo = importlib.util.module_from_spec(spec); spec.loader.exec_module(logo)
FONTS = Path('C:/Windows/Fonts')
TOP, BOTTOM = (22, 25, 34), (11, 13, 18)
TEXT, MUTED = (205, 214, 244), (140, 147, 173)


def banner(scale):
    w, h = 164 * scale, 314 * scale
    img = Image.new('RGB', (w, h))
    px = img.load()
    for y in range(h):
        t = y / (h - 1)
        c = tuple(round(a + (b - a) * t) for a, b in zip(TOP, BOTTOM))
        for x in range(w):
            px[x, y] = c
    glow = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse([w * 0.12, h * 0.22, w * 0.88, h * 0.48], fill=(137, 180, 250, 34))
    glow = glow.filter(ImageFilter.GaussianBlur(28 * scale))
    img.paste(glow, (0, 0), glow)
    mark = logo.render(96 * scale, logo.FULL)
    img.paste(mark, ((w - mark.width) // 2, int(h * 0.20)), mark)
    d = ImageDraw.Draw(img)
    title = ImageFont.truetype(str(FONTS / 'segoeuib.ttf'), 26 * scale)
    sub = ImageFont.truetype(str(FONTS / 'segoeui.ttf'), 10 * scale)
    for text, font, color, y in (('Annex', title, TEXT, 0.53), ('Second desktop', sub, MUTED, 0.635), ('for streaming', sub, MUTED, 0.675)):
        tw = d.textlength(text, font=font)
        d.text(((w - tw) / 2, h * y), text, font=font, fill=color)
    return img


HEADER = (43, 43, 43)  # Inno Setup's dark wizard background


def small(size):
    mark = logo.render(size, logo.FULL)
    img = Image.new('RGB', (size, size), HEADER)
    img.paste(mark, (0, 0), mark)
    return img


if __name__ == '__main__':
    out = ROOT / 'installer'
    for s in (1, 2):
        banner(s).save(out / f'wizard-{s * 100}.bmp')
        small(55 * s).save(out / f'small-{s * 100}.bmp')
    print('wrote installer/wizard-*.bmp and small-*.bmp')
