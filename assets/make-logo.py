"""Renders the Annex logo (picture-in-picture) to app.ico, PNGs and SVGs.

Run from the repo root:  python assets/make-logo.py
Sizes up to 32 px use a simplified mark (no title lines, thicker frame) so it stays crisp in the tray.
"""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
FRAME = (0x89, 0xB4, 0xFA)
GRAD_A, GRAD_B = (0x89, 0xB4, 0xFA), (0x7D, 0x82, 0xD9)

# Geometry on a 64-unit canvas: (x, y, w, h, radius)
FULL = dict(frame=(4, 10, 56, 44, 8), stroke=4, inner=(30, 30, 24, 18, 4), lines=[(10, 16, 14, 3), (10, 22, 9, 3)])
SMALL = dict(frame=(4, 10, 56, 44, 9), stroke=6, inner=(27, 27, 27, 21, 5), lines=[])


def render(size, spec, ss=16):
    s = size * ss / 64
    img = Image.new('RGBA', (size * ss,) * 2, (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    x, y, w, h, r = spec['frame']
    half = spec['stroke'] / 2
    d.rounded_rectangle([(x - half) * s, (y - half) * s, (x + w + half) * s, (y + h + half) * s], radius=(r + half) * s,
                        outline=FRAME, width=round(spec['stroke'] * s))
    for lx, ly, lw, lh in spec['lines']:
        d.rounded_rectangle([lx * s, ly * s, (lx + lw) * s, (ly + lh) * s], radius=lh / 2 * s, fill=FRAME + (140,))
    # gradient-filled inner window
    x, y, w, h, r = spec['inner']
    box = [round(x * s), round(y * s), round((x + w) * s), round((y + h) * s)]
    bw, bh = box[2] - box[0], box[3] - box[1]
    grad = Image.new('RGBA', (bw, bh))
    gp = grad.load()
    for j in range(bh):
        for i in range(bw):
            t = (i / bw + j / bh) / 2
            gp[i, j] = tuple(round(a + (b - a) * t) for a, b in zip(GRAD_A, GRAD_B)) + (255,)
    mask = Image.new('L', (bw, bh), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, bw - 1, bh - 1], radius=r * s, fill=255)
    img.paste(grad, box[:2], mask)
    return img.resize((size, size), Image.LANCZOS)


def svg(spec):
    x, y, w, h, r = spec['frame']
    ix, iy, iw, ih, ir = spec['inner']
    lines = ''.join(f'\n  <rect x="{lx}" y="{ly}" width="{lw}" height="{lh}" rx="{lh / 2:g}" fill="#89B4FA" opacity=".55"/>' for lx, ly, lw, lh in spec['lines'])
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
  <defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#89B4FA"/><stop offset="1" stop-color="#7D82D9"/></linearGradient></defs>
  <rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{r}" fill="none" stroke="#89B4FA" stroke-width="{spec['stroke']}"/>{lines}
  <rect x="{ix}" y="{iy}" width="{iw}" height="{ih}" rx="{ir}" fill="url(#g)"/>
</svg>
'''


if __name__ == '__main__':
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = {n: render(n, SMALL if n <= 32 else FULL) for n in sizes}
    frames[256].save(ROOT / 'app.ico', sizes=[(n, n) for n in sizes], append_images=[frames[n] for n in sizes if n != 256])
    out = ROOT / 'assets'
    render(512, FULL).save(out / 'logo-512.png')
    frames[256].save(out / 'logo-256.png')
    (out / 'logo.svg').write_text(svg(FULL), encoding='utf-8')
    (out / 'logo-small.svg').write_text(svg(SMALL), encoding='utf-8')
    print('wrote app.ico and assets/logo*.{png,svg}')
