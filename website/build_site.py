"""
Builds tibiafoundry.com (version from src/Directory.Build.props) into website/site/ from website/src/:

    python -I website/build_site.py                       (from the repository root)
    python -I website/build_site.py --strict              (fail when a translation is missing or a text is unused)
    python -I website/build_site.py --checksums Release/9.0.5/SHA256SUMS.txt   (show the archives' SHA-256 on Downloads)

* src/registry.json is the site's structure: languages, pages, Tibia data routes, downloads, the creator's profiles;
  src/providers.json lists every external data provider (rendered as the footer's "Data & resource credits");
* every page of src/registry.json "pages" is rendered from src/templates/<page>.html inside src/templates/layout.html,
  once per language with the texts of src/locales/<lang>.json: English at /<page>/, the others at /<lang>/<page>/;
* src/css/ (cascade layers, bundled and minified into one site.css) and src/js/*.js are published under site/assets/ with a content hash in their URL (cache busting);
* screenshots come from the library src/images/screens/<code>/<id>-<code>-<theme>.png (website/screenshots/
  screenshots.py captures them from the running program) and are written as WebP to site/img/screens/; every language
  shows its own screenshots, a missing one stops the build;
* {app:Key} in a text is the program's own translation of that key (Strings.<code>.xaml), so page names on the site
  match the screenshots; {version} comes from src/Directory.Build.props, {date} is the build date;
* sitemap.xml lists every page in every language with hreflang alternates; 404.html sends old and deep data URLs
  (e.g. /tibia/character/Name) to their view; CNAME and robots.txt are never written when they exist;
* website/i18n-report.md is the translation completeness report;
* src/ui-icons.svg (interface icons: a Lucide subset, ISC licence, written by website/art/icons.py) is inlined in every page;
* src/images/pixel/*.svg (the site's own pixel ornaments, drawn by website/art/pixelart.py) is copied to site/img/pixel/;
* calculators: a page with "calc" (formula ids of src/formulas.json) loads formulas.js and calc.js and gets the calc.*
  texts at run time; {{CALC_HOW:<id>}} renders a formula's rule, sources and "checked on" date from src/formulas.json,
  {{CALC_CARDS}} the hub's card per calculator page;
* registry.json "menu" is the header menu (desktop panels, {{NAV}}), the phone drawer ({{MENU_NAV}}) and the footer's link
  columns ({{FOOTER_MENU}}, {{FOOTER_MENU_LINKS}}); --strict fails on an unknown page or data route in it;
* --checksums also reads the archives' sizes when the files sit next to the checksum file, and the release date is the
  date of the version's heading in CHANGELOG.md (both are shown on the Downloads page when known).

The pages load nothing from other hosts except the Google tag (unchanged: G-J3EX3FB3PB) and, at run time, the
TibiaData API and images on static.tibia.com (credited in the footer). Needs Pillow (pip install pillow).
"""
import datetime
import hashlib
import html as htmllib
import io
import json
import os
import re
import shutil
import sys
import urllib.parse

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "src")
SITE = os.path.join(HERE, "site")
ROOT = os.path.dirname(HERE)

REGISTRY = json.load(open(os.path.join(SRC, "registry.json"), encoding="utf-8"))
PROVIDERS = json.load(open(os.path.join(SRC, "providers.json"), encoding="utf-8"))["providers"]
ORIGIN = REGISTRY["origin"]

# (code, URL folder, name in its own language, short badge, Open Graph locale) - kept in this form for screenshots.py.
LANGUAGES = [(l["code"], l["folder"], l["name"], l["badge"], l["ogLocale"]) for l in REGISTRY["languages"]]
INTL = {l["code"]: l["intl"] for l in REGISTRY["languages"]}
DEFAULT = LANGUAGES[0][0]
PAGES = REGISTRY["pages"]
# The formula registry of the calculators (sources, dates and test values; checked by check_formulas under --strict).
FORMULA_REGISTRY = {f["id"]: f for f in json.load(open(os.path.join(SRC, "formulas.json"), encoding="utf-8"))["formulas"]}
CALC_PAGES = [p for p in PAGES if p.get("calc")]

# Screenshot gallery (Features page): (id, the program's page as keys of its own texts, unused). The page name (alt
# text) is the program's translation of those keys joined with " → "; the tab label is the last of them. Each id is a
# shot of SiteScreenshots.cs in the program.
SCREENS = [
    ("home", ["Tool.home.Name"], None),
    ("asset-library", ["Tool.asset-library.Name"], None),
    ("duplicates", ["Tool.asset-library.Name", "AssetLibrary.Duplicates"], None),
    ("content-studio", ["Tool.content-studio.Name"], None),
    ("profiles", ["Tool.content-studio.Name", "ContentStudio.PublishingProfiles"], None),
    ("appearances", ["Tool.appearances.Name"], None),
    ("infobox", ["Tool.appearances.Name", "Infobox.Tab"], None),
    ("extract-sprites", ["Tool.sprite-extractor.Name"], None),
    ("static-data", ["Tool.static-data.Name", "StaticData.Monsters"], None),
    ("item-catalog", ["Tool.item-catalog.Name"], None),
    ("compare", ["Tool.compare.Name"], None),
    ("map-tools", ["Tool.map-tools.Name"], None),
    ("house-images", ["Tool.house-images.Name"], None),
    ("sounds", ["Tool.sounds.Name"], None),
    ("assets-editor", ["Tool.assets-editor.Name"], None),
    ("rcc-editor", ["Tool.rcc-editor.Name"], None),
    ("clients", ["Tool.clients.Name"], None),
    ("settings", ["Tool.settings.Name"], None),
    ("settings-attribution", ["Tool.settings.Name", "Choice.SettingsCategory.Attribution"], None),
    ("about", ["Tool.about.Name"], None),
]
SCREEN_SIZE = (1280, 800)
SHOTS = os.path.join(SRC, "images", "screens")
APP_STRINGS = os.path.join(ROOT, "src", "TibiaFoundry.Core", "Localization", "Resources", "Strings.{code}.xaml")
APP_LINK_ICONS = os.path.join(ROOT, "src", "TibiaFoundry.App", "Assets", "Links")
DERIVED = []          # (name, source, crop box): none since 9.0.5 (kept for screenshots.py)
SHARED = ("logo", "output-")
PROTECTED = ("CNAME", "robots.txt")   # never written when they exist (the Pages repository's own files)
# Interface icons (Lucide subset, inline sprite written by art/icons.py); included once per page.
UI_ICONS = open(os.path.join(SRC, "ui-icons.svg"), encoding="utf-8").read().strip()

MONTHS = {
    "en": "January February March April May June July August September October November December",
    "pt-BR": "janeiro fevereiro março abril maio junho julho agosto setembro outubro novembro dezembro",
    "pl": "stycznia lutego marca kwietnia maja czerwca lipca sierpnia września października listopada grudnia",
    "es": "enero febrero marzo abril mayo junio julio agosto septiembre octubre noviembre diciembre",
    "de": "Januar Februar März April Mai Juni Juli August September Oktober November Dezember",
}
GROUP = {"en": ",", "pt-BR": ".", "pl": " ", "es": " ", "de": "."}


def version():
    props = open(os.path.join(ROOT, "src", "Directory.Build.props"), encoding="utf-8").read()
    return re.search(r"<Version>([^<]+)</Version>", props).group(1)


# ============================================================================ localization
def plural_form(lang, n):
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
    if lang in ("pl", "es") and n < 10000:
        s = str(n)
    return s.replace(",", GROUP[lang])


def format_date(lang, d):
    month = MONTHS[lang].split()[d.month - 1]
    return {"en": f"{month} {d.day}, {d.year}", "pt-BR": f"{d.day} de {month} de {d.year}", "pl": f"{d.day} {month} {d.year}",
            "es": f"{d.day} de {month} de {d.year}", "de": f"{d.day}. {month} {d.year}"}[lang]


DECIMAL = {"en": ".", "pt-BR": ",", "pl": ",", "es": ",", "de": ","}


def format_size(lang, n):
    """Archive sizes as MB (10^6 bytes, as GitHub shows them) with one decimal."""
    return f"{n / 1_000_000:.1f}".replace(".", DECIMAL[lang]) + " MB"


def release_date(ver):
    """The date of the version's heading in CHANGELOG.md ("## 9.0.5 – 2026-11-17 ..."), or None."""
    path = os.path.join(ROOT, "CHANGELOG.md")
    if not os.path.exists(path):
        return None
    m = re.search(r"^## " + re.escape(ver) + r"\s+\S\s+(\d{4})-(\d{2})-(\d{2})", open(path, encoding="utf-8").read(), re.M)
    return datetime.date(int(m.group(1)), int(m.group(2)), int(m.group(3))) if m else None


def flatten(tree, prefix=""):
    out = {}
    for key, value in tree.items():
        if key.startswith("_"):
            continue
        full = prefix + key
        if isinstance(value, dict) and not (prefix == "plural." and set(value) <= {"zero", "one", "two", "few", "many", "other"}):
            out.update(flatten(value, full + "."))
        else:
            out[full] = value
    return out


def load_locales():
    locales, meta = {}, {}
    for code, *_ in LANGUAGES:
        tree = json.load(open(os.path.join(SRC, "locales", code + ".json"), encoding="utf-8"))
        meta[code] = tree.get("_meta", {})
        locales[code] = flatten(tree)
    return locales, meta


TAG = re.compile(r"</?[a-zA-Z][^>]*>")
CODE = re.compile(r"<code[^>]*>(.*?)</code>", re.S)
BUILD_VARIABLE = re.compile(r"\{(?:version|date|count|page|language|num:\d+|plural:\w+:\d+|app:[\w.-]+)\}")
PLACEHOLDER = re.compile(r"\{[\w.:-]+\}")
_app_cache = {}


def app_strings(code):
    if code not in _app_cache:
        texts = {}
        for c in (DEFAULT, code):
            path = APP_STRINGS.replace("{code}", c)
            if os.path.exists(path):
                xaml = open(path, encoding="utf-8-sig").read()
                texts.update({k: htmllib.unescape(v) for k, v in re.findall(r'<sys:String x:Key="([^"]+)"[^>]*>([^<]*)</sys:String>', xaml)})
        _app_cache[code] = texts
    return _app_cache[code]


def app_text(code, key):
    texts = app_strings(code)
    if key not in texts:
        sys.exit(f"Unknown program text '{key}' (not in {APP_STRINGS.replace('{code}', DEFAULT)}).")
    return texts[key]


def signature(value):
    """What a translation must keep exactly: its markup, its code and its placeholders ({app:...} may be written out)."""
    if isinstance(value, dict):
        return ("plural", sorted(set(PLACEHOLDER.findall(" ".join(value.values())))))
    return (sorted(TAG.findall(value)), CODE.findall(value), sorted(v for v in PLACEHOLDER.findall(value) if not v.startswith("{app:")))


class Translator:
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
        text = forms.get(plural_form(lang, n)) or forms.get("other") or forms.get("many")
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
            if token.startswith("app:"):
                return htmllib.escape(app_text(self.code, token[4:]), quote=False)
            return str(vars.get(token, m.group(0)))
        text = BUILD_VARIABLE.sub(sub, text)
        for name, value in vars.items():
            text = text.replace("{" + name + "}", str(value))
        return text

    def text(self, key, **vars):
        value, lang = self.raw(key)
        return self.interpolate(value, lang, **vars)

    def runtime_text(self, key):
        """A text for the page scripts: only {version}, {app:...}, {num:...} and {plural:...} are filled in at build time;
        {date}, {page}, {count}, {language} and the other placeholders stay for the script (TF.t) to fill."""
        value, lang = self.raw(key)
        kept = []

        def protect(m):
            if m.group(0)[1:-1] in ("date", "page", "count", "language"):
                kept.append(m.group(0))
                return "@@RUNTIME%d@@" % (len(kept) - 1)
            return m.group(0)

        text = self.interpolate(re.sub(r"\{\w+\}", protect, value), lang)
        for i, token in enumerate(kept):
            text = text.replace("@@RUNTIME%d@@" % i, token)
        return text

    def app(self, key):
        return app_text(self.code, key)

    def attr(self, key, **vars):
        return htmllib.escape(TAG.sub("", htmllib.unescape(self.text(key, **vars))), quote=True)


# ============================================================================ images
def screenshot(name):
    if name.startswith(SHARED) or not re.search(r"-(dark|light)$", name):
        return None
    return tuple(name.rsplit("-", 1))


def source(name, code):
    if not screenshot(name):
        return os.path.join(SRC, "images", name + ".png")
    sid, theme = screenshot(name)
    path = os.path.join(SHOTS, code, f"{sid}-{code}-{theme}.png")
    if not os.path.exists(path):
        sys.exit(f"[{code}] Missing screenshot {os.path.relpath(path, ROOT)}. Capture it with: python -I "
                 f"website/screenshots/screenshots.py capture --pages {sid} --languages {code} --themes {theme}")
    return path


def output_path(name, code):
    if screenshot(name):
        sid, theme = screenshot(name)
        return f"img/screens/{sid}-{code}-{theme}"
    return f"img/{name}"


def load(name, code=DEFAULT):
    image = Image.open(source(name, code))
    if screenshot(name) and image.size != SCREEN_SIZE:
        sys.exit(f"[{code}] {source(name, code)} is {image.size[0]}x{image.size[1]}, not {SCREEN_SIZE[0]}x{SCREEN_SIZE[1]}.")
    return image


def encode(image, name):
    buf = io.BytesIO()
    if name == "logo":
        image.resize((image.width * 2, image.height * 2), Image.NEAREST).save(buf, "PNG", optimize=True)
        return "png", buf.getvalue()
    if name.startswith("output-"):
        image.save(buf, "WEBP", lossless=True, method=6)
        return "webp", buf.getvalue()
    image.convert("RGB").save(buf, "WEBP", quality=82, method=6)
    return "webp", buf.getvalue()


def font(size, bold=False):
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
    scaled_logo(48).save(os.path.join(SITE, "favicon.ico"), sizes=[(16, 16), (32, 32), (48, 48)])
    scaled_logo(192).save(os.path.join(SITE, "favicon-192.png"), optimize=True)
    touch = Image.new("RGBA", (180, 180), (22, 24, 28, 255))
    touch.alpha_composite(scaled_logo(160), (10, 10))
    touch.convert("RGB").save(os.path.join(SITE, "apple-touch-icon.png"), optimize=True)
    # The site icons of TibiaWiki and tibia.com, as the program's About page shows them next to the creator's profiles.
    os.makedirs(os.path.join(SITE, "img", "icons"), exist_ok=True)
    for name in ("tibiawiki", "tibia"):
        ico = Image.open(os.path.join(APP_LINK_ICONS, f"{name}-favicon.ico"))
        sizes = sorted(ico.info.get("sizes", {ico.size}), key=lambda s: s[0])
        best = sizes[-1]
        ico.size = best
        ico = ico.convert("RGBA")
        ico.resize((32, 32), Image.LANCZOS if best[0] > 32 else Image.NEAREST).save(os.path.join(SITE, "img", "icons", f"{name}.png"), optimize=True)


def write_preview(tr, filename):
    og = Image.new("RGB", (1200, 630), (22, 24, 28))
    shot = load("asset-library-dark", tr.code).convert("RGB")
    shot = shot.resize((700, int(shot.height * 700 / shot.width)), Image.LANCZOS)
    og.paste(shot, (470, 100))
    d = ImageDraw.Draw(og)
    og.paste(scaled_logo(96).convert("RGB"), (56, 64), scaled_logo(96))
    d.text((56, 190), "Tibia Foundry", font=font(54, True), fill=(240, 242, 245))
    d.text((58, 258), tr.text("ogImage.version"), font=font(24), fill=(232, 188, 106))
    y = 320
    for n in range(1, 5):
        line, size = "• " + TAG.sub("", tr.text(f"ogImage.line{n}")), 25
        while d.textlength(line, font=font(size)) > 400 and size > 18:
            size -= 1
        d.text((58, y), line, font=font(size), fill=(200, 205, 212))
        y += 44
    d.text((58, 560), "tibiafoundry.com", font=font(22, True), fill=(127, 178, 255))
    og.save(os.path.join(SITE, filename), optimize=True)


# ============================================================================ pages
def lang_folder(code):
    return next(l[1] for l in LANGUAGES if l[0] == code)


def page_dir(code, page):
    """Folder of a page relative to the site root ('' for the English home page)."""
    return "/".join(x for x in (lang_folder(code), page["path"].strip("/")) if x)


def page_url(code, page):
    d = page_dir(code, page)
    return ORIGIN + (d + "/" if d else "")


def rel(from_dir, to_dir):
    """Relative link from one page folder to another (both relative to the site root)."""
    up = "../" * (len(from_dir.split("/")) if from_dir else 0)
    target = (to_dir + "/") if to_dir else ""
    return (up + target) or "./"


def og_name(code):
    folder = lang_folder(code)
    return f"og-image-{folder}.png" if folder else "og-image.png"


CSS_LAYERS = ["tokens", "base", "components", "pages", "utilities"]   # cascade order: later layers win, whatever the specificity
# The files of each layer, in order. Files of pages/ are added in sorted order. A file that should come first in a layer
# (for example fonts.css in base) is listed before the others here; --strict fails when a file under src/css is not listed.
CSS_FILES = {"tokens": ["tokens.css"], "base": ["fonts.css", "base.css"], "components": ["components.css"], "pages": [], "utilities": ["utilities.css"]}
CSS_TOKEN = re.compile(r"""/\*.*?\*/|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|url\(\s*[^"'\s)][^)]*\)|\s+|[{};,]|[^\s{};,"'/(]+|.""", re.S)


def minify_css(css):
    """Removes comments and collapses whitespace (none around { } ; ,); strings and url() stay exactly as written."""
    tokens = []
    for m in CSS_TOKEN.finditer(css):
        t = m.group(0)
        tokens.append(" " if t.startswith("/*") or t.isspace() else t)
    out = []
    for i, t in enumerate(tokens):
        if t == " ":
            before = out[-1] if out else "{"
            after = tokens[i + 1] if i + 1 < len(tokens) else "}"
            if before in ("{", "}", ";", ",", " ") or after in ("{", "}", ";", ",", " "):
                continue
        if t == "}" and out and out[-1] == ";":
            out.pop()
        out.append(t)
    return "".join(out).strip()


def css_bundle(strict=False):
    """site.css: every file of src/css wrapped in its @layer (CSS_LAYERS order), minified. --strict: no file may be left out."""
    root = os.path.join(SRC, "css")
    on_disk = sorted(os.path.relpath(os.path.join(d, f), root).replace(os.sep, "/") for d, _, fs in os.walk(root) for f in fs if f.endswith(".css"))
    pages = sorted(f for f in on_disk if f.startswith("pages/"))
    order = {layer: [f for f in CSS_FILES[layer]] + (pages if layer == "pages" else []) for layer in CSS_LAYERS}
    listed = [f for files in order.values() for f in files]
    missing = [f for f in listed if f not in on_disk]
    if missing:
        sys.exit(f"css_bundle: file(s) of the bundle not found in src/css: {', '.join(missing)}")
    if strict:
        left_out = [f for f in on_disk if f not in listed]
        if left_out:
            sys.exit(f"--strict: src/css file(s) not in the bundle: {', '.join(left_out)}")
        if os.path.exists(os.path.join(SRC, "styles.css")):
            sys.exit("--strict: src/styles.css still exists; its rules live in src/css/ now.")
    parts = ["@layer " + ",".join(CSS_LAYERS) + ";"]
    for layer in CSS_LAYERS:
        body = "".join(minify_css(open(os.path.join(root, f), encoding="utf-8").read()) for f in order[layer])
        parts.append("@layer " + layer + "{" + body + "}")
    return "\n".join(parts) + "\n"


def asset_files(strict=False):
    """name -> bytes of everything published under assets/."""
    files = {"site.css": css_bundle(strict).encode("utf-8")}
    for name in sorted(os.listdir(os.path.join(SRC, "js"))):
        if name.endswith(".js"):
            files[name] = open(os.path.join(SRC, "js", name), "rb").read()
    fonts = os.path.join(SRC, "fonts")
    for name in sorted(os.listdir(fonts)):
        if name.endswith(".woff2"):
            files["fonts/" + name] = open(os.path.join(fonts, name), "rb").read()
    referenced = set(re.findall(r"url\(\s*fonts/([^)\s]+?)\s*\)", open(os.path.join(SRC, "css", "fonts.css"), encoding="utf-8").read()))
    absent = sorted(f for f in referenced if "fonts/" + f not in files)
    if absent:
        sys.exit(f"fonts.css names font file(s) missing from src/fonts: {', '.join(absent)}")
    if strict:
        unused = sorted(f[6:] for f in files if f.startswith("fonts/") and f[6:] not in referenced)
        if unused:
            sys.exit(f"--strict: src/fonts file(s) not used by fonts.css: {', '.join(unused)}")
    return files


def credits_html(tr):
    """The footer's (and About page's) data and resource credits from providers.json; TibiaData first, with its own thank-you."""
    out = []
    for p in PROVIDERS:
        link = (f'<a href="{htmllib.escape(p["url"])}" rel="noopener noreferrer" target="_blank" data-provider="{p["id"]}">'
                f'{htmllib.escape(p["name"])}<span class="sr-only"> {tr.attr("js.newTab")}</span></a>')
        raw = tr.raw(f"credits.{p['id']}")[0]
        if "{link}" not in raw:
            sys.exit(f"[{tr.code}] credits.{p['id']} needs {{link}} (the provider's name, linked)")
        out.append((p["id"], tr.text(f"credits.{p['id']}").replace("{link}", link)))
    first = next(t for i, t in out if i == "tibiadata")
    items = "\n".join(f'        <li data-credit="{i}">{t}</li>' for i, t in out if i != "tibiadata")
    return (f'      <p class="credits-thanks" data-credit="tibiadata">{first}</p>\n      <ul>\n{items}\n      </ul>\n'
            f'      <p class="muted">{tr.text("credits.independent")}</p>')


def profiles_html(tr, root):
    items = []
    for link in REGISTRY["creator"]["links"]:
        name = tr.text(f"about.link.{link['id']}")
        if link["id"] in ("tibiawiki", "tibia"):
            icon = f'<img src="{root}img/icons/{link["id"]}.png" alt="" width="28" height="28">'
        else:
            icon = f'<svg aria-hidden="true" focusable="false"><use href="#i-{link["id"]}"/></svg>'
        label = tr.attr("about.linkLabel", platform=TAG.sub("", name), handle=link["handle"])
        items.append(f'        <li><a class="profile" href="{htmllib.escape(link["url"])}" target="_blank" rel="noopener noreferrer" '
                     f'data-profile="{link["id"]}" aria-label="{label}">{icon}<span class="profile-text"><span class="profile-name">{name}</span>'
                     f'<span class="profile-handle">{htmllib.escape(link["handle"])}</span></span></a></li>')
    return '<ul class="profiles" data-profiles>\n' + "\n".join(items) + "\n      </ul>"


def footer_profiles_html(tr, root):
    """The six profiles as a row of icons in the footer (same URLs and labels as the About page)."""
    items = []
    for link in REGISTRY["creator"]["links"]:
        name = tr.text(f"about.link.{link['id']}")
        if link["id"] in ("tibiawiki", "tibia"):
            icon = f'<img src="{root}img/icons/{link["id"]}.png" alt="" width="24" height="24">'
        else:
            icon = f'<svg aria-hidden="true" focusable="false"><use href="#i-{link["id"]}"/></svg>'
        label = tr.attr("about.linkLabel", platform=TAG.sub("", name), handle=link["handle"])
        items.append(f'          <li><a href="{htmllib.escape(link["url"])}" target="_blank" rel="noopener noreferrer" '
                     f'data-profile="{link["id"]}" aria-label="{label}" title="{htmllib.escape(TAG.sub("", name))}">{icon}</a></li>')
    return '        <ul class="footer-people" data-footer-profiles>\n' + "\n".join(items) + "\n        </ul>"


def download_notice_html(tr, root, legal_link, heading="h3"):
    """The download notice shown next to every download offer (Fansite Agreement: downloads need a disclaimer)."""
    return (f'      <div class="dl-notice" data-download-notice role="note">'
            f'<svg class="ic" aria-hidden="true"><use href="#i-shield-check"/></svg><div>'
            f'<{heading} class="dl-notice-title">{tr.text("notice.title")}</{heading}>'
            f'<p>{tr.text("notice.text1")}</p><p>{tr.text("notice.text2")}</p><p>{tr.text("notice.text3")}</p>'
            f'<p><a href="{legal_link}#downloads">{tr.text("notice.more")}</a></p></div></div>')


def shot_html(tr, img, spec):
    sid, dark_key, light_key, zoom_key, *rest = spec.split(":")
    eager = bool(rest) and rest[0] == "eager"
    w, h = SCREEN_SIZE
    load_attr = 'fetchpriority="high"' if eager else 'loading="lazy"'
    return (f'<button class="shot" type="button" data-zoom="{tr.attr(zoom_key)}">'
            f'<img class="shot-dark" src="{img(sid + "-dark")}" alt="{tr.attr(dark_key)}" width="{w}" height="{h}" {load_attr} decoding="async">'
            f'<img class="shot-light" src="{img(sid + "-light")}" alt="{tr.attr(light_key)}" width="{w}" height="{h}" {load_attr} decoding="async"></button>')


def gallery_html(tr, img):
    tabs, panels = [], []
    w, h = SCREEN_SIZE
    for i, (sid, keys, _) in enumerate(SCREENS):
        on = i == 0
        page = " → ".join(tr.app(k) for k in keys)
        label = htmllib.escape(tr.app(keys[-1]), quote=False)
        tabs.append(f'      <button class="tab" role="tab" id="t-{sid}" aria-controls="p-{sid}" aria-selected="{str(on).lower()}"'
                    + ("" if on else ' tabindex="-1"') + f">{label}</button>")
        dark, light = tr.attr("screens.altDark", page=page), tr.attr("screens.altLight", page=page)
        caption_key = f"screens.caption.{sid}"
        panels.append(
            f'    <figure class="tab-panel" role="tabpanel" id="p-{sid}" aria-labelledby="t-{sid}"' + ("" if on else " hidden") + ">\n"
            f'      <button class="shot" type="button" data-zoom="{tr.attr(caption_key)}">'
            f'<img class="shot-dark" src="{img(sid + "-dark")}" alt="{dark}" width="{w}" height="{h}" loading="lazy" decoding="async">'
            f'<img class="shot-light" src="{img(sid + "-light")}" alt="{light}" width="{w}" height="{h}" loading="lazy" decoding="async"></button>\n'
            f"      <figcaption>{tr.text(caption_key)}</figcaption>\n    </figure>")
    return (f'    <div class="tabs" role="tablist" aria-label="{tr.attr("screens.tablist")}">\n' + "\n".join(tabs)
            + "\n    </div>\n" + "\n".join(panels)
            + f'\n    <dialog class="lightbox" id="lightbox" aria-label="{tr.attr("lightbox.label")}"><img alt=""><p></p>'
            f'<button class="btn btn-small close" type="button">{tr.text("lightbox.close")}</button></dialog>')


def docs_html(tr):
    return "\n".join(f'      <li><a class="tool-card" href="{d["url"]}" target="_blank" rel="noopener noreferrer">'
                     f'<span class="tool-title">{tr.text("guides.docs." + d["id"] + ".title")}</span>'
                     f'<span class="tool-text">{tr.text("guides.docs." + d["id"] + ".text")}</span></a></li>' for d in REGISTRY["docs"])


def downloads(ver, checksums, sizes=None):
    dl = REGISTRY["downloads"]
    base = dl["base"].replace("{version}", ver)
    files = {}
    for f in dl["files"]:
        name = f["file"].replace("{version}", ver)
        files[f["id"]] = {"file": name, "url": base + name, "sha": checksums.get(name), "size": (sizes or {}).get(name)}
    return files, base + dl["checksums"], dl["releasePage"].replace("{version}", ver), dl["allReleases"]


# ============================================================================ calculators
SOURCE_NAMES = {"tibia.fandom.com": "TibiaWiki", "tibia.com": "Tibia.com"}
TODAY_JS_KEYS = {"home.today.countdown", "home.today.local"}   # the texts today.js writes at run time


def source_label(url):
    """A source's link text: the site's name (a TibiaWiki page with its title), else its host name."""
    parts = urllib.parse.urlparse(url)
    host = parts.netloc.lower()
    host = host[4:] if host.startswith("www.") else host
    name = SOURCE_NAMES.get(host, host)
    if host == "tibia.fandom.com" and parts.path.startswith("/wiki/"):
        name += ": " + urllib.parse.unquote(parts.path[len("/wiki/"):]).replace("_", " ")
    return name


def calc_how_html(tr, fid):
    """"How this is calculated" for one formula: its rule in words, its sources (new tab, with their kind and, when a source
    shows something different from our result - another rounding, part of the values only - its localized qualifier, the
    source's optional "label" key) and the date it was last checked, all from src/formulas.json."""
    if fid not in FORMULA_REGISTRY:
        sys.exit(f"{{{{CALC_HOW:{fid}}}}}: '{fid}' is not in src/formulas.json.")
    f = FORMULA_REGISTRY[fid]
    sources = "".join(
        f'<li><a href="{htmllib.escape(s["url"])}" target="_blank" rel="noopener noreferrer" data-source-kind="{htmllib.escape(s["kind"])}">'
        f'{htmllib.escape(source_label(s["url"]))}<span class="sr-only"> {tr.attr("js.newTab")}</span></a> '
        f'<span class="how-kind">({tr.text("calc.how.kind." + s["kind"])})</span>'
        + (f' <span class="how-note" data-source-label>{tr.text(s["label"])}</span>' if s.get("label") else "")
        + '</li>' for s in f["sources"])
    checked = format_date(tr.code, datetime.date.fromisoformat(f["checkedOn"]))
    return (f'<div class="how-item" data-how="{fid}">\n'
            f'      <p class="how-rule">{tr.text(f["rule"])}</p>\n'
            f'      <p class="how-label">{tr.text("calc.how.sources")}</p>\n'
            f'      <ul class="how-sources">{sources}</ul>\n'
            f'      <p class="how-checked"><time datetime="{f["checkedOn"]}">{tr.text("calc.how.checked", checked=checked)}</time></p>\n'
            f'    </div>')


def today_server_html(tr):
    """The server save and Rashid block of the "Today in Tibia" panel (home page and calculators hub); src/js/today.js fills the
    values. Its rules and sources come from src/formulas.json like a calculator's; the ticking countdown is not a live region."""
    return (f'<div class="today-server" data-today-server>\n'
            f'        <p class="today-row"><span class="today-label">{tr.text("home.today.rashid")}</span> <strong class="today-value" data-today-rashid></strong></p>\n'
            f'        <p class="today-row"><span class="today-label">{tr.text("home.today.save")}</span> <strong class="today-value" data-today-countdown></strong>'
            f' <span class="today-sub" data-today-clock></span></p>\n'
            f'        <p class="today-note">{tr.text("home.today.maintenance")}</p>\n'
            f'        {today_how_html(tr)}\n'
            f'      </div>')


def today_how_html(tr):
    """"How this is calculated" for the server save and Rashid (rules and sources from src/formulas.json)."""
    return (f'<details class="how today-how"><summary><svg class="ic" aria-hidden="true"><use href="#i-info"/></svg>{tr.text("calc.how.title")}</summary>\n'
            f'        <div class="how-body">{calc_how_html(tr, "nextServerSave")}{calc_how_html(tr, "rashid")}</div></details>')


def today_strip_html(tr):
    """{{TODAY_STRIP}} (home page): four cells. Boosted creature and boss (src/js/data-app.js renderBoosted) start as skeletons and
    are hidden when TibiaData cannot tell; Rashid and the server save come from src/js/today.js (the [data-today-server] root).
    No live region: the countdown ticks, and the boosted cells only appear or disappear."""
    def boosted(kind, label):
        return (f'          <li class="today-cell today-cell-live" data-boosted="{kind}" aria-busy="true">'
                f'<span class="today-kind">{tr.text(label)}</span>'
                f'<span class="today-skeleton" aria-hidden="true"><span></span><span></span></span></li>\n')
    return ('<div class="today-strip" data-today-server data-today-strip>\n'
            '        <ul class="today-cells">\n'
            + boosted("creature", "js.boostedCreature") + boosted("boss", "js.boostedBoss")
            + f'          <li class="today-cell"><span class="today-label">{tr.text("home.today.rashid")}</span> '
            f'<strong class="today-value" data-today-rashid></strong></li>\n'
            f'          <li class="today-cell"><span class="today-label">{tr.text("home.today.save")}</span> '
            f'<strong class="today-value" data-today-countdown></strong> <span class="today-sub" data-today-clock></span></li>\n'
            '        </ul>\n'
            f'        <div class="today-foot"><p class="today-note">{tr.text("home.today.maintenance")}</p>\n'
            f'        {today_how_html(tr)}</div>\n'
            '      </div>')


def calc_cards_html(tr, link_to):
    """The hub's cards: one per calculator page; its text is the key next to the page's name (calc.<x>.title -> calc.<x>.card)."""
    return "\n".join(
        f'      <li><a class="dest-card" href="{link_to(p["id"])}" data-calc-card="{p["id"]}"><span class="icon-tile">'
        f'<svg class="ic" aria-hidden="true"><use href="#i-{p["icon"]}"/></svg></span><span class="dest-title">{tr.text(p["nav"])}</span>'
        f'<span class="dest-text">{tr.text(p["nav"].rsplit(".", 1)[0] + ".card")}</span></a></li>' for p in CALC_PAGES)


# ============================================================================ menu (header panels, phone drawer, footer columns)
MENU = REGISTRY["menu"]
# A menu route that stops before a pattern's :world or :town (e.g. "guilds" for guilds/:world) takes these, as the Tibia
# page's own sub-navigation does.
MENU_ROUTE_DEFAULTS = {"world": REGISTRY["fixtures"]["world"], "town": REGISTRY["fixtures"]["house"]["town"]}


def resolve_route(route):
    """A menu route as a data route path: exactly a pattern (literal segments equal, :params filled), or the literal start of
    a pattern whose remaining :params all have defaults. None when no data route matches."""
    segs = route.split("/")
    patterns = [r["pattern"].split("/") for r in REGISTRY["dataRoutes"] if r["pattern"]]
    for pat in patterns:
        if len(pat) == len(segs) and all(p.startswith(":") or p == s for p, s in zip(pat, segs)):
            return route
    for pat in patterns:
        rest = pat[len(segs):]
        if (len(pat) > len(segs) and all(p.startswith(":") or p == s for p, s in zip(pat, segs))
                and all(p.startswith(":") and p[1:] in MENU_ROUTE_DEFAULTS for p in rest)):
            return "/".join(segs + [MENU_ROUTE_DEFAULTS[p[1:]] for p in rest])
    return None


def menu_entries():
    """registry.menu as (group or None, [items]); a plain link is a group-less single item."""
    return [(e, e["items"]) if "items" in e else (None, [e]) for e in MENU]


def menu_dest(item):
    """The stable key of a menu destination (data-menu-dest): page:<id>[#hash], route:<route> or href:<url>."""
    if "page" in item:
        return "page:" + item["page"] + ("#" + item["hash"] if item.get("hash") else "")
    return ("route:" + item["route"]) if "route" in item else ("href:" + item["href"])


def check_menu(strict):
    """An unknown page always stops the build (no link could be written); --strict also fails on an unknown data route, an
    item without a label, a malformed group or a calculator page missing from the menu (otherwise they are printed)."""
    problems, pages, listed = [], {p["id"] for p in PAGES}, set()
    for group, items in menu_entries():
        if group is not None and not (group.get("id") and group.get("label") and items):
            problems.append(f"group {group.get('id')!r} needs an id, a label and items")
        for item in items:
            kinds = [k for k in ("page", "route", "href") if k in item]
            if len(kinds) != 1:
                problems.append(f"item {item} needs exactly one of page, route or href")
                continue
            if "page" in item:
                if item["page"] not in pages:
                    sys.exit(f"registry.menu: unknown page '{item['page']}'")
                listed.add(item["page"])
            elif "route" in item and resolve_route(item["route"]) is None:
                problems.append(f"route '{item['route']}' matches no data route")
            elif "href" in item and not item["href"].startswith("https://"):
                problems.append(f"href '{item['href']}' must be an https:// address")
            if ("route" in item or "href" in item) and not item.get("label"):
                problems.append(f"{menu_dest(item)} needs a label")
    problems += [f"calculator page '{p['id']}' is not in the menu" for p in CALC_PAGES if p["id"] not in listed]
    if problems and strict:
        sys.exit("--strict: registry.menu errors:\n  " + "\n  ".join(problems))
    for p in problems:
        print("  registry.menu: " + p)


# ============================================================================ global search (header; src/js/search.js)
SEARCH = REGISTRY["search"]
SEARCH_LISTS = {"worlds", "creatures", "spells"}   # the TibiaData lists search.js turns into suggestions


def check_search(strict):
    """registry.search: each route is a data route with {placeholders} where the pattern has :params, browse is a data route
    without params, list is a list search.js understands; a type exists in every language (js.searchBox.*)."""
    problems, patterns, seen = [], [r["pattern"].split("/") for r in REGISTRY["dataRoutes"]], set()
    for e in SEARCH:
        t = e.get("type")
        if not t or t in seen:
            problems.append(f"entry {e} needs a unique type")
        seen.add(t)
        segs = e.get("route", "").split("/")
        if not any(len(p) == len(segs) and all((re.fullmatch(r"\{\w+\}", s) is not None) if q.startswith(":") else q == s for q, s in zip(p, segs))
                   for p in patterns):
            problems.append(f"{t}: route '{e.get('route')}' matches no data route")
        if "browse" in e and e["browse"].split("/") not in patterns:
            problems.append(f"{t}: browse '{e['browse']}' is not a data route without parameters")
        if "list" in e and e["list"] not in SEARCH_LISTS:
            problems.append(f"{t}: list '{e['list']}' is not one of {sorted(SEARCH_LISTS)}")
        if e.get("exact") and "list" in e:
            problems.append(f"{t}: an exact-name type has no list")
    if problems and strict:
        sys.exit("--strict: registry.search errors:\n  " + "\n  ".join(problems))
    for p in problems:
        print("  registry.search: " + p)


def search_types_html(tr):
    """{{SEARCH_TYPES}}: the header search's type picker, in registry order (the first is the default)."""
    return "\n".join(f'          <option value="{e["type"]}">{tr.text("js.searchBox.type." + e["type"])}</option>' for e in SEARCH)


def menu_html(tr, link_to, page):
    """{{NAV}} (desktop disclosure buttons and panels), {{MENU_NAV}} (the drawer's collapsible groups and plain links),
    {{FOOTER_MENU}} (a footer column per group) and {{FOOTER_MENU_LINKS}} (the plain links, in the footer's Project column)."""
    tibia = link_to("tibia")

    def link(item, place, cls="", icons=True):
        """(the <a> of an item, whether it is the current page); place: header, drawer or footer (data-place of tracked
        links); icons only inside panels and drawer groups. An href item is external: new tab, rel, the "new tab" text."""
        if "page" in item:
            target = next(p for p in PAGES if p["id"] == item["page"])
            href = link_to(item["page"]) + ("#" + item["hash"] if item.get("hash") else "")
            label, icon = tr.text(item.get("label") or target["nav"]), item.get("icon") or target["icon"]
            current = item["page"] == page["id"] and not item.get("hash")
            extra = "" if item.get("hash") else f' data-page-link="{item["page"]}"'
        elif "route" in item:
            href = tibia + "#/" + "/".join(urllib.parse.quote(s, safe="") for s in resolve_route(item["route"]).split("/"))
            label, icon, current, extra = tr.text(item["label"]), item.get("icon"), False, ""
        else:
            href, label, icon, current = htmllib.escape(item["href"]), tr.text(item["label"]), item.get("icon"), False
            extra = ' target="_blank" rel="noopener noreferrer"'
            extra += f' data-track="repository" data-place="{place}"' if item["href"] == REGISTRY["repository"] else ""
        body = (f'<svg class="ic" aria-hidden="true"><use href="#i-{icon}"/></svg><span>{label}</span>'
                if icon and icons and place != "footer" else (f"<span>{label}</span>" if "href" in item else label))
        if "href" in item:
            body += f'<span class="sr-only"> {tr.text("js.newTab")}</span>'
        return (f'<a{cls} href="{href}" data-menu-dest="{htmllib.escape(menu_dest(item))}"{extra}'
                + (' aria-current="page"' if current else "") + f">{body}</a>"), current

    nav, drawer, footer, footer_links = [], [], [], []
    chevron = '<svg class="ic nav-chevron" aria-hidden="true"><use href="#i-chevron-down"/></svg>'
    for group, items in menu_entries():
        if group is None:
            nav.append('        <li class="nav-item">' + link(items[0], "header", ' class="nav-link"', icons=False)[0] + "</li>")
            drawer.append("        " + link(items[0], "drawer", ' class="drawer-link"', icons=False)[0])
            footer_links.append(f'            <li>{link(items[0], "footer")[0]}</li>')
            continue
        gid, label = group["id"], tr.text(group["label"])
        links = [link(i, "header") for i in items]
        here = " data-current" if any(cur for _, cur in links) else ""
        wide = " nav-panel-wide" if len(items) > 6 else ""
        nav.append(f'        <li class="nav-group" data-nav-group="{gid}">\n'
                   f'          <button type="button" class="nav-button" aria-expanded="false" aria-controls="nav-panel-{gid}"{here}>'
                   f'<span>{label}</span>{chevron}</button>\n'
                   f'          <div class="nav-panel{wide}" id="nav-panel-{gid}" hidden>\n            <ul class="nav-panel-list">\n'
                   + "\n".join(f"              <li>{a}</li>" for a, _ in links) + "\n            </ul>\n          </div>\n        </li>")
        drawer.append(f'        <details class="drawer-group" data-nav-group="{gid}"' + (" open" if here else "") + ">\n"
                      f'          <summary class="drawer-summary"{here}><span>{label}</span>{chevron}</summary>\n'
                      '          <ul class="drawer-links">\n'
                      + "\n".join(f"            <li>{link(i, 'drawer')[0]}</li>" for i in items) + "\n          </ul>\n        </details>")
        footer.append(f'        <div data-footer-group="{gid}">\n          <h2>{label}</h2>\n          <ul>\n'
                      + "\n".join(f"            <li>{link(i, 'footer')[0]}</li>" for i in items) + "\n          </ul>\n        </div>")
    return "\n".join(nav), "\n".join(drawer), "\n".join(footer), "\n".join(footer_links)


# ============================================================================ home page (app hero, showcase, tool shortcuts)
HOME_SHOWCASE = ("asset-library", "extract-sprites", "map-tools", "content-studio")   # screens of SCREENS, in this order
# The community shortcuts: these routes of the registry.menu "community" group, in the menu's order.
HOME_COMMUNITY = ("character", "guilds", "worlds", "highscores/all/experience/all/1", "bosses", "killstatistics")


def picture_html(img, sid, alt, eager=False):
    """A screenshot as <picture>: the dark variant for prefers-color-scheme: dark, else the light one. src/js/site.js points the
    dark <source> at the visitor's own choice (data-theme) when there is one. Only the hero loads eagerly."""
    w, h = SCREEN_SIZE
    load = 'fetchpriority="high"' if eager else 'loading="lazy"'
    return (f'<picture data-theme-picture><source srcset="{img(sid + "-dark")}" media="(prefers-color-scheme: dark)" data-theme-source="dark" '
            f'width="{w}" height="{h}"><img src="{img(sid + "-light")}" alt="{alt}" width="{w}" height="{h}" {load} decoding="async"></picture>')


def home_showcase_html(tr, img):
    """{{HOME_SHOWCASE}}: four screenshots of the visitor's language with the program's own name of the page and a short caption."""
    names = {sid: keys for sid, keys, _ in SCREENS}
    out = []
    for sid in HOME_SHOWCASE:
        name = " → ".join(tr.app(k) for k in names[sid])
        out.append(f'      <figure class="showcase-item">\n        <div class="showcase-frame">'
                   f'{picture_html(img, sid, tr.attr("home.showcase.alt", page=name))}</div>\n'
                   f'        <figcaption><strong>{htmllib.escape(name, quote=False)}</strong> {tr.text("home.showcase." + sid)}</figcaption>\n'
                   f'      </figure>')
    return "\n".join(out)


def home_tools_html(tr, link_to):
    """{{HOME_CALCS}} (the calculator pages of the registry.menu "tools" group) and {{HOME_COMMUNITY}} (HOME_COMMUNITY's routes of
    the "community" group): plain links, so they work without JavaScript; labels and icons are the menu's."""
    groups = {e["id"]: e["items"] for e in MENU if "items" in e}
    calc_ids = {p["id"]: p for p in CALC_PAGES}
    calcs = [calc_ids[i["page"]] for i in groups.get("tools", []) if i.get("page") in calc_ids and not i.get("hash")]
    community = [i for i in groups.get("community", []) if i.get("route") in HOME_COMMUNITY]
    if len(community) != len(HOME_COMMUNITY):
        sys.exit("HOME_COMMUNITY: every route must be an item of the registry.menu \"community\" group.")
    tibia = link_to("tibia")
    calc_html = "\n".join(
        f'        <li><a class="tool-link" href="{link_to(p["id"])}"><span class="icon-tile icon-tile-sm"><svg class="ic" aria-hidden="true">'
        f'<use href="#i-{p["icon"]}"/></svg></span><span class="tool-link-text"><span class="tool-link-title">{tr.text(p["nav"])}</span>'
        f'<span class="tool-link-sub">{tr.text(p["nav"].rsplit(".", 1)[0] + ".card")}</span></span></a></li>' for p in calcs)
    comm_html = "\n".join(
        f'        <li><a class="chip-link" href="{tibia}#/' + "/".join(urllib.parse.quote(s, safe="") for s in resolve_route(i["route"]).split("/"))
        + f'"><svg class="ic" aria-hidden="true"><use href="#i-{i["icon"]}"/></svg><span>{tr.text(i["label"])}</span></a></li>' for i in community)
    return calc_html, comm_html


def page_title(tr, page):
    return tr.text("meta.title") if page["id"] == "home" else tr.text(f"pages.{page['id']}.title") + " – Tibia Foundry"


def page_description(tr, page):
    return tr.attr("meta.description") if page["id"] == "home" else tr.attr(f"pages.{page['id']}.description")


def structured_data(tr, ver, page, code):
    creator = {"@type": "Person", "name": REGISTRY["creator"]["name"], "jobTitle": TAG.sub("", tr.text("about.role")),
               "homeLocation": {"@type": "Place", "name": "Upstate New York, USA"},
               "email": "mailto:" + REGISTRY["creator"]["email"], "sameAs": [l["url"] for l in REGISTRY["creator"]["links"]]}
    data = [{"@context": "https://schema.org", "@type": "WebPage", "name": TAG.sub("", page_title(tr, page)), "url": page_url(code, page),
             "inLanguage": code, "isPartOf": {"@type": "WebSite", "name": "Tibia Foundry", "url": ORIGIN}}]
    if page["id"] in ("home", "downloads"):
        data.append({"@context": "https://schema.org", "@type": "SoftwareApplication", "name": "Tibia Foundry", "softwareVersion": ver,
                     "operatingSystem": "Windows 10, Windows 11", "applicationCategory": "MultimediaApplication",
                     "offers": {"@type": "Offer", "price": "0", "priceCurrency": "USD"}, "url": ORIGIN, "downloadUrl": ORIGIN + "downloads/",
                     "author": creator, "description": TAG.sub("", tr.text("meta.description"))})
    if page["id"] in ("about", "contact"):
        data.append(dict(creator, **{"@context": "https://schema.org"}))
    return json.dumps(data, ensure_ascii=False, separators=(",", ":")).replace("</", "<\\/")


def render(layout, templates, code, page, tr, translators, img, ver, assets, dlinfo):
    here = page_dir(code, page)
    root = ("../" * len(here.split("/"))) if here else ""
    current = next(l for l in LANGUAGES if l[0] == code)

    def link_to(page_id, lang=code):
        return rel(here, page_dir(lang, next(p for p in PAGES if p["id"] == page_id)))

    alternates = [f'<link rel="alternate" hreflang="{c}" href="{page_url(c, page)}">' for c, *_ in LANGUAGES]
    alternates.append(f'<link rel="alternate" hreflang="x-default" href="{page_url(DEFAULT, page)}">')
    nav, menu, footer_menu, footer_menu_links = menu_html(tr, link_to, page)
    lang_menu, lang_footer = [], []
    for c, folder, name, badge, _ in LANGUAGES:
        cur = ' aria-current="true"' if c == code else ""
        href = link_to(page["id"], c)
        lang_menu.append(f'          <li><a href="{href}" hreflang="{c}" lang="{c}" data-lang="{c}"{cur}><span class="lang-badge" aria-hidden="true">{badge}</span> {name}</a></li>')
        lang_footer.append(f'          <li><a href="{href}" hreflang="{c}" lang="{c}" data-lang="{c}"{cur}>{name}</a></li>')

    english = translators[DEFAULT]
    calc_page = bool(page.get("calc")) or page["id"] == "calculators"
    today_page = page["id"] in ("home", "calculators")   # the server save and Rashid block (src/js/today.js)
    js_keys = [k for k in sorted(english.locales[DEFAULT]) if k.startswith("js.") or (calc_page and k.startswith("calc.")) or (today_page and k in TODAY_JS_KEYS)] + ["nav.menuOpen", "nav.menuClose"]
    runtime = {
        "lang": code, "intl": INTL[code], "default": DEFAULT,
        "strings": {k: tr.runtime_text(k) for k in js_keys}, "fallback": {k: english.runtime_text(k) for k in js_keys},
        "languages": [{"code": c, "name": n, "offer": {"text": translators[c].text("js.suggestText"), "go": translators[c].text("js.suggestGo"),
                                                       "close": translators[c].text("js.suggestClose")}} for c, f, n, *_ in LANGUAGES],
    }
    tibia_link = link_to("tibia")
    site_json = {
        "version": ver, "page": page["id"], "api": REGISTRY["api"], "dataRoutes": REGISTRY["dataRoutes"],
        "defaultWorld": REGISTRY["fixtures"]["world"], "defaultTown": MENU_ROUTE_DEFAULTS["town"], "search": SEARCH,
        "paths": {"tibia": tibia_link, "tibiaHash": "" if page["id"] == "tibia" else tibia_link},
    }
    redirect = ""
    if code == DEFAULT:
        paths = {c: f + "/" for c, f, *_ in LANGUAGES if f}
        redirect = ("  // A visitor who chose another language is taken to this page in that language; search engines keep no storage.\n"
                    "  try { var l = localStorage.getItem('tf-lang'), P = " + json.dumps(paths) + ";\n"
                    "    if (P[l] && !/[?&]lang=en\\b/.test(location.search)) {\n"
                    "      var path = location.pathname.replace(/index\\.html$/, ''), sub = " + json.dumps(page["path"]) + ";\n"
                    "      var base = path.slice(0, path.length - sub.length);\n"
                    "      location.replace(base + P[l] + sub + (location.protocol === 'file:' ? 'index.html' : '') + location.search + location.hash);\n"
                    "    } } catch (e) {}")
    # Every page: the header search (search.js) needs the API client (tibiadata.js), which needs formulas.js loaded before it
    # (server-save expiry of the creature list).
    scripts = (["site.js", "nav.js", "formulas.js", "tibiadata.js", "sanitize.js", "search.js"]
               + (["data-app.js"] if page["id"] in ("home", "tibia") else []) + (["calc.js"] if calc_page else [])
               + (["today.js"] if today_page else []))
    files, checksums_url, release_page, all_releases, released = dlinfo

    page_html = layout.replace("{{CONTENT}}", templates[page["template"]])

    def dl_sub(m):
        fid, what = m.group(1), m.group(2)
        if fid == "checksums":
            return checksums_url
        f = files[fid]
        if what == "size":
            return format_size(code, f["size"]) if f["size"] else ""
        if what == "sha":
            tr.raw("download.shaInFile")   # the text for builds without checksums counts as used either way
            return (f'<p class="sha" data-sha="{f["sha"]}">SHA-256: {f["sha"]}</p>' if f["sha"]
                    else f'<p class="sha">{tr.text("download.shaInFile")}</p>')
        return htmllib.escape(f[what])

    fixed = {
        "LANG": code, "LANG_BADGE": current[3], "TITLE": htmllib.escape(TAG.sub("", page_title(tr, page)), quote=False),
        "DESCRIPTION": page_description(tr, page), "CANONICAL": page_url(code, page), "HREFLANG": "\n".join(alternates),
        "OG_TITLE": htmllib.escape(TAG.sub("", page_title(tr, page) if page["id"] != "home" else tr.text("meta.ogTitle")), quote=True),
        "OG_IMAGE": ORIGIN + og_name(code), "OG_LOCALE": current[4],
        "OG_LOCALE_ALTERNATES": "\n".join(f'<meta property="og:locale:alternate" content="{l[4]}">' for l in LANGUAGES if l[0] != code),
        "LANG_REDIRECT": redirect, "ROOT": root, "VERSION": ver, "PAGE_ID": page["id"],
        "NAV": nav, "MENU_NAV": menu, "FOOTER_MENU": footer_menu, "FOOTER_MENU_LINKS": footer_menu_links, "LANG_MENU": "\n".join(lang_menu), "MENU_LANGS": "\n".join(lang_menu),
        "LANG_FOOTER": "\n".join(lang_footer), "CREDITS": credits_html(tr), "PROFILES": profiles_html(tr, root),
        "DOCS": docs_html(tr) if "{{DOCS}}" in page_html else "", "GALLERY": gallery_html(tr, img) if "{{GALLERY}}" in page_html else "",
        "EMAIL": REGISTRY["creator"]["email"], "MAIL_SUBJECT": urllib.parse.quote("Tibia Foundry"),
        "RELEASE_PAGE": release_page, "ALL_RELEASES": all_releases, "ASSET_VERSION": assets["version"],
        "STRUCTURED_DATA": structured_data(tr, ver, page, code),
        "UI_ICONS": UI_ICONS, "SEARCH_TYPES": search_types_html(tr),
        "FOOTER_PROFILES": footer_profiles_html(tr, root),
        "CALC_CARDS": calc_cards_html(tr, link_to) if "{{CALC_CARDS}}" in page_html else "",
        "DOWNLOAD_NOTICE": download_notice_html(tr, root, link_to("legal"), "h2" if page["id"] == "downloads" else "h3")
                           if "{{DOWNLOAD_NOTICE}}" in page_html else "", "YEAR": str(datetime.date.today().year),
        "RELEASE_DATE": format_date(code, released) if released else tr.text("download.facts.dateUnknown"),
        "DL_OS": REGISTRY["downloads"]["os"],
        "CODE_SIGNING": tr.text("download.facts.signedYes" if REGISTRY["downloads"]["codeSigning"] else "download.facts.signedNo"),
    }
    tr.raw("download.facts.dateUnknown")   # used when CHANGELOG.md has no date for the version
    tr.raw("download.facts.signedYes")     # used once a release is code-signed
    for name, value in fixed.items():
        page_html = page_html.replace("{{" + name + "}}", value)
    page_html = re.sub(r"\{\{LINK:([\w-]+)\}\}", lambda m: link_to(m.group(1)), page_html)
    shown = re.findall(r"\{\{CALC_HOW:([\w-]+)\}\}", page_html)
    if sorted(shown) != sorted(page.get("calc", [])):
        sys.exit(f"[{code}/{page['id']}] {{{{CALC_HOW:...}}}} must explain exactly the page's formulas {page.get('calc', [])} (template has {shown}).")
    page_html = re.sub(r"\{\{CALC_HOW:([\w-]+)\}\}", lambda m: calc_how_html(tr, m.group(1)), page_html)
    page_html = page_html.replace("{{TODAY_SERVER}}", today_server_html(tr))
    if "{{TODAY_STRIP}}" in page_html:
        page_html = page_html.replace("{{TODAY_STRIP}}", today_strip_html(tr))
    if "{{HOME_SHOWCASE}}" in page_html:
        calcs, community = home_tools_html(tr, link_to)
        page_html = (page_html.replace("{{HOME_SHOT}}", picture_html(img, "asset-library", tr.attr("home.hero.shotAlt"), eager=True))
                     .replace("{{HOME_SHOWCASE}}", home_showcase_html(tr, img))
                     .replace("{{HOME_CALCS}}", calcs).replace("{{HOME_COMMUNITY}}", community))
    page_html = re.sub(r"\{\{SHOT:([\w.:-]+)\}\}", lambda m: shot_html(tr, img, m.group(1)), page_html)
    page_html = re.sub(r"\{\{DL:(\w+)(?::(\w+))?\}\}", dl_sub, page_html)
    page_html = re.sub(r"\{\{t:([\w.-]+)\}\}", lambda m: tr.text(m.group(1)), page_html)
    page_html = re.sub(r"\{\{a:([\w.-]+)\}\}", lambda m: tr.attr(m.group(1)), page_html)
    page_html = re.sub(r"\{\{IMG:([\w-]+)\}\}", lambda m: img(m.group(1)), page_html)
    page_html = re.sub(r"\{\{APP:([\w.-]+)\}\}", lambda m: htmllib.escape(tr.app(m.group(1)), quote=False), page_html)
    i18n = "window.TF_I18N = " + json.dumps(runtime, ensure_ascii=False, separators=(",", ":")).replace("</", "<\\/") + ";"
    page_html = page_html.replace("{{SITE_JSON}}", json.dumps(site_json, ensure_ascii=False, separators=(",", ":")).replace("</", "<\\/"))
    page_html = page_html.replace("{{I18N}}", i18n)
    page_html = page_html.replace("{{SCRIPTS}}", "\n".join(f'<script src="{root}assets/{s}?v={assets["hash"][s]}" defer></script>' for s in scripts))
    return page_html


def check_page(where, page):
    left = re.findall(r"\{\{[^}]+\}\}", page)
    if left:
        sys.exit(f"[{where}] Unreplaced placeholders: " + ", ".join(sorted(set(left))))
    if page.count("googletagmanager.com/gtag/js") != 1:
        sys.exit(f"[{where}] The page must contain exactly one Google tag.")
    loads = re.findall(r'<(?:script|img|iframe|source)\b[^>]*\bsrc="(https?://[^"]+)"', page)
    loads += re.findall(r'<link\b[^>]*\brel="(?:stylesheet|icon|preload|modulepreload)"[^>]*\bhref="(https?://[^"]+)"', page)
    loads = [u for u in loads if not u.startswith("https://www.googletagmanager.com/gtag/js")]
    if loads:
        sys.exit(f"[{where}] External resources are not allowed: " + ", ".join(loads))
    if page.count("<h1") != 1:
        sys.exit(f"[{where}] A page needs exactly one <h1>.")
    if 'href="https://tibiadata.com/"' not in page or 'data-credit="tibiadata"' not in page:
        sys.exit(f"[{where}] The footer credit to TibiaData is missing.")
    for p in PROVIDERS:
        if f'data-provider="{p["id"]}"' not in page:
            sys.exit(f"[{where}] The footer credit to {p['name']} is missing.")
    for needle, what in (('href="https://www.tibia.com/"', "the link to Tibia.com"), ('href="https://www.cipsoft.com/"', "the link to CipSoft"),
                         ("data-year", "the copyright year"), ("data-footer-profiles", "the creator's profiles")):
        if needle not in page:
            sys.exit(f"[{where}] The footer lacks {what}.")
    if ("data-download=" in page or "data-download-link" in page) and "data-download-notice" not in page:
        sys.exit(f"[{where}] A page offering the download must show the download notice (data-download-notice) next to it.")
    for m in re.finditer(r'<a\b[^>]*target="_blank"[^>]*>', page):
        if 'rel="noopener noreferrer' not in m.group(0):
            sys.exit(f"[{where}] A link opening a new tab lacks rel=\"noopener noreferrer\": {m.group(0)[:120]}")


def validate(locales):
    problems, english = [], locales[DEFAULT]
    for code, texts in locales.items():
        for key, value in texts.items():
            if key not in english:
                problems.append(f"[{code}] '{key}' is not an English key (typo or removed text)")
            elif type(value) is not type(english[key]):
                problems.append(f"[{code}] '{key}' must be a {'plural object' if isinstance(english[key], dict) else 'string'}")
            elif value not in ("", None) and signature(value) != signature(english[key]):
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
        lines += ["## Texts not used by the site", ""] + [f"- `{k}`" for k in unused] + [""]
    open(os.path.join(HERE, "i18n-report.md"), "w", encoding="utf-8", newline="\n").write("\n".join(lines))
    return review, unused


def not_found_page(assets):
    """404.html: deep data URLs without the hash (/de/tibia/character/Name) open their view; anything else gets links."""
    folders = "|".join(f for _, f, *_ in LANGUAGES if f)
    script = ("(function(){var p=location.pathname,m=p.match(/^(.*?\\/)(?:(" + folders + ")\\/)?tibia\\/(.+?)\\/?$/);"
              "if(m){location.replace(m[1]+(m[2]?m[2]+'/':'')+'tibia/#/'+m[3]);}})();")
    return ("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n"
            "<title>Page not found – Tibia Foundry</title>\n<meta name=\"robots\" content=\"noindex\">\n"
            f"<script>{script}</script>\n<link rel=\"stylesheet\" href=\"/assets/site.css?v={assets['hash']['site.css']}\">\n</head>\n"
            "<body><main class=\"wrap page-head\"><h1>Page not found</h1><p class=\"lead\">This page does not exist (any more).</p>"
            "<p class=\"actions\"><a class=\"btn btn-primary\" href=\"/\">Tibia Foundry</a><a class=\"btn\" href=\"/tibia/\">Tibia data</a>"
            "<a class=\"btn\" href=\"/downloads/\">Downloads</a></p>"
            "<p class=\"muted\"><a href=\"/pt-br/\" lang=\"pt-BR\">Português (Brasil)</a> · <a href=\"/pl/\" lang=\"pl\">Polski</a> · "
            "<a href=\"/es/\" lang=\"es\">Español</a> · <a href=\"/de/\" lang=\"de\">Deutsch</a></p>"
            "</main></body>\n</html>\n")


SOURCE_KINDS = {"official", "wiki", "calculator", "news"}
SOURCE_ALIASES = {"tibiawiki": "tibia.fandom.com", "tibiacom": "tibia.com"}   # a test's "from" name -> the source's host


def check_formulas():
    """--strict: every entry of formulas.json is documented and tested, ids are unique, and every formula a page names (page["calc"]) exists."""
    path = os.path.join(SRC, "formulas.json")
    problems, entries = [], {}
    for f in json.load(open(path, encoding="utf-8"))["formulas"]:
        fid = f.get("id", "?")
        if fid in entries:
            problems.append("%s: duplicate id in formulas.json" % fid)
        entries[fid] = f
        if not f.get("sources"):
            problems.append("%s: has no source" % fid)
        if not re.match(r"^\d{4}-\d{2}-\d{2}$", str(f.get("checkedOn", ""))):
            problems.append("%s: checkedOn must be YYYY-MM-DD" % fid)
        tests = f.get("tests", [])
        if len(tests) < 3:
            problems.append("%s: needs at least 3 tests (has %d)" % (fid, len(tests)))
        if not any(t.get("from") != "computed" for t in tests):
            problems.append("%s: needs a test taken from an official source, the wiki or another calculator" % fid)
        hosts = []
        for s in f.get("sources", []):
            if s.get("kind") not in SOURCE_KINDS:
                problems.append("%s: source %s has kind %r (allowed: %s)" % (fid, s.get("url"), s.get("kind"), ", ".join(sorted(SOURCE_KINDS))))
            host = urllib.parse.urlparse(s.get("url", "")).netloc.lower()
            hosts.append((s.get("kind"), host[4:] if host.startswith("www.") else host))
            if s.get("label") is not None and not str(s["label"]).startswith("calc.how.label."):
                problems.append("%s: source label %r must be a calc.how.label.* text key" % (fid, s["label"]))
        for t in tests:
            src = str(t.get("from", ""))
            if src == "computed":
                continue
            kind, _, name = src.partition(":")
            if not any(k == kind and (not name or h == SOURCE_ALIASES.get(name) or h.split(".")[0] == name) for k, h in hosts):
                problems.append("%s: test from %r names no listed source (a %s source%s)" % (fid, src, kind, " on " + name if name else ""))
    for page in PAGES:
        for fid in page.get("calc", []):
            if fid not in entries:
                problems.append("%s: formula '%s' is not in formulas.json" % (page["id"], fid))
    if problems:
        sys.exit("--strict: formula registry errors:\n  " + "\n  ".join(problems))



def main():
    args = sys.argv[1:]
    strict = "--strict" in args
    checksums, sizes = {}, {}
    if "--checksums" in args:
        path = args[args.index("--checksums") + 1]
        for line in open(path, encoding="utf-8"):
            m = re.match(r"^([0-9a-fA-F]{64})\s+\*?(.+?)\s*$", line)
            if m:
                name = os.path.basename(m.group(2))
                checksums[name] = m.group(1).lower()
                archive = os.path.join(os.path.dirname(os.path.abspath(path)), name)
                if os.path.exists(archive):
                    sizes[name] = os.path.getsize(archive)
    ver = version()
    today = datetime.date.today()
    locales, meta = load_locales()
    problems = validate(locales)
    if problems:
        sys.exit("Translation errors:\n  " + "\n  ".join(problems))
    if strict:
        check_formulas()
    check_menu(strict)
    check_search(strict)

    layout = open(os.path.join(SRC, "templates", "layout.html"), encoding="utf-8").read()
    templates = {p["template"]: open(os.path.join(SRC, "templates", p["template"]), encoding="utf-8").read() for p in PAGES}

    # Generated pages, assets and images are rebuilt from scratch; CNAME and robots.txt are kept as they are.
    os.makedirs(SITE, exist_ok=True)
    for name in os.listdir(SITE):
        if name in PROTECTED:
            continue
        path = os.path.join(SITE, name)
        shutil.rmtree(path) if os.path.isdir(path) else os.remove(path)

    os.makedirs(os.path.join(SITE, "assets"), exist_ok=True)
    assets = {"hash": {}}
    for name, data in asset_files(strict).items():
        if b"</script" in data.lower() or b"</style" in data.lower():
            sys.exit(f"{name} would close its own element.")
        os.makedirs(os.path.dirname(os.path.join(SITE, "assets", name)), exist_ok=True)
        open(os.path.join(SITE, "assets", name), "wb").write(data)
        assets["hash"][name] = hashlib.sha256(data).hexdigest()[:10]
    assets["version"] = assets["hash"]["site.css"]

    files = {}
    translators = {code: Translator(code, locales, ver, today) for code, *_ in LANGUAGES}
    dlinfo = downloads(ver, checksums, sizes) + (release_date(ver),)
    count = 0
    for code, folder, *_ in LANGUAGES:
        for page in PAGES:
            here = page_dir(code, page)
            root = ("../" * len(here.split("/"))) if here else ""

            def img(name, code=code, root=root):
                key = output_path(name, code)
                if key not in files:
                    ext, data = encode(load(name, code), name)
                    files[key] = (f"{key}.{ext}", data)
                    os.makedirs(os.path.dirname(os.path.join(SITE, files[key][0])), exist_ok=True)
                    with open(os.path.join(SITE, files[key][0]), "wb") as f:
                        f.write(data)
                return root + files[key][0]

            html = render(layout, templates, code, page, translators[code], translators, img, ver, assets, dlinfo)
            check_page(f"{code}/{page['id']}", html)
            out = os.path.join(SITE, here) if here else SITE
            os.makedirs(out, exist_ok=True)
            with open(os.path.join(out, "index.html"), "w", encoding="utf-8", newline="\n") as f:
                f.write(html)
            count += 1
        write_preview(translators[code], og_name(code))
    if "img/logo" not in files:
        ext, data = encode(load("logo"), "logo")
        files["img/logo"] = ("img/logo.png", data)
        open(os.path.join(SITE, "img", "logo.png"), "wb").write(data)

    # The site's own pixel art (icons sprite and ornament masks).
    pixel = os.path.join(SRC, "images", "pixel")
    os.makedirs(os.path.join(SITE, "img", "pixel"), exist_ok=True)
    for name in sorted(os.listdir(pixel)):
        if name.endswith(".svg"):
            shutil.copyfile(os.path.join(pixel, name), os.path.join(SITE, "img", "pixel", name))

    # /en/<page>/ remembers the choice of English and opens the English page.
    for page in PAGES:
        sub = page["path"].strip("/")
        d = os.path.join(SITE, "en", sub) if sub else os.path.join(SITE, "en")
        os.makedirs(d, exist_ok=True)
        target = "../" * (len(sub.split("/")) + 1 if sub else 1) + page["path"]
        with open(os.path.join(d, "index.html"), "w", encoding="utf-8", newline="\n") as f:
            f.write('<!doctype html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n<title>Tibia Foundry</title>\n'
                    f'<link rel="canonical" href="{ORIGIN}{page["path"]}">\n<meta name="robots" content="noindex">\n'
                    f'<meta http-equiv="refresh" content="0; url={target}?lang=en">\n'
                    "<script>try { localStorage.setItem('tf-lang', 'en'); } catch (e) {}\n"
                    f"location.replace({json.dumps(target)} + (location.protocol === 'file:' ? 'index.html' : '') + '?lang=en' + location.hash);</script>\n"
                    f'</head>\n<body><a href="{target}">Tibia Foundry</a></body>\n</html>\n')

    with open(os.path.join(SITE, "404.html"), "w", encoding="utf-8", newline="\n") as f:
        f.write(not_found_page(assets))
    open(os.path.join(SITE, ".nojekyll"), "w").close()

    with open(os.path.join(SITE, "sitemap.xml"), "w", encoding="utf-8", newline="\n") as f:
        f.write('<?xml version="1.0" encoding="UTF-8"?>\n'
                '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">\n')
        for page in PAGES:
            for code, *_ in LANGUAGES:
                f.write(f"  <url>\n    <loc>{page_url(code, page)}</loc>\n    <lastmod>{today.isoformat()}</lastmod>\n")
                for c, *_ in LANGUAGES:
                    f.write(f'    <xhtml:link rel="alternate" hreflang="{c}" href="{page_url(c, page)}"/>\n')
                f.write(f'    <xhtml:link rel="alternate" hreflang="x-default" href="{page_url(DEFAULT, page)}"/>\n  </url>\n')
        f.write("</urlset>\n")
    write_icons()
    for name, content in (("CNAME", "tibiafoundry.com"),  # exactly as in the Pages repository (no newline)
                          ("robots.txt", "User-agent: *\nAllow: /\n\nSitemap: https://tibiafoundry.com/sitemap.xml\n")):
        path = os.path.join(SITE, name)
        if not os.path.exists(path):
            open(path, "w", encoding="utf-8", newline="\n").write(content)

    review, unused = write_report(locales, meta, translators, ver, today.isoformat())
    total = sum(len(d) for _, d in files.values())
    print(f"site: {count} pages in {len(LANGUAGES)} languages; img: {len(files)} images, {total / 1024:,.0f} KB; version {ver}, {today.isoformat()}"
          + (f"; SHA-256 shown for {len(checksums)} archive(s)" if checksums else ""))
    missing = {c: sorted(t.missing) for c, t in translators.items() if t.missing}
    for code, keys in missing.items():
        print(f"  [{code}] {len(keys)} missing, shown in English: " + ", ".join(keys[:40]) + (" ..." if len(keys) > 40 else ""))
    for code, keys in review.items():
        if keys:
            print(f"  [{code}] same as English, not marked as intended: " + ", ".join(keys[:40]))
    if unused:
        print("  unused texts: " + ", ".join(unused))
    print("Report: website/i18n-report.md")
    if strict and (missing or unused or any(review.values())):
        sys.exit("--strict: translations are incomplete.")


if __name__ == "__main__":
    main()
