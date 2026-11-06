"""
Builds tibiafoundry.com (8.8.0) into website/site/ from website/src/, in every language of LANGUAGES:

    python -I website/build_site.py            (from the repository root)
    python -I website/build_site.py --strict   (fail when a translation is missing)

* src/index.template.html is rendered once per language with the texts of src/locales/<lang>.json:
  English at /, the others at /pt-br/, /pl/, /es/ and /de/ (see website/TRANSLATING.md);
* src/styles.css and src/app.js are inlined into every page;
* every image of src/images is written once to site/img/: screenshots as lossy WebP (quality 80), the program's
  pixel-art output and the logo losslessly, so they stay pixel-exact. A language whose folder src/images/<code>/
  has a screenshot of the program in that language gets its own copy in site/img/<url folder>/; everything else
  (and English) is shared;
* {version} comes from src/Directory.Build.props, {date} is the build date (written in each language's style);
* sitemap.xml lists every language with hreflang alternates; CNAME and robots.txt are kept;
* website/i18n-report.md is the translation completeness report.

The pages load nothing from other hosts except the Google tag (analytics, unchanged: G-J3EX3FB3PB).
Needs Pillow (pip install pillow).
"""
import datetime
import html as htmllib
import io
import json
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "src")
SITE = os.path.join(HERE, "site")
ROOT = os.path.dirname(HERE)
ORIGIN = "https://tibiafoundry.com/"

# (code, URL folder, name in its own language, short badge, Open Graph locale). The first one is the default.
LANGUAGES = [
    ("en", "", "English", "EN", "en_US"),
    ("pt-BR", "pt-br", "Português (Brasil)", "PT", "pt_BR"),
    ("pl", "pl", "Polski", "PL", "pl_PL"),
    ("es", "es", "Español", "ES", "es_LA"),
    ("de", "de", "Deutsch", "DE", "de_DE"),
]
DEFAULT = LANGUAGES[0][0]

# Screenshot gallery: (id, page name in the program). The program is in English, so its page names stay English.
SCREENS = [
    ("home", "Home"), ("asset-library", "Asset Library"), ("duplicates", "Asset Library – Duplicates"),
    ("content-studio", "Content Studio"), ("profiles", "Content Studio – Publishing profiles"),
    ("appearances", "Appearances"), ("extract-sprites", "Extract Sprites"), ("static-data", "Static Data"),
    ("item-catalog", "Item Catalog"), ("compare", "Compare Files"), ("map-tools", "Map Tools"),
    ("house-images", "House Images"), ("sounds", "Sounds"), ("assets-editor", "Assets Editor"),
    ("rcc-editor", "RCC Editor"), ("clients", "Clients"), ("settings", "Settings"),
    ("settings-attribution", "Settings – Attribution"),
]

# Derived images: (name, source, crop box) – a close-up of the library so the page never shows one picture twice.
DERIVED = [
    ("asset-library-detail-dark", "asset-library-dark.png", (480, 250, 1572, 845)),
    ("asset-library-detail-light", "asset-library-light.png", (480, 250, 1572, 845)),
]

MONTHS = {
    "en": "January February March April May June July August September October November December",
    "pt-BR": "janeiro fevereiro março abril maio junho julho agosto setembro outubro novembro dezembro",
    "pl": "stycznia lutego marca kwietnia maja czerwca lipca sierpnia września października listopada grudnia",
    "es": "enero febrero marzo abril mayo junio julio agosto septiembre octubre noviembre diciembre",
    "de": "Januar Februar März April Mai Juni Juli August September Oktober November Dezember",
}
# Digit-group separator for 1000 and up (CLDR; Polish and Spanish group with a no-break space).
GROUP = {"en": ",", "pt-BR": ".", "pl": " ", "es": " ", "de": "."}


def version():
    props = open(os.path.join(ROOT, "src", "Directory.Build.props"), encoding="utf-8").read()
    return re.search(r"<Version>([^<]+)</Version>", props).group(1)


# ============================================================================ localization
def plural_form(lang, n):
    """CLDR cardinal plural category of the integer n."""
    if lang == "pl":
        if n == 1:
            return "one"
        if 2 <= n % 10 <= 4 and not 12 <= n % 100 <= 14:
            return "few"
        return "many"
    if lang == "pt-BR":
        return "one" if n in (0, 1) else "other"
    return "one" if n == 1 else "other"


def format_number(lang, n):
    s = f"{n:,}"
    # Polish and Spanish leave four-digit numbers ungrouped.
    if lang in ("pl", "es") and n < 10000:
        s = str(n)
    return s.replace(",", GROUP[lang])


def format_date(lang, d):
    month = MONTHS[lang].split()[d.month - 1]
    return {
        "en": f"{month} {d.day}, {d.year}",
        "pt-BR": f"{d.day} de {month} de {d.year}",
        "pl": f"{d.day} {month} {d.year}",
        "es": f"{d.day} de {month} de {d.year}",
        "de": f"{d.day}. {month} {d.year}",
    }[lang]


def flatten(tree, prefix=""):
    out = {}
    for key, value in tree.items():
        if key.startswith("_"):
            continue
        full = prefix + key
        # A plural entry is an object of CLDR categories ("one", "few", "many", "other").
        if isinstance(value, dict) and not (prefix == "plural." and set(value) <= {"zero", "one", "two", "few", "many", "other"}):
            out.update(flatten(value, full + "."))
        else:
            out[full] = value
    return out


def load_locales():
    locales, meta = {}, {}
    for code, *_ in LANGUAGES:
        path = os.path.join(SRC, "locales", code + ".json")
        tree = json.load(open(path, encoding="utf-8"))
        meta[code] = tree.get("_meta", {})
        locales[code] = flatten(tree)
    return locales, meta


TAG = re.compile(r"</?[a-zA-Z][^>]*>")
CODE = re.compile(r"<code[^>]*>(.*?)</code>", re.S)
VARIABLE = re.compile(r"\{(?:version|date|count|page|language|num:\d+|plural:\w+:\d+)\}")


def signature(value):
    """What a translation must keep exactly: its markup, its code and its placeholders."""
    if isinstance(value, dict):
        return ("plural", sorted(set(VARIABLE.findall(" ".join(value.values())))))
    return (sorted(TAG.findall(value)), CODE.findall(value), sorted(VARIABLE.findall(value)))


class Translator:
    """Texts of one language with English fallback; records what it used and what it had to borrow."""

    def __init__(self, code, locales, ver, date):
        self.code, self.locales, self.ver, self.date = code, locales, ver, date
        self.used, self.missing = set(), set()

    def raw(self, key):
        self.used.add(key)
        own = self.locales[self.code]
        if key in own and own[key] not in ("", None):
            return own[key], self.code
        if key not in self.locales[DEFAULT]:
            sys.exit(f"Unknown text key '{key}' (add it to src/locales/{DEFAULT}.json).")
        if self.code != DEFAULT:
            self.missing.add(key)
        return self.locales[DEFAULT][key], DEFAULT

    def plural(self, name, n, **vars):
        forms, lang = self.raw("plural." + name)
        form = plural_form(lang, n)
        text = forms.get(form) or forms.get("other") or forms.get("many")
        return self.interpolate(text, lang, count=format_number(lang, n), **vars)

    def interpolate(self, text, lang, **vars):
        def sub(m):
            token = m.group(0)[1:-1]
            if token.startswith("num:"):
                return format_number(lang, int(token[4:]))
            if token.startswith("plural:"):
                _, name, n = token.split(":")
                return self.plural(name, int(n))
            if token == "version":
                return self.ver
            if token == "date":
                return format_date(lang, self.date)
            return str(vars.get(token, m.group(0)))
        return VARIABLE.sub(sub, text)

    def text(self, key, **vars):
        value, lang = self.raw(key)
        return self.interpolate(value, lang, **vars)

    def attr(self, key, **vars):
        return htmllib.escape(TAG.sub("", htmllib.unescape(self.text(key, **vars))), quote=True)


# ============================================================================ images
def encode(image, name):
    buf = io.BytesIO()
    if name == "logo":
        # The 32×32 pixel-art logo, enlarged 2× with nearest neighbour (sharp header icon).
        image.resize((image.width * 2, image.height * 2), Image.NEAREST).save(buf, "PNG", optimize=True)
        return "png", buf.getvalue()
    if name.startswith("output-"):
        image.save(buf, "WEBP", lossless=True, method=6)
        return "webp", buf.getvalue()
    image.convert("RGB").save(buf, "WEBP", quality=80, method=6)
    return "webp", buf.getvalue()


def source(name, code):
    """The source file of an image for a language: src/images/<code>/<file> when it exists, else src/images/<file>.
    Returns (path, localized)."""
    file = next((src for derived, src, _ in DERIVED if derived == name), name + ".png")
    localized = os.path.join(SRC, "images", code, file)
    if code != DEFAULT and os.path.exists(localized):
        return localized, True
    return os.path.join(SRC, "images", file), False


def load(name, code=DEFAULT):
    path, _ = source(name, code)
    image = Image.open(path)
    for derived, _, box in DERIVED:
        if derived == name:
            return image.crop(box)
    return image


def minify_css(css):
    css = re.sub(r"/\*.*?\*/", "", css, flags=re.S)
    css = re.sub(r"\s*\n\s*", "\n", css)
    return css.strip()


def font(size, bold=False):
    # Segoe UI on Windows; Pillow's own font elsewhere.
    for name in (("segoeuib.ttf", "arialbd.ttf") if bold else ("segoeui.ttf", "arial.ttf")):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default(size)


def scaled_logo(size):
    logo = Image.open(os.path.join(SRC, "images", "logo.png")).convert("RGBA")
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    n = size // logo.width * logo.width
    canvas.alpha_composite(logo.resize((n, n), Image.NEAREST), ((size - n) // 2, (size - n) // 2))
    return canvas


def write_icons():
    """Favicons as real files (search engines do not use data URIs)."""
    scaled_logo(48).save(os.path.join(SITE, "favicon.ico"), sizes=[(16, 16), (32, 32), (48, 48)])
    scaled_logo(192).save(os.path.join(SITE, "favicon-192.png"), optimize=True)
    touch = Image.new("RGBA", (180, 180), (15, 18, 22, 255))
    touch.alpha_composite(scaled_logo(160), (10, 10))
    touch.convert("RGB").save(os.path.join(SITE, "apple-touch-icon.png"), optimize=True)


def write_preview(tr, filename):
    """The 1200x630 link-preview image of one language."""
    og = Image.new("RGB", (1200, 630), (15, 18, 22))
    shot = load("asset-library-dark").convert("RGB")
    shot = shot.resize((760, int(shot.height * 760 / shot.width)), Image.LANCZOS)
    og.paste(shot, (470, 120))
    d = ImageDraw.Draw(og)
    og.paste(scaled_logo(96).convert("RGB"), (56, 64), scaled_logo(96))
    d.text((56, 190), "Tibia Foundry", font=font(54, True), fill=(240, 242, 245))
    d.text((58, 258), tr.text("ogImage.version"), font=font(24), fill=(232, 176, 72))
    y = 320
    for n in range(1, 5):
        line, size = "• " + tr.text(f"ogImage.line{n}"), 25
        while d.textlength(line, font=font(size)) > 400 and size > 18:
            size -= 1
        d.text((58, y), line, font=font(size), fill=(200, 205, 212))
        y += 44
    d.text((58, 560), "tibiafoundry.com", font=font(22, True), fill=(120, 170, 255))
    og.save(os.path.join(SITE, filename), optimize=True)


# ============================================================================ pages
def page_url(folder):
    return ORIGIN + (folder + "/" if folder else "")


def relative(from_folder, to_folder):
    up = "../" if from_folder else ""
    return up + (to_folder + "/" if to_folder else "") if (up or to_folder) else "./"


def og_name(folder):
    return f"og-image-{folder}.png" if folder else "og-image.png"


def render_screens(tr, img):
    tabs, panels = [], []
    for i, (sid, page) in enumerate(SCREENS):
        on = i == 0
        label = tr.text(f"screens.tab.{sid}")
        tabs.append(f'      <button class="tab" role="tab" id="t-{sid}" aria-controls="p-{sid}" aria-selected="{str(on).lower()}"'
                    + ("" if on else ' tabindex="-1"') + f">{label}</button>")
        cap_attr, cap = tr.attr(f"screens.caption.{sid}"), tr.text(f"screens.caption.{sid}")
        dark, light = tr.attr("screens.altDark", page=page), tr.attr("screens.altLight", page=page)
        panels.append(
            f'    <div class="tab-panel" role="tabpanel" id="p-{sid}" aria-labelledby="t-{sid}"' + ("" if on else " hidden") + ">\n"
            f'      <button class="shot-btn themed" type="button" data-zoom="{cap_attr}"><div class="window">'
            f'<img class="shot-dark" src="{img(sid + "-dark")}" alt="{dark}" width="1586" height="893" loading="lazy" decoding="async">'
            f'<img class="shot-light" src="{img(sid + "-light")}" alt="{light}" width="1586" height="893" loading="lazy" decoding="async"></div></button>\n'
            f"      <p>{cap}</p>\n    </div>")
    return (f'    <div class="tabs" role="tablist" aria-label="{tr.attr("screens.tablist")}">\n' + "\n".join(tabs)
            + "\n    </div>\n" + "\n".join(panels))


def render_page(template, css, js, code, folder, tr, translators, img, ver):
    current = next(l for l in LANGUAGES if l[0] == code)
    alternates = [f'<link rel="alternate" hreflang="{c}" href="{page_url(f)}">' for c, f, *_ in LANGUAGES]
    alternates.append(f'<link rel="alternate" hreflang="x-default" href="{page_url("")}">')
    menu, footer = [], []
    for c, f, name, badge, _ in LANGUAGES:
        here = ' aria-current="true"' if c == code else ""
        href = relative(folder, f)
        menu.append(f'          <li><a href="{href}" hreflang="{c}" lang="{c}" data-lang="{c}"{here}>'
                    f'<span class="lang-badge" aria-hidden="true">{badge}</span> {name}</a></li>')
        footer.append(f'          <li><a href="{href}" hreflang="{c}" lang="{c}" data-lang="{c}"{here}>{name}</a></li>')

    # Texts the script needs at run time, plus every language's offer line for visitors whose browser prefers it.
    runtime = {
        "lang": code, "default": DEFAULT,
        "strings": {k[3:]: tr.text(k) for k in sorted(translators[DEFAULT].locales[DEFAULT]) if k.startswith("js.")},
        "fallback": {k[3:]: translators[DEFAULT].text(k) for k in sorted(translators[DEFAULT].locales[DEFAULT]) if k.startswith("js.")},
        "languages": [{"code": c, "path": relative(folder, f), "name": n,
                       "offer": {"text": translators[c].text("js.suggestText"), "go": translators[c].text("js.suggestGo"),
                                 "close": translators[c].text("js.suggestClose")}} for c, f, n, *_ in LANGUAGES],
    }
    runtime_js = "window.TF_I18N = " + json.dumps(runtime, ensure_ascii=False, separators=(",", ":")).replace("</", "<\\/") + ";"

    redirect = ""
    if code == DEFAULT:
        paths = {c: f + "/" for c, f, *_ in LANGUAGES if f}
        redirect = ("  // A visitor who picked another language before is taken there from the English page; search engines keep no\n"
                    "  // storage, so they always see this page. ?lang=en opens English regardless.\n"
                    "  try { var l = localStorage.getItem('tf-lang'), P = " + json.dumps(paths) + ";\n"
                    "    if (P[l] && !/[?&]lang=en\\b/.test(location.search)) {\n"
                    "      var p = location.pathname.replace(/index\\.html$/, '');\n"
                    "      location.replace(p + P[l] + (location.protocol === 'file:' ? 'index.html' : '') + location.search + location.hash);\n"
                    "    } } catch (e) {}")

    page = template
    fixed = {
        "LANG": code, "LANG_CODE": current[3], "URL": page_url(folder), "OG_IMAGE": ORIGIN + og_name(folder),
        "OG_LOCALE": current[4],
        "OG_LOCALE_ALTERNATES": "\n".join(f'<meta property="og:locale:alternate" content="{l[4]}">' for l in LANGUAGES if l[0] != code),
        "HREFLANG": "\n".join(alternates), "LANG_MENU": "\n".join(menu), "LANG_FOOTER": "\n".join(footer),
        "LANG_REDIRECT": redirect, "SCREENS": render_screens(tr, img), "VERSION": ver,
    }
    for name, value in fixed.items():
        page = page.replace("{{" + name + "}}", value)
    page = re.sub(r"\{\{t:([\w.-]+)\}\}", lambda m: tr.text(m.group(1)), page)
    page = re.sub(r"\{\{a:([\w.-]+)\}\}", lambda m: tr.attr(m.group(1)), page)
    page = re.sub(r"\{\{j:([\w.-]+)\}\}", lambda m: json.dumps(TAG.sub("", tr.text(m.group(1))), ensure_ascii=False)[1:-1].replace("</", "<\\/"), page)
    page = re.sub(r"\{\{IMG:([\w-]+)\}\}", lambda m: img(m.group(1)), page)
    # CSS and script last: their own braces are never read as placeholders.
    return page.replace("{{CSS}}", css).replace("{{I18N}}", runtime_js).replace("{{JS}}", js)


def check_page(code, page):
    left = re.findall(r"\{\{[^}]+\}\}", page)
    if left:
        sys.exit(f"[{code}] Unreplaced placeholders: " + ", ".join(sorted(set(left))))
    if page.count("googletagmanager.com/gtag/js") != 1:
        sys.exit(f"[{code}] The page must contain exactly one Google tag.")
    # Nothing else may load from another host (links to other sites are fine).
    loads = re.findall(r'<(?:script|img|iframe|source)\b[^>]*\bsrc="(https?://[^"]+)"', page)
    loads += re.findall(r'<link\b[^>]*\brel="(?:stylesheet|icon|preload|modulepreload)"[^>]*\bhref="(https?://[^"]+)"', page)
    loads = [u for u in loads if not u.startswith("https://www.googletagmanager.com/gtag/js")]
    if loads:
        sys.exit(f"[{code}] External resources are not allowed: " + ", ".join(loads))


def validate(locales):
    """Every translation keeps the English markup, code and placeholders; returns the list of problems."""
    problems, english = [], locales[DEFAULT]
    for code, texts in locales.items():
        for key, value in texts.items():
            if key not in english:
                problems.append(f"[{code}] '{key}' is not an English key (typo or removed text)")
            elif type(value) is not type(english[key]):
                problems.append(f"[{code}] '{key}' must be a {'plural object' if isinstance(english[key], dict) else 'string'}")
            elif signature(value) != signature(english[key]):
                problems.append(f"[{code}] '{key}' changes markup, code or placeholders: {signature(value)} vs English {signature(english[key])}")
    return problems


def write_report(locales, meta, translators, ver, today):
    english = locales[DEFAULT]
    used = set().union(*(t.used for t in translators.values()))
    unused = sorted(set(english) - used)
    lines = [f"# Translation completeness – tibiafoundry.com {ver}", "",
             f"Generated by `website/build_site.py` on {today}. Source language: English (`src/locales/en.json`, {len(english)} texts).", "",
             "| Language | File | Translated | Missing (English shown) | Same as English: intended | Same as English: to review | Complete |",
             "|---|---|---:|---:|---:|---:|---:|"]
    review = {}
    for code, folder, name, *_ in LANGUAGES:
        texts = locales[code]
        translated = [k for k in english if k in texts and texts[k] not in ("", None)]
        missing = [k for k in english if k not in translated]
        intended = set(meta[code].get("sameAsEnglish", []))
        same = [k for k in translated if texts[k] == english[k]] if code != DEFAULT else []
        review[code] = [k for k in same if k not in intended]
        pct = 100.0 * len(translated) / len(english)
        lines.append(f"| {name} (`{code}`) | `src/locales/{code}.json` | {len(translated)} | {len(missing)} | "
                     f"{len([k for k in same if k in intended])} | {len(review[code])} | {pct:.1f} % |")
    lines += ["", "*Missing* texts fall back to English on the page and are listed by the build. *Same as English* texts are",
              "names and technical terms kept on purpose; each locale lists them under `_meta.sameAsEnglish`.", ""]
    for code, folder, name, *_ in LANGUAGES[1:]:
        missing = [k for k in english if k not in locales[code] or locales[code][k] in ("", None)]
        if missing or review[code]:
            lines.append(f"## {name}")
            lines += [f"- missing: `{k}`" for k in missing] + [f"- same as English, not marked as intended: `{k}`" for k in review[code]]
            lines.append("")
    if unused:
        lines += ["## Texts not used by the page", ""] + [f"- `{k}`" for k in unused] + [""]
    open(os.path.join(HERE, "i18n-report.md"), "w", encoding="utf-8", newline="\n").write("\n".join(lines))
    return review, unused


def main():
    strict = "--strict" in sys.argv[1:]
    ver = version()
    today = datetime.date.today()
    locales, meta = load_locales()
    problems = validate(locales)
    if problems:
        sys.exit("Translation errors:\n  " + "\n  ".join(problems))

    template = open(os.path.join(SRC, "index.template.html"), encoding="utf-8").read()
    css = minify_css(open(os.path.join(SRC, "styles.css"), encoding="utf-8").read())
    js = open(os.path.join(SRC, "app.js"), encoding="utf-8").read().replace("__VERSION__", ver)
    if "</script" in js.lower() or "</style" in css.lower():
        sys.exit("The script or style sheet would close its own element.")

    os.makedirs(os.path.join(SITE, "img"), exist_ok=True)
    files = {}
    translators = {code: Translator(code, locales, ver, today) for code, *_ in LANGUAGES}
    for code, folder, *_ in LANGUAGES:
        def img(name, folder=folder, code=code):
            localized = source(name, code)[1]
            key = (folder if localized else "", name)
            if key not in files:
                ext, data = encode(load(name, code if localized else DEFAULT), name)
                files[key] = (f"img/{key[0] + '/' if key[0] else ''}{name}.{ext}", data)
                os.makedirs(os.path.dirname(os.path.join(SITE, files[key][0])), exist_ok=True)
                with open(os.path.join(SITE, files[key][0]), "wb") as f:
                    f.write(data)
            return ("../" if folder else "") + files[key][0]
        page = render_page(template, css, js, code, folder, translators[code], translators, img, ver)
        check_page(code, page)
        out = os.path.join(SITE, folder) if folder else SITE
        os.makedirs(out, exist_ok=True)
        with open(os.path.join(out, "index.html"), "w", encoding="utf-8", newline="\n") as f:
            f.write(page)
        write_preview(translators[code], og_name(folder))
        print(f"site/{folder + '/' if folder else ''}index.html: {len(page.encode('utf-8')) / 1024:,.0f} KB ({code})")

    # /en/ is the English page's language URL: it remembers the choice and opens /.
    os.makedirs(os.path.join(SITE, "en"), exist_ok=True)
    with open(os.path.join(SITE, "en", "index.html"), "w", encoding="utf-8", newline="\n") as f:
        f.write('<!doctype html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n<title>Tibia Foundry</title>\n'
                f'<link rel="canonical" href="{ORIGIN}">\n<meta name="robots" content="noindex">\n'
                '<meta http-equiv="refresh" content="0; url=../?lang=en">\n'
                "<script>try { localStorage.setItem('tf-lang', 'en'); } catch (e) {}\n"
                "location.replace('../' + (location.protocol === 'file:' ? 'index.html' : '') + '?lang=en' + location.hash);</script>\n"
                '</head>\n<body><a href="../">Tibia Foundry</a></body>\n</html>\n')

    # Images of earlier builds that no page uses any more.
    keep = {os.path.normpath(os.path.join(SITE, p)) for p, _ in files.values()}
    for folder, dirs, names in os.walk(os.path.join(SITE, "img"), topdown=False):
        for name in names:
            if os.path.normpath(os.path.join(folder, name)) not in keep:
                os.remove(os.path.join(folder, name))
        if folder != os.path.join(SITE, "img") and not os.listdir(folder):
            os.rmdir(folder)

    with open(os.path.join(SITE, "sitemap.xml"), "w", encoding="utf-8", newline="\n") as f:
        f.write('<?xml version="1.0" encoding="UTF-8"?>\n'
                '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">\n')
        for _, folder, *_ in LANGUAGES:
            f.write(f"  <url>\n    <loc>{page_url(folder)}</loc>\n    <lastmod>{today.isoformat()}</lastmod>\n")
            for c, other, *_ in LANGUAGES:
                f.write(f'    <xhtml:link rel="alternate" hreflang="{c}" href="{page_url(other)}"/>\n')
            f.write(f'    <xhtml:link rel="alternate" hreflang="x-default" href="{page_url("")}"/>\n  </url>\n')
        f.write("</urlset>\n")
    write_icons()
    for name, content in (("CNAME", "tibiafoundry.com\n"), ("robots.txt", "User-agent: *\nAllow: /\n\nSitemap: https://tibiafoundry.com/sitemap.xml\n")):
        path = os.path.join(SITE, name)
        if not os.path.exists(path):
            open(path, "w", encoding="utf-8", newline="\n").write(content)

    review, unused = write_report(locales, meta, translators, ver, today.isoformat())
    total = sum(len(d) for _, d in files.values())
    print(f"site/img: {len(files)} images, {total / 1024:,.0f} KB; version {ver}, {today.isoformat()}")
    missing = {c: sorted(t.missing) for c, t in translators.items() if t.missing}
    for code, keys in missing.items():
        print(f"  [{code}] {len(keys)} missing, shown in English: " + ", ".join(keys))
    for code, keys in review.items():
        if keys:
            print(f"  [{code}] same as English, not marked as intended: " + ", ".join(keys))
    if unused:
        print("  unused texts: " + ", ".join(unused))
    print("Report: website/i18n-report.md")
    if strict and (missing or unused):
        sys.exit("--strict: translations are incomplete.")


if __name__ == "__main__":
    main()
