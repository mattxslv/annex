"""Renders a clean sample Windows 11 desktop (default wallpaper, taskbar, a few app icons) that the
README screenshot shows inside the Annex viewer, so it doesn't expose anyone's real desktop.

    python assets/make-sample-desktop.py <icons-dir> <out.png>
<icons-dir> holds explorer.png, edge.png, chrome.png, steam.png (extracted from the installed apps).
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

W, H, BAR = 1920, 1080, 48
FONTS = Path('C:/Windows/Fonts')


def icon(path, size):
    return Image.open(path).convert('RGBA').resize((size, size), Image.LANCZOS)


def windows_logo(size):
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img); g = size // 12; half = (size - g) // 2
    for x, y in ((0, 0), (half + g, 0), (0, half + g), (half + g, half + g)):
        d.rectangle([x, y, x + half - 1, y + half - 1], fill=(71, 167, 255, 255))
    return img


if __name__ == '__main__':
    icons, out = Path(sys.argv[1]), Path(sys.argv[2])
    wall = Image.open('C:/Windows/Web/4K/Wallpaper/Windows/img0_1920x1200.jpg').convert('RGB')
    desk = wall.crop((0, 60, W, 60 + H)).convert('RGBA')

    # desktop shortcuts, top left, with the usual soft text shadow
    label = ImageFont.truetype(str(FONTS / 'segoeui.ttf'), 13)
    for i, (name, file) in enumerate((('Steam', 'steam'), ('Google Chrome', 'chrome'), ('Microsoft Edge', 'edge'))):
        x, y = 22, 18 + i * 96
        desk.alpha_composite(icon(icons / f'{file}.png', 48), (x + 14, y))
        shadow = Image.new('RGBA', desk.size, (0, 0, 0, 0)); sd = ImageDraw.Draw(shadow)
        tw = sd.textlength(name, font=label)
        sd.text((x + 38 - tw / 2 + 1, y + 54), name, font=label, fill=(0, 0, 0, 200))
        desk.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(1.2)))
        ImageDraw.Draw(desk).text((x + 38 - tw / 2, y + 53), name, font=label, fill=(255, 255, 255, 255))

    # taskbar: frosted dark strip, centred icons, clock on the right
    bar = desk.crop((0, H - BAR, W, H)).filter(ImageFilter.GaussianBlur(24))
    tint = Image.new('RGBA', bar.size, (28, 28, 30, 215))
    bar.alpha_composite(tint)
    desk.paste(bar, (0, H - BAR))
    d = ImageDraw.Draw(desk)
    d.line([(0, H - BAR), (W, H - BAR)], fill=(255, 255, 255, 28))
    row = [windows_logo(24)] + [icon(icons / f'{n}.png', 26) for n in ('explorer', 'edge', 'chrome', 'steam')]
    step = 44; x0 = W // 2 - (len(row) * step) // 2
    for i, img in enumerate(row):
        desk.alpha_composite(img, (x0 + i * step + (step - img.width) // 2, H - BAR + (BAR - img.height) // 2))
    clock = ImageFont.truetype(str(FONTS / 'segoeui.ttf'), 12)
    for text, y in (('8:41 PM', H - BAR + 8), ('10/26/2026', H - BAR + 25)):
        tw = d.textlength(text, font=clock)
        d.text((W - 24 - tw, y), text, font=clock, fill=(255, 255, 255, 235))
    desk.convert('RGB').save(out, quality=92)
    print('wrote', out)
