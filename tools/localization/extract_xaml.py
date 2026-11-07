"""
Moves the literal interface text of the application's XAML files into the English string table.

Every literal Text/Content/ToolTip/Header/Title/AutomationProperties.Name/... attribute (and
text setters in styles) becomes {loc:Tr Key}; the English text is added to
src/TibiaFoundry.Core/Localization/Resources/Strings.en.xaml.
Brand names, icon glyphs and technical tokens are left as they are.

Usage: python extract_xaml.py [--dry-run]
"""
import html, os, re, sys
import strings_io
from collections import OrderedDict, defaultdict

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
APP = os.path.join(ROOT, "src", "TibiaFoundry.App")
LOC_NS = 'xmlns:loc="clr-namespace:TibiaFoundry.App.Localization"'

ATTRS = ["Text", "Content", "ToolTip", "Header", "Title", "AutomationProperties.Name", "AutomationProperties.HelpText",
         "controls:Input.Placeholder", "Description", "Label"]
TEXT_SETTER_PROPS = {"Text", "Content", "ToolTip", "Header", "AutomationProperties.Name"}

# Never translated: product, game and company names, file formats (glossary: docs/Localization.md).
KEEP = {"Tibia", "Tibia Foundry", "TibiaWiki", "CipSoft", "Rookgaard", "GitHub", "JSON", "XML", "PNG", "JPG", "GIF", "DAT", "SPR",
        "OTB", "RCC", "SHA-256", "API", "OK", "ID", "IDs", "BMP", "WebP", "LZMA", "OGG", "WAV", "MP3", "FLAC", "CSV", "ZIP", "APNG",
        "Tibia Foundry - All your Tibia-Tools for TibiaWiki", "Tibia Foundry - All your ", "Tibia-Tools for ",
        "Tibia Foundry - All your Tibia-Tools for", "TibiaWiki (opens tibia.fandom.com)", "A link"}

SKIP_FILES = {"Colors.Light.xaml", "Colors.Dark.xaml", "BrandIcons.xaml"}


def area_of(path):
    name = os.path.splitext(os.path.basename(path))[0]
    if name in ("Styles", "Shared"): return "Common"
    if name == "MainWindow": return "Main"
    if path.replace("\\", "/").find("/Dialogs/") >= 0: return "Dialog"
    for suffix in ("Page", "Panel", "Window"):
        if name.endswith(suffix) and name != suffix: return name[: -len(suffix)]
    return name


def slug(text, words=6):
    parts = re.findall(r"[A-Za-z0-9]+", text)
    s = "".join(p[:1].upper() + p[1:] for p in parts[:words])
    return (s or "Text")[:48]


def translatable(value):
    if not value or value.startswith("{") and not value.startswith("{}"): return False
    text = html.unescape(value[2:] if value.startswith("{}") else value)
    if text.strip() in {k.strip() for k in KEEP}: return False
    if re.match(r"^\s*(https?://|www\.|[\w.-]+\.(com|io|org|net))", text): return False
    if not re.search(r"[A-Za-z]{2}", text): return False
    if re.fullmatch(r"[\s\W\d]*[A-Za-z]{1,3}[\s\W\d]*", text) and text.strip().upper() == text.strip(): return False  # "x", "px", "ms"
    return True


class Master:
    def __init__(self):
        self.data = strings_io.load("en")
        self.by_text = defaultdict(list)
        for k, v in self.data.items(): self.by_text[v].append(k)

    def key_for(self, text, area):
        for k in self.by_text.get(text, []):
            if k.startswith(area + ".") or k.startswith("Common."): return k
        base = f"{area}.{slug(text)}"
        key, n = base, 2
        while key in self.data and self.data[key] != text:
            key, n = f"{base}{n}", n + 1
        self.data[key] = text
        self.by_text[text].append(key)
        return key

    def save(self):
        strings_io.save("en", self.data)


def files():
    for d, _, fs in os.walk(APP):
        if os.sep + "bin" in d or os.sep + "obj" in d: continue
        for f in fs:
            if f.endswith(".xaml") and f not in SKIP_FILES: yield os.path.join(d, f)


def main():
    dry = "--dry-run" in sys.argv
    master = Master()
    # Pass 1: count in how many areas each text appears (shared short texts become Common.*).
    areas = defaultdict(set)
    attr_re = re.compile(r'(?<=\s)(' + "|".join(re.escape(a) for a in ATTRS) + r')="([^"]*)"')
    setter_re = re.compile(r'(<Setter\s+(?:TargetName="[^"]*"\s+)?Property="(' + "|".join(re.escape(p) for p in TEXT_SETTER_PROPS) + r')"\s+Value=")([^"]*)(")')
    contents = {}
    for path in files():
        with open(path, encoding="utf-8-sig", newline="") as f: text = f.read()
        contents[path] = text
        for m in attr_re.finditer(text):
            if translatable(m.group(2)): areas[html.unescape(m.group(2))].add(area_of(path))
        for m in setter_re.finditer(text):
            if translatable(m.group(3)): areas[html.unescape(m.group(3))].add(area_of(path))

    def area_for(text, path):
        a = area_of(path)
        return "Common" if len(areas[text]) > 1 and len(text) <= 60 else a

    changed = 0
    for path, text in contents.items():
        def repl_attr(m):
            name, value = m.group(1), m.group(2)
            if not translatable(value): return m.group(0)
            t = html.unescape(value[2:] if value.startswith("{}") else value)
            return f'{name}="{{loc:Tr {master.key_for(t, area_for(t, path))}}}"'

        def repl_setter(m):
            value = m.group(3)
            if not translatable(value): return m.group(0)
            t = html.unescape(value)
            return f'{m.group(1)}{{loc:Tr {master.key_for(t, area_for(t, path))}}}{m.group(4)}'

        new = attr_re.sub(repl_attr, text)
        new = setter_re.sub(repl_setter, new)
        if new != text and LOC_NS not in new:
            # Add the namespace to the root element (after the last xmlns of the first tag).
            first = re.search(r"<(?!\?|!)[^>]*>", new, re.S)
            tag = first.group(0)
            idx = first.start() + tag.rfind("xmlns")
            end = new.index('"', new.index('"', idx) + 1) + 1
            nl = "\r\n" if "\r\n" in new else "\n"
            new = new[:end] + nl + "             " + LOC_NS + new[end:]
        if new != text:
            changed += 1
            if not dry:
                with open(path, "w", encoding="utf-8", newline="") as f: f.write(new)
    if not dry: master.save()
    print(f"{changed} files changed, {len(master.data)} keys in the master table")


if __name__ == "__main__":
    main()
