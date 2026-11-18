"""Side-by-side before/after images of representative pages for the stage-2 report.

    python -I docs/redesign/stage2/before_after.py <before-dir> <after-dir>

Both folders are outputs of website/tests/site_screenshots.py (<view>/<lang>/<theme>/<route>.webp). Writes JPEGs to
docs/redesign/stage2/before-after/ (top part of each page, scaled, labelled).
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "before-after")
PAIRS = [("desktop", "en", "light", "home"), ("desktop", "en", "dark", "home"), ("desktop", "en", "light", "highscores"),
         ("desktop", "de", "dark", "guild"), ("desktop", "en", "light", "fansites"), ("desktop", "de", "light", "downloads"),
         ("mobile", "en", "light", "home"), ("mobile", "de", "dark", "highscores"), ("mobile", "en", "dark", "spells")]


def main():
    before, after = sys.argv[1], sys.argv[2]
    os.makedirs(OUT, exist_ok=True)
    font = ImageFont.truetype("arialbd.ttf", 26)
    done = 0
    for view, lang, theme, route in PAIRS:
        # very tall pages are stored as PNG (WebP's 16,383-px limit)
        a, b = (next((p for p in (os.path.join(d, view, lang, theme, route + e) for e in (".webp", ".png")) if os.path.exists(p)), "")
                for d in (before, after))
        if not (os.path.exists(a) and os.path.exists(b)):
            print("missing", view, lang, theme, route)
            continue
        ims = [Image.open(p).convert("RGB") for p in (a, b)]
        w = 720 if view == "desktop" else 390
        h = 1100 if view == "desktop" else 1500
        ims = [im.resize((w, int(im.height * w / im.width))).crop((0, 0, w, h)) for im in ims]
        sheet = Image.new("RGB", (w * 2 + 24, h + 44), "white")
        d = ImageDraw.Draw(sheet)
        for i, (im, label) in enumerate(zip(ims, ("Before (stage 1)", "After (stage 2)"))):
            x = i * (w + 24)
            d.text((x + 6, 8), label, fill="black", font=font)
            sheet.paste(im, (x, 44))
        name = f"{route}-{view}-{lang}-{theme}.jpg"
        sheet.save(os.path.join(OUT, name), quality=78)
        done += 1
    print(f"{done} before/after images in {OUT}")


if __name__ == "__main__":
    main()
