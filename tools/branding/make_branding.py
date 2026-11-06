# Builds the Tibia Foundry icon and large logo from the official 32x32 logo.
#
#   python -I tools/branding/make_branding.py
#
# Source (master, never modified): src/TibiaFoundry.App/Assets/Branding/tibia-foundry-logo.png
# Outputs (next to it):
#   tibia-foundry.ico          16, 24, 32, 48, 64, 128 and 256 px (Windows application/window icon)
#   tibia-foundry-logo-256.png 8x enlargement for documentation, installers and store-style listings
#
# Every size is made with nearest-neighbor sampling only: each output pixel is an exact copy of
# one source pixel, so the pixel art stays crisp (no blending, no new colors, alpha stays 1-bit).
# For sizes smaller than the source, the sampling phase (which source rows/columns are kept) was
# chosen by eye for legibility; it is fixed here so the output is reproducible:
#   16 px: odd columns, even rows   24 px: center of each 4/3-pixel step
# Sizes up to 48 px are stored as 32-bit DIBs (best compatibility), larger ones as PNG.
# Requires Pillow (only for reading and writing PNG data).
import io, os, struct, sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
BRANDING = os.path.join(ROOT, 'src', 'TibiaFoundry.App', 'Assets', 'Branding')
MASTER = os.path.join(BRANDING, 'tibia-foundry-logo.png')

# size -> (x offset, y offset) of the sampling grid, in source pixels
PHASE = {16: (1.0, 0.0), 24: (0.5, 0.5)}


def nearest(img, size):
    w, h = img.size
    if size % w == 0:                                    # exact integer enlargement
        return img.resize((size, size), Image.NEAREST)
    ox, oy = PHASE.get(size, (0.0, 0.0))
    step = w / size
    out = Image.new('RGBA', (size, size))
    src, dst = img.load(), out.load()
    for y in range(size):
        for x in range(size):
            dst[x, y] = src[min(w - 1, int(x * step + ox)), min(h - 1, int(y * step + oy))]
    return out


def dib(img):
    """32-bit BGRA DIB with the AND mask, as stored in .ico files (height doubled, bottom-up)."""
    w, h = img.size
    header = struct.pack('<IiiHHIIiiII', 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    px = img.load()
    rows = []
    for y in range(h - 1, -1, -1):
        rows.append(b''.join(struct.pack('BBBB', px[x, y][2], px[x, y][1], px[x, y][0], px[x, y][3]) for x in range(w)))
    mask_row = ((w + 31) // 32) * 4
    mask = bytearray()
    for y in range(h - 1, -1, -1):
        bits = bytearray(mask_row)
        for x in range(w):
            if px[x, y][3] == 0:
                bits[x // 8] |= 0x80 >> (x % 8)
        mask += bits
    return header + b''.join(rows) + bytes(mask)


def png(img):
    buf = io.BytesIO()
    img.save(buf, 'PNG', optimize=False)
    return buf.getvalue()


def main():
    master = Image.open(MASTER)
    if master.size != (32, 32) or master.mode != 'RGBA':
        sys.exit(f'unexpected master logo {master.size} {master.mode}')
    sizes = [16, 24, 32, 48, 64, 128, 256]
    images = {s: (master.copy() if s == 32 else nearest(master, s)) for s in sizes}
    entries = [(s, dib(images[s]) if s <= 48 else png(images[s])) for s in sizes]
    out = bytearray(struct.pack('<HHH', 0, 1, len(entries)))
    offset = 6 + 16 * len(entries)
    for s, data in entries:
        out += struct.pack('<BBBBHHII', s % 256, s % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for _, data in entries:
        out += data
    open(os.path.join(BRANDING, 'tibia-foundry.ico'), 'wb').write(bytes(out))
    images[256].save(os.path.join(BRANDING, 'tibia-foundry-logo-256.png'), 'PNG', optimize=False)
    print('wrote', ', '.join(f'{s}px' for s in sizes), 'icon and 256 px logo')


if __name__ == '__main__':
    main()
