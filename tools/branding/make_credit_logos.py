# Builds the logos shown in About > Credits from the official files of each organization.
#
# Usage: python -I tools/branding/make_credit_logos.py <folder with the downloaded originals>
#
# The originals are checked against the SHA-256 values below, so the output can only be made from
# the files that were reviewed. Tibia and CipSoft logos are smooth artwork, not pixel art: they are
# reduced with Lanczos filtering on premultiplied alpha (no color fringes at transparent edges) to
# about twice their largest on-screen size, keeping the aspect ratio within 0.1 %. The TibiaWiki logo
# is used byte for byte. Requires Pillow.
import hashlib
import os
import shutil
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', '..', 'src', 'TibiaFoundry.App', 'Assets', 'Credits')

# name in the source folder -> (sha256, output name, target, source URL)
SOURCES = {
    'Tibia_Logo.png': ('4ccd20cd28f8d8ec1a4b1094bbe2b0df235b05bc350874e747d3ddfeacc39ee1', 'tibia-logo.png', ('height', 128),
                       'https://nextcloud.cipsoft.com/index.php/s/JQfrYtjAAm2rEN7 (Logos/Tibia_Logo.png)'),
    'logo-cipsoft-h-pos-rgb.png': ('91084db121676c22e814feb8d6a9f21220ad4dc09f929445179c20c7693d8c61', 'cipsoft-logo-light.png', ('width', 436),
                                   'https://nextcloud.cipsoft.com/index.php/s/3q4j2cY7WCscZiA (Logos/logo-cipsoft-h-pos-rgb.png)'),
    'logo-cipsoft-h-neg-rgb.png': ('a34f0567616106792ac16c0c6498ca84e8b24d6498a6f2d455c18bbc7186d5bb', 'cipsoft-logo-dark.png', ('width', 436),
                                   'https://nextcloud.cipsoft.com/index.php/s/3q4j2cY7WCscZiA (Logos/logo-cipsoft-h-neg-rgb.png)'),
    'Site-logo.png': ('117c6e334d0954d8c55ac8df6ae227eeee077bc767911f9ff352960a77fd4dbd', 'tibiawiki-logo.png', None,
                      'https://static.wikia.nocookie.net/tibia/images/e/e6/Site-logo.png (TibiaWiki site logo)'),
}


def best_size(width, height, axis, target):
    """The size near the target whose aspect ratio is closest to the original's."""
    ratio = width / height
    candidates = []
    for t in range(target - 6, target + 7):
        w, h = (t, round(t / ratio)) if axis == 'width' else (round(t * ratio), t)
        candidates.append((abs((w / h) - ratio) / ratio, abs(t - target), w, h))
    error, _, w, h = min(candidates)
    return w, h, error


def main(source_dir):
    os.makedirs(OUT, exist_ok=True)
    for name, (sha, out_name, target, url) in SOURCES.items():
        path = os.path.join(source_dir, name)
        data = open(path, 'rb').read()
        actual = hashlib.sha256(data).hexdigest()
        if actual != sha:
            sys.exit(f'{name}: SHA-256 {actual} does not match the reviewed original {sha}')
        out = os.path.join(OUT, out_name)
        if target is None:
            shutil.copyfile(path, out)
            print(f'{out_name}: copied unchanged from {url}')
            continue
        with Image.open(path) as image:
            image = image.convert('RGBA')
            w, h, error = best_size(image.width, image.height, *target)
            reduced = image.convert('RGBa').resize((w, h), Image.LANCZOS).convert('RGBA')
            reduced.save(out, optimize=True)
            print(f'{out_name}: {image.width}x{image.height} -> {w}x{h} (aspect error {error * 100:.3f} %) from {url}')


if __name__ == '__main__':
    if len(sys.argv) != 2:
        sys.exit(__doc__ or 'usage: make_credit_logos.py <folder with the originals>')
    main(sys.argv[1])
