"""
Copies the website's screenshots into website/src/images (8.8.0):

    python -I website/src/prepare_images.py <folder with the captures>
    python -I website/src/prepare_images.py <folder with the captures> --language <code>

The second form copies the screenshots of the program in another language (pt-BR, pl, es, de) into
website/src/images/<code>/, which the page of that language uses; with --language en they replace the
English screenshots in website/src/images/. Those captures are made by the program
itself on the demo copy, with paths in the user's folder already blurred:

    TibiaFoundry.exe --site-screenshots <folder> --language <code>

The captures are real window captures (1586x893) of the running program, one per page and theme
(<page>-dark.png and <page>-light.png), taken with a demo data folder under C:\\Users\\Public so that
no personal path is shown. The installed client's folder still contains the Windows user name, so
those text fields are blurred here. The generated outputs (output-*.png) are kept as they are.
"""
import os
import sys

from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "images")

# page -> regions with a client path (x0, y0, x1, y1 in capture pixels; the same in both themes)
PAGES = {
    "home": [], "asset-library": [], "duplicates": [], "content-studio": [], "profiles": [],
    "appearances": [(337, 131, 1258, 157)],
    "extract-sprites": [(362, 131, 1468, 157)],
    "static-data": [(322, 131, 1258, 157)],
    "item-catalog": [],
    "compare": [(332, 129, 1303, 159), (332, 169, 1303, 199)],
    "map-tools": [(322, 131, 1258, 157)],
    "house-images": [(333, 131, 1258, 157)],
    "sounds": [(358, 131, 1258, 157)],
    "assets-editor": [(322, 131, 1054, 157)],
    "rcc-editor": [(322, 131, 1078, 157)],
    "clients": [(390, 449, 760, 473)],
    "settings": [], "settings-attribution": [],
}


def redact(img, boxes):
    img = img.convert("RGB")
    for box in boxes:
        img.paste(img.crop(box).filter(ImageFilter.GaussianBlur(6)), box)
    return img


def main():
    src = sys.argv[1]
    if "--language" in sys.argv:
        code = sys.argv[sys.argv.index("--language") + 1]
        out = OUT if code == "en" else os.path.join(OUT, code)
        os.makedirs(out, exist_ok=True)
        for page in PAGES:
            for theme in ("dark", "light"):
                name = f"{page}-{theme}.png"
                Image.open(os.path.join(src, name)).convert("RGB").save(os.path.join(out, name), optimize=True)
        print(f"{len(PAGES) * 2} screenshots written to {out}")
        return
    for page, boxes in PAGES.items():
        for theme in ("dark", "light"):
            name = f"{page}-{theme}.png"
            redact(Image.open(os.path.join(src, name)), boxes).save(os.path.join(OUT, name), optimize=True)
    print(f"{len(PAGES) * 2} screenshots written to {OUT}")


if __name__ == "__main__":
    main()
