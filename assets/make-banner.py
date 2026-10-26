"""README header: logo + wordmark + tagline on a transparent background, one version per GitHub theme.

    python assets/make-banner.py   ->  assets/banner-dark.png, assets/banner-light.png
"""
import importlib.util
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location('logo', ROOT / 'assets' / 'make-logo.py')
logo = importlib.util.module_from_spec(spec); spec.loader.exec_module(logo)
FONTS = Path('C:/Windows/Fonts')
THEMES = {'dark': ((230, 237, 243), (145, 152, 161)), 'light': ((31, 35, 40), (89, 99, 110))}
S = 2  # rendered at 2x for sharp text on high-DPI screens

if __name__ == '__main__':
    title = ImageFont.truetype(str(FONTS / 'segoeuib.ttf'), 96 * S)
    tag = ImageFont.truetype(str(FONTS / 'segoeui.ttf'), 26 * S)
    tagline = 'A second Windows desktop for game streaming'
    mark = logo.render(132 * S, logo.FULL)
    probe = ImageDraw.Draw(Image.new('RGBA', (1, 1)))
    title_w = probe.textlength('Annex', font=title)
    gap = 26 * S
    w, h = 900 * S, 212 * S
    for name, (fg, muted) in THEMES.items():
        img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        block = mark.width + gap + title_w
        x = (w - block) / 2
        img.alpha_composite(mark, (int(x), 8 * S))
        d.text((x + mark.width + gap, 6 * S), 'Annex', font=title, fill=fg)
        tw = d.textlength(tagline, font=tag)
        d.text(((w - tw) / 2, 156 * S), tagline, font=tag, fill=muted)
        img.save(ROOT / 'assets' / f'banner-{name}.png', optimize=True)
    print('wrote assets/banner-dark.png, assets/banner-light.png')
